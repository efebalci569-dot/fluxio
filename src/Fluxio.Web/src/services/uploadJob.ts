import type { BatchDto, ChunkResult, FileDto } from '../types/api';
import { isFinalFileState } from '../types/api';
import type { FileProgress, JobPhase, JobSnapshot } from '../state/jobTypes';
import { isFinalProgress } from '../state/jobTypes';
import { ApiClient, ApiError, delay, errorFromResponse, isAbort, NetworkError } from './api';
import { createSha256, type Sha256Hasher } from './hash';
import { addHistory } from './history';
import { SpeedMeter } from './speed';

const MAX_RETRIES = 8;

class Stopped extends Error {
  constructor() {
    super('stopped');
  }
}

/**
 * Sends files from this phone to the computer.
 *
 * Each file is read in chunks (never fully loaded into memory). Every chunk is hashed with
 * SHA-256 and sent with PUT …?offset=N. If the connection drops, the job asks the computer
 * how many bytes it stored and resumes from there. At the end the hash is sent and the
 * computer only keeps the file if its own hash of the received bytes matches.
 */
export class UploadJob {
  readonly batchId: string;
  private readonly files: FileProgress[];
  private readonly sources: File[];
  private readonly meter = new SpeedMeter();
  private phase: JobPhase;
  private started = false;
  private stopped = false;
  private currentFileId: string | null = null;
  private currentXhr: XMLHttpRequest | null = null;
  private skipCurrent = false;
  private notifyTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly createdAt = Date.now();

  constructor(
    private readonly api: ApiClient,
    batch: BatchDto,
    sources: File[],
    private readonly chunkSize: number,
    private readonly computerName: string,
    private readonly onChange: () => void,
  ) {
    this.batchId = batch.id;
    this.sources = sources;
    this.files = batch.files.map((f) => ({ fileId: f.id, name: f.name, size: f.size, done: 0, status: 'queued' }));
    this.phase = batch.status === 'inProgress' ? 'running' : 'waitingApproval';
  }

  get isActive() {
    return this.phase === 'running' || this.phase === 'waitingApproval';
  }

  snapshot(): JobSnapshot {
    return {
      batchId: this.batchId,
      direction: 'upload',
      phase: this.phase,
      files: this.files.map((f) => ({ ...f })),
      bytesPerSecond: this.phase === 'running' ? this.meter.bytesPerSecond : 0,
      createdAt: this.createdAt,
    };
  }

  /** Called with the computer's view of this batch on every state update. */
  applyServerState(batch: BatchDto) {
    if (batch.status === 'declined' && this.phase === 'waitingApproval') {
      this.phase = 'declined';
      this.files.forEach((f) => (f.status = 'cancelled'));
      this.record();
      this.emit(true);
      return;
    }

    if (batch.status === 'cancelled' && this.isActive) {
      this.stopAll('cancelled');
      return;
    }

    // Files cancelled on the computer side.
    for (const server of batch.files) {
      const local = this.files.find((f) => f.fileId === server.id);
      if (local && !isFinalProgress(local.status) && (server.state === 'cancelled' || server.state === 'failed')) {
        local.status = server.state === 'cancelled' ? 'cancelled' : 'failed';
        local.error = server.failure ?? undefined;
        if (this.currentFileId === local.fileId) {
          this.skipCurrent = true;
          this.currentXhr?.abort();
        }

        this.emit(true);
      }
    }

    if (batch.status === 'inProgress' && !this.started && this.phase !== 'cancelled') {
      this.start();
    }
  }

  start() {
    if (this.started) {
      return;
    }

    this.started = true;
    this.phase = 'running';
    this.emit(true);
    void this.run();
  }

  /** Cancel everything that has not finished yet. Files already saved on the computer stay. */
  async cancelAll() {
    this.stopAll('cancelled');
    try {
      await this.api.delete(`/api/v1/uploads/${this.batchId}`);
    } catch {
      // The computer cleans up on its own when the session ends.
    }
  }

  async cancelFile(fileId: string) {
    const file = this.files.find((f) => f.fileId === fileId);
    if (!file || isFinalProgress(file.status)) {
      return;
    }

    file.status = 'cancelled';
    if (this.currentFileId === fileId) {
      this.skipCurrent = true;
      this.currentXhr?.abort();
    }

    this.emit(true);
    try {
      await this.api.delete(`/api/v1/uploads/${this.batchId}/files/${fileId}`);
    } catch {
      // ignore
    }
  }

  /** The page is going away or the session ended. */
  abandon() {
    this.stopAll('interrupted');
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

    this.currentXhr?.abort();
    this.record();
    this.emit(true);
  }

  private async run() {
    for (let i = 0; i < this.files.length; i++) {
      const file = this.files[i];
      if (this.stopped) {
        break;
      }

      if (isFinalProgress(file.status)) {
        continue;
      }

      this.currentFileId = file.fileId;
      this.skipCurrent = false;
      try {
        await this.uploadFile(file, this.sources[i]);
      } catch (error) {
        if (this.stopped) {
          break;
        }

        if (error instanceof Stopped || isAbort(error)) {
          continue; // this file was cancelled
        }

        file.status = error instanceof ApiError && error.code === 'cancelled' ? 'cancelled' : 'failed';
        file.error = error instanceof ApiError ? error.code : error instanceof NetworkError ? 'network' : 'readFailed';
        this.emit(true);
      } finally {
        this.currentFileId = null;
      }
    }

    if (!this.stopped) {
      this.phase = 'done';
      this.record();
      this.emit(true);
    }
  }

  private async uploadFile(file: FileProgress, source: File) {
    file.status = 'transferring';
    this.emit(true);
    const hasher = createSha256();
    const size = source.size;
    let offset = 0;

    let current = size > 0 ? await this.readChunk(source, 0, Math.min(this.chunkSize, size), hasher) : null;
    while (current) {
      const start = offset;
      const end = start + current.length;
      // Read and hash the next chunk while this one is on the network.
      const next = end < size ? this.readChunk(source, end, Math.min(end + this.chunkSize, size), hasher) : Promise.resolve(null);
      next.catch(() => undefined);
      await this.putChunk(file, start, current);
      offset = end;
      current = await next;
    }

    this.throwIfSkipped();
    file.status = 'verifying';
    this.emit(true);
    const result = await this.completeWithRetry(file.fileId, hasher.hex());
    file.done = file.size;
    file.status = result.state === 'skipped' ? 'skipped' : 'completed';
    file.verified = result.verified;
    file.savedName = result.savedName;
    this.emit(true);
  }

  private async readChunk(source: File, start: number, end: number, hasher: Sha256Hasher): Promise<Uint8Array> {
    const bytes = new Uint8Array(await source.slice(start, end).arrayBuffer());
    if (bytes.length !== end - start) {
      throw new Error('readFailed');
    }

    hasher.update(bytes);
    return bytes;
  }

  /** Sends one chunk, resuming from the server's position after network errors. */
  private async putChunk(file: FileProgress, start: number, bytes: Uint8Array) {
    let sent = 0;
    let failures = 0;
    while (sent < bytes.length) {
      this.throwIfSkipped();
      try {
        const result = await this.send(file, start + sent, bytes.subarray(sent));
        sent = result.received - start;
        failures = 0;
      } catch (error) {
        if (isAbort(error)) {
          throw new Stopped();
        }

        if (error instanceof ApiError) {
          if (error.code === 'offsetMismatch' && error.received !== null && error.received >= start && error.received <= start + bytes.length) {
            sent = error.received - start;
            continue;
          }

          throw error;
        }

        failures++;
        if (failures > MAX_RETRIES) {
          throw new NetworkError();
        }

        await delay(Math.min(500 * 2 ** failures, 8000));
        this.throwIfSkipped();
        try {
          const status = await this.api.get<ChunkResult>(`/api/v1/uploads/${this.batchId}/files/${file.fileId}`);
          if (status.received < start || status.received > start + bytes.length) {
            throw new ApiError(409, { code: 'offsetMismatch', message: 'desync' });
          }

          sent = status.received - start;
          file.done = status.received;
        } catch (statusError) {
          if (statusError instanceof ApiError) {
            throw statusError;
          }
          // Still unreachable: the loop retries.
        }
      }
    }

    file.done = start + bytes.length;
    this.meterSample();
  }

  private send(file: FileProgress, offset: number, body: Uint8Array): Promise<ChunkResult> {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      this.currentXhr = xhr;
      xhr.open('PUT', `/api/v1/uploads/${this.batchId}/files/${file.fileId}?offset=${offset}`);
      const auth = this.api.authorization.Authorization;
      if (auth) {
        xhr.setRequestHeader('Authorization', auth);
      }

      xhr.setRequestHeader('Content-Type', 'application/octet-stream');
      xhr.timeout = 120_000;
      xhr.upload.onprogress = (event) => {
        file.done = offset + event.loaded;
        this.meterSample();
        this.emit(false);
      };
      xhr.onload = () => {
        this.currentXhr = null;
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            resolve(JSON.parse(xhr.responseText) as ChunkResult);
          } catch {
            reject(new NetworkError());
          }

          return;
        }

        void errorFromResponse(new Response(xhr.responseText, { status: xhr.status })).then(reject);
      };
      xhr.onerror = () => {
        this.currentXhr = null;
        reject(new NetworkError());
      };
      xhr.ontimeout = xhr.onerror;
      xhr.onabort = () => {
        this.currentXhr = null;
        reject(new DOMException('Aborted', 'AbortError'));
      };
      xhr.send(body as Uint8Array<ArrayBuffer>);
    });
  }

  private async completeWithRetry(fileId: string, sha256: string): Promise<FileDto> {
    for (let attempt = 0; ; attempt++) {
      try {
        return await this.api.post<FileDto>(`/api/v1/uploads/${this.batchId}/files/${fileId}/complete`, { sha256 });
      } catch (error) {
        if (!(error instanceof NetworkError) || attempt >= MAX_RETRIES) {
          throw error;
        }

        await delay(Math.min(500 * 2 ** attempt, 8000));
      }
    }
  }

  private throwIfSkipped() {
    if (this.stopped || this.skipCurrent) {
      throw new Stopped();
    }
  }

  private meterSample() {
    const total = this.files.reduce((sum, f) => sum + (f.status === 'completed' ? f.size : f.done), 0);
    this.meter.sample(total);
  }

  private recorded = false;

  private record() {
    if (this.recorded) {
      return;
    }

    this.recorded = true;
    for (const file of this.files) {
      addHistory({
        direction: 'sent',
        name: file.name,
        size: file.size,
        result: this.phase === 'declined' ? 'declined' : file.status === 'completed' || file.status === 'skipped' ? 'completed' : file.status === 'failed' ? 'failed' : 'cancelled',
        verified: !!file.verified,
        computer: this.computerName,
      });
    }
  }

  /** Coalesces frequent progress events into ~8 UI updates per second. */
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

export const serverFileIsFinal = (file: FileDto) => isFinalFileState(file.state);
