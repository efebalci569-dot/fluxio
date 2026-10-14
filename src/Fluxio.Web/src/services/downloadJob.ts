import type { BatchDto, FileDto } from '../types/api';
import type { FileProgress, JobPhase, JobSnapshot } from '../state/jobTypes';
import { isFinalProgress } from '../state/jobTypes';
import { ApiClient, ApiError, delay, errorFromResponse, isAbort, NetworkError } from './api';
import { createSha256 } from './hash';
import { addHistory } from './history';
import { saveBlob, startBrowserDownload } from './save';
import { SpeedMeter } from './speed';

const MAX_RETRIES = 6;
/** Received data is grouped into Blobs of this size so the JS heap does not hold the whole file. */
const BLOB_SEGMENT = 16 * 1024 * 1024;
/** A browser-managed download with no progress for this long is reported as interrupted. */
const STALL_MS = 90_000;

class Skipped extends Error {
  constructor() {
    super('skipped');
  }
}

/**
 * Receives files offered by the computer.
 *
 * Files up to `inPageLimit` are streamed by this page, hashed while they arrive and confirmed
 * with the computer (SHA-256) before they are handed to the browser to save. Larger files go
 * through the browser's own download manager (a single-use link); their progress comes from
 * the computer, and the page cannot verify their content — the UI says so.
 */
export class DownloadJob {
  readonly batchId: string;
  private readonly files: FileProgress[];
  private readonly blobs = new Map<string, Blob>();
  private readonly meter = new SpeedMeter();
  private phase: JobPhase = 'running';
  private stopped = false;
  private current: { fileId: string; controller: AbortController } | null = null;
  private browserWaiters = new Map<string, { resolve: () => void; lastProgress: number; lastChange: number }>();
  private notifyTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly createdAt = Date.now();
  private recorded = false;

  constructor(
    private readonly api: ApiClient,
    batch: BatchDto,
    inPageLimit: number,
    private readonly computerName: string,
    private readonly onChange: () => void,
  ) {
    this.batchId = batch.id;
    this.files = batch.files.map((f) => ({
      fileId: f.id,
      name: f.name,
      size: f.size,
      done: 0,
      status: 'queued',
      mode: f.size <= inPageLimit ? 'page' : 'browser',
    }));
  }

  get isActive() {
    return this.phase === 'running';
  }

  /** True while a file is being fetched by the browser's download manager (progress comes from polling). */
  get needsFastPolling() {
    return this.browserWaiters.size > 0;
  }

  snapshot(): JobSnapshot {
    return {
      batchId: this.batchId,
      direction: 'download',
      phase: this.phase,
      files: this.files.map((f) => ({ ...f, canSaveAgain: this.blobs.has(f.fileId) })),
      bytesPerSecond: this.phase === 'running' ? this.meter.bytesPerSecond : 0,
      createdAt: this.createdAt,
    };
  }

  start() {
    this.emit(true);
    void this.run();
  }

  saveAgain(fileId: string) {
    const blob = this.blobs.get(fileId);
    const file = this.files.find((f) => f.fileId === fileId);
    if (blob && file) {
      saveBlob(blob, file.name);
    }
  }

  async cancelAll() {
    this.stopAll('cancelled');
    try {
      await this.api.delete(`/api/v1/offers/${this.batchId}`);
    } catch {
      // ignore
    }
  }

  async cancelFile(fileId: string) {
    const file = this.files.find((f) => f.fileId === fileId);
    if (!file || isFinalProgress(file.status)) {
      return;
    }

    file.status = 'cancelled';
    if (this.current?.fileId === fileId) {
      this.current.controller.abort();
    }

    this.browserWaiters.get(fileId)?.resolve();
    this.emit(true);
    try {
      await this.api.delete(`/api/v1/offers/${this.batchId}/files/${fileId}`);
    } catch {
      // ignore
    }
  }

  abandon() {
    this.stopAll('interrupted');
  }

  /** Server view of the batch: used for cancellations by the computer and browser-mode progress. */
  applyServerState(batch: BatchDto) {
    if (batch.status === 'cancelled' && this.isActive) {
      this.stopAll('cancelled');
      return;
    }

    for (const server of batch.files) {
      const local = this.files.find((f) => f.fileId === server.id);
      if (!local || isFinalProgress(local.status)) {
        continue;
      }

      if (server.state === 'cancelled' || (server.state === 'failed' && local.mode === 'browser')) {
        local.status = server.state === 'cancelled' ? 'cancelled' : 'failed';
        local.error = server.failure ?? undefined;
        if (this.current?.fileId === local.fileId) {
          this.current.controller.abort();
        }

        this.browserWaiters.get(local.fileId)?.resolve();
        this.emit(true);
        continue;
      }

      const waiter = this.browserWaiters.get(local.fileId);
      if (waiter) {
        this.updateBrowserProgress(local, server, waiter);
      }
    }
  }

  private updateBrowserProgress(local: FileProgress, server: FileDto, waiter: { resolve: () => void; lastProgress: number; lastChange: number }) {
    if (server.transferred !== waiter.lastProgress) {
      waiter.lastProgress = server.transferred;
      waiter.lastChange = Date.now();
    }

    local.done = server.transferred;
    this.meterSample();
    if (server.state === 'completed') {
      local.status = 'completed';
      local.done = local.size;
      local.verified = false;
      waiter.resolve();
    } else if (Date.now() - waiter.lastChange > STALL_MS) {
      local.status = 'failed';
      local.error = 'interrupted';
      waiter.resolve();
    }

    this.emit(false);
  }

  private stopAll(phase: JobPhase) {
    if (!this.isActive) {
      return;
    }

    this.stopped = true;
    this.phase = phase;
    for (const file of this.files) {
      if (!isFinalProgress(file.status)) {
        file.status = 'cancelled';
      }
    }

    this.current?.controller.abort();
    this.browserWaiters.forEach((w) => w.resolve());
    this.record();
    this.emit(true);
  }

  private async run() {
    for (const file of this.files) {
      if (this.stopped) {
        break;
      }

      if (isFinalProgress(file.status)) {
        continue;
      }

      try {
        if (file.mode === 'page') {
          await this.downloadInPage(file);
        } else {
          await this.downloadWithBrowser(file);
        }
      } catch (error) {
        if (this.stopped) {
          break;
        }

        if (error instanceof Skipped || isAbort(error)) {
          continue;
        }

        file.status = 'failed';
        file.error = error instanceof ApiError ? (error.code === 'integrityFailed' ? 'integrityMismatch' : error.code) : 'network';
        this.emit(true);
      }
    }

    if (!this.stopped) {
      this.phase = 'done';
      this.record();
      this.emit(true);
    }
  }

  private async downloadInPage(file: FileProgress) {
    const url = `/api/v1/offers/${this.batchId}/files/${file.fileId}/content`;
    const controller = new AbortController();
    this.current = { fileId: file.fileId, controller };
    const hasher = createSha256();
    const segments: Blob[] = [];
    let pending: Uint8Array[] = [];
    let pendingBytes = 0;
    let received = 0;
    let failures = 0;
    file.status = 'transferring';
    this.emit(true);

    const consume = (chunk: Uint8Array) => {
      hasher.update(chunk);
      pending.push(chunk);
      pendingBytes += chunk.length;
      received += chunk.length;
      if (pendingBytes >= BLOB_SEGMENT) {
        segments.push(new Blob(pending as Uint8Array<ArrayBuffer>[]));
        pending = [];
        pendingBytes = 0;
      }

      file.done = received;
      this.meterSample();
      this.emit(false);
    };

    try {
      for (;;) {
        try {
          const response = await fetch(url, {
            headers: { ...this.api.authorization, ...(received > 0 ? { Range: `bytes=${received}-` } : {}) },
            cache: 'no-store',
            credentials: 'omit',
            signal: controller.signal,
          });
          if (!response.ok) {
            throw await errorFromResponse(response);
          }

          if (received > 0 && response.status !== 206) {
            throw new ApiError(response.status, { code: 'badRequest', message: 'Range not honoured' });
          }

          if (response.body) {
            const reader = response.body.getReader();
            for (;;) {
              const { done, value } = await reader.read();
              if (done) {
                break;
              }

              consume(value);
            }
          } else {
            consume(new Uint8Array(await response.arrayBuffer()));
          }

          if (received >= file.size) {
            break;
          }

          throw new NetworkError('incomplete');
        } catch (error) {
          if (isAbort(error) || error instanceof ApiError) {
            throw error;
          }

          failures++;
          if (failures > MAX_RETRIES) {
            throw new NetworkError();
          }

          await delay(Math.min(500 * 2 ** failures, 8000), controller.signal);
        }
      }
    } catch (error) {
      if (isAbort(error)) {
        throw new Skipped();
      }

      throw error;
    } finally {
      this.current = null;
    }

    if (pending.length > 0) {
      segments.push(new Blob(pending as Uint8Array<ArrayBuffer>[]));
    }

    file.status = 'verifying';
    this.emit(true);
    await this.confirm(file, hasher.hex());

    const blob = new Blob(segments, { type: 'application/octet-stream' });
    this.blobs.set(file.fileId, blob);
    file.status = 'completed';
    file.verified = true;
    file.done = file.size;
    this.emit(true);
    saveBlob(blob, file.name);
  }

  /** The computer compares our hash with the hash of the original file. */
  private async confirm(file: FileProgress, sha256: string) {
    for (let attempt = 0; ; attempt++) {
      try {
        await this.api.post(`/api/v1/offers/${this.batchId}/files/${file.fileId}/confirm`, { sha256 });
        return;
      } catch (error) {
        const retryable = error instanceof NetworkError || (error instanceof ApiError && error.code === 'notReady');
        if (!retryable || attempt >= 40) {
          throw error;
        }

        await delay(error instanceof NetworkError ? Math.min(500 * 2 ** attempt, 8000) : 1500);
      }
    }
  }

  private async downloadWithBrowser(file: FileProgress) {
    const { url } = await this.api.post<{ url: string }>(`/api/v1/offers/${this.batchId}/files/${file.fileId}/ticket`);
    file.status = 'transferring';
    const finished = new Promise<void>((resolve) => {
      this.browserWaiters.set(file.fileId, { resolve, lastProgress: 0, lastChange: Date.now() });
    });
    this.emit(true);
    startBrowserDownload(url, file.name);
    await finished;
    this.browserWaiters.delete(file.fileId);
  }

  private meterSample() {
    const total = this.files.reduce((sum, f) => sum + (f.status === 'completed' ? f.size : f.done), 0);
    this.meter.sample(total);
  }

  private record() {
    if (this.recorded) {
      return;
    }

    this.recorded = true;
    for (const file of this.files) {
      addHistory({
        direction: 'received',
        name: file.name,
        size: file.size,
        result: file.status === 'completed' ? 'completed' : file.status === 'failed' ? 'failed' : 'cancelled',
        verified: !!file.verified,
        computer: this.computerName,
      });
    }
  }

  private emit(immediate: boolean) {
    if (immediate) {
      if (this.notifyTimer) {
        clearTimeout(this.notifyTimer);
        this.notifyTimer = null;
      }

      this.onChange();
      return;
    }

    if (!this.notifyTimer) {
      this.notifyTimer = setTimeout(() => {
        this.notifyTimer = null;
        this.onChange();
      }, 125);
    }
  }
}
