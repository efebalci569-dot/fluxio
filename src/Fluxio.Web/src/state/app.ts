import type { BatchDto, CloseReason, PairResponse, PhoneState } from '../types/api';
import { ApiClient, ApiError, delay, isAbort, NetworkError } from '../services/api';
import { describeDevice, isIOS } from '../services/device';
import { DownloadJob } from '../services/downloadJob';
import { UploadJob } from '../services/uploadJob';
import type { JobSnapshot } from './jobTypes';

export type Phase =
  | 'starting'
  | 'pairing'
  | 'noLink'
  | 'pairFailed'
  | 'awaitingApproval'
  | 'connected'
  | 'ended';

export type View = 'home' | 'send' | 'receive' | 'history';

export interface EndInfo {
  /** closed | expired | unauthorized */
  kind: 'closed' | 'expired' | 'unauthorized';
  reason: CloseReason | null;
}

export interface AppSnapshot {
  phase: Phase;
  view: View;
  phone: PhoneState | null;
  /** ok = last request worked; reconnecting = a few failures; lost = still failing. */
  connection: 'ok' | 'reconnecting' | 'lost';
  pairError: string | null;
  ended: EndInfo | null;
  uploads: JobSnapshot[];
  downloads: JobSnapshot[];
  /** Offers waiting for an answer on this phone. */
  pendingOffers: BatchDto[];
  busy: boolean;
  actionError: string | null;
}

const TOKEN_KEY = 'fluxio.session';
const LONG_POLL_SECONDS = 25;
/** iOS Safari keeps Blobs in memory; Chromium pages them to disk. */
const IOS_IN_PAGE_LIMIT = 512 * 1024 * 1024;

function readToken(): string | null {
  try {
    return sessionStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

function writeToken(token: string | null) {
  try {
    if (token) {
      sessionStorage.setItem(TOKEN_KEY, token);
    } else {
      sessionStorage.removeItem(TOKEN_KEY);
    }
  } catch {
    // Storage disabled: the session still works until the page is reloaded.
  }
}

/** Reads "#s=<session>&k=<secret>" from the QR code URL. */
export function parsePairingFragment(hash: string): { sessionId: string; secret: string } | null {
  const params = new URLSearchParams(hash.replace(/^#/, ''));
  const sessionId = params.get('s');
  const secret = params.get('k');
  if (!sessionId || !secret || !/^[A-Za-z0-9_-]{16,64}$/.test(sessionId) || !/^[A-Za-z0-9_-]{32,128}$/.test(secret)) {
    return null;
  }

  return { sessionId, secret };
}

/**
 * The phone-side application: pairing, the long-polling loop that mirrors the computer's
 * state, and the transfer jobs. React reads immutable snapshots via useSyncExternalStore.
 */
export class FluxioApp {
  private readonly api = new ApiClient();
  private readonly listeners = new Set<() => void>();
  private readonly uploadJobs = new Map<string, UploadJob>();
  private readonly downloadJobs = new Map<string, DownloadJob>();
  private state: AppSnapshot = {
    phase: 'starting',
    view: 'home',
    phone: null,
    connection: 'ok',
    pairError: null,
    ended: null,
    uploads: [],
    downloads: [],
    pendingOffers: [],
    busy: false,
    actionError: null,
  };
  private pollAbort: AbortController | null = null;
  private polling = false;
  private fastPollTimer: ReturnType<typeof setInterval> | null = null;
  private failures = 0;

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  getSnapshot = () => this.state;

  get hasActiveTransfers() {
    return [...this.uploadJobs.values()].some((j) => j.isActive) || [...this.downloadJobs.values()].some((j) => j.isActive);
  }

  get inPageDownloadLimit() {
    const serverLimit = this.state.phone?.limits.inPageDownloadLimit ?? IOS_IN_PAGE_LIMIT;
    return isIOS() ? Math.min(serverLimit, IOS_IN_PAGE_LIMIT) : serverLimit;
  }

  async start() {
    document.addEventListener('visibilitychange', this.onVisibility);
    const pairing = parsePairingFragment(window.location.hash);
    if (pairing) {
      // Remove the secret from the address bar and history right away.
      history.replaceState(null, '', window.location.pathname);
      await this.pair(pairing.sessionId, pairing.secret);
      return;
    }

    const token = readToken();
    if (token) {
      this.api.setToken(token);
      this.set({ phase: 'pairing' });
      this.startPolling();
      return;
    }

    this.set({ phase: 'noLink' });
  }

  setView(view: View) {
    if (view !== this.state.view) {
      if (view !== 'home') {
        history.pushState({ view }, '');
      }

      this.set({ view, actionError: null });
    }
  }

  /** Browser back button. */
  onPopState = () => {
    this.set({ view: 'home', actionError: null });
  };

  clearActionError() {
    this.set({ actionError: null });
  }

  async sendFiles(files: File[]) {
    const phone = this.state.phone;
    if (!phone || files.length === 0) {
      return;
    }

    if (files.length > phone.limits.maxFilesPerBatch) {
      this.set({ actionError: 'tooManyFiles' });
      return;
    }

    if (files.some((f) => f.size > phone.limits.maxFileSize)) {
      this.set({ actionError: 'tooLarge' });
      return;
    }

    this.set({ busy: true, actionError: null });
    try {
      const batch = await this.api.post<BatchDto>('/api/v1/uploads', {
        files: files.map((f) => ({ name: f.name, size: f.size, type: f.type || null })),
      });
      const job = new UploadJob(this.api, batch, files, phone.limits.chunkSize, phone.computerName, this.refreshJobs);
      this.uploadJobs.set(batch.id, job);
      if (batch.status === 'inProgress') {
        job.start();
      }

      this.refreshJobs();
    } catch (error) {
      this.set({ actionError: this.codeOf(error) });
    } finally {
      this.set({ busy: false });
    }
  }

  async respondToOffer(batchId: string, accept: boolean) {
    const offer = this.state.phone?.offers.find((o) => o.id === batchId);
    if (!offer) {
      return;
    }

    this.set({ busy: true, actionError: null });
    try {
      const batch = await this.api.post<BatchDto>(`/api/v1/offers/${batchId}/${accept ? 'accept' : 'decline'}`);
      if (accept) {
        const job = new DownloadJob(this.api, batch, this.inPageDownloadLimit, this.state.phone?.computerName ?? '', this.refreshJobs);
        this.downloadJobs.set(batchId, job);
        job.start();
      }

      this.refreshJobs();
    } catch (error) {
      this.set({ actionError: this.codeOf(error) });
    } finally {
      this.set({ busy: false });
    }
  }

  cancelUpload(batchId: string, fileId?: string) {
    const job = this.uploadJobs.get(batchId);
    void (fileId ? job?.cancelFile(fileId) : job?.cancelAll());
  }

  cancelDownload(batchId: string, fileId?: string) {
    const job = this.downloadJobs.get(batchId);
    void (fileId ? job?.cancelFile(fileId) : job?.cancelAll());
  }

  saveAgain(batchId: string, fileId: string) {
    this.downloadJobs.get(batchId)?.saveAgain(fileId);
  }

  /** Server-side upload batches this page has no job for (e.g. after a reload): can only be cancelled. */
  orphanUploads(): BatchDto[] {
    return (this.state.phone?.uploads ?? []).filter(
      (b) => !this.uploadJobs.has(b.id) && (b.status === 'awaitingApproval' || b.status === 'inProgress'),
    );
  }

  async cancelOrphan(batchId: string) {
    try {
      await this.api.delete(`/api/v1/uploads/${batchId}`);
    } catch {
      // ignore
    }
  }

  async disconnect() {
    this.uploadJobs.forEach((j) => j.abandon());
    this.downloadJobs.forEach((j) => j.abandon());
    try {
      await this.api.post('/api/v1/leave');
    } catch {
      // The computer may already be gone.
    }

    this.end({ kind: 'closed', reason: 'deviceLeft' });
  }

  retryNow() {
    this.failures = 0;
    this.set({ connection: 'reconnecting' });
    this.pollAbort?.abort();
    if (!this.polling) {
      this.startPolling();
    }
  }

  // ---- internals ----

  private async pair(sessionId: string, secret: string) {
    this.set({ phase: 'pairing' });
    try {
      const response = await this.api.post<PairResponse>('/api/v1/pair', { sessionId, secret, deviceName: describeDevice() });
      writeToken(response.token);
      this.api.setToken(response.token);
      this.applyState(response.state);
      this.startPolling();
    } catch (error) {
      this.set({ phase: 'pairFailed', pairError: this.codeOf(error) });
    }
  }

  private startPolling() {
    if (this.polling) {
      return;
    }

    this.polling = true;
    void this.pollLoop();
  }

  private async pollLoop() {
    let first = true;
    while (this.polling) {
      const controller = new AbortController();
      this.pollAbort = controller;
      const timeout = setTimeout(() => controller.abort(), (LONG_POLL_SECONDS + 10) * 1000);
      try {
        const version = this.state.phone?.version;
        const query = first || version === undefined ? '' : `?since=${version}&wait=${LONG_POLL_SECONDS}`;
        const phone = await this.api.get<PhoneState>(`/api/v1/state${query}`, controller.signal);
        first = false;
        this.failures = 0;
        if (this.state.connection !== 'ok') {
          this.set({ connection: 'ok' });
        }

        this.applyState(phone);
      } catch (error) {
        if (error instanceof ApiError) {
          if (error.code === 'sessionClosed' || error.code === 'sessionExpired') {
            this.end({ kind: error.code === 'sessionExpired' ? 'expired' : 'closed', reason: error.reason });
            return;
          }

          if (error.code === 'unauthorized') {
            this.end({ kind: 'unauthorized', reason: null });
            return;
          }

          await delay(2000).catch(() => undefined);
          continue;
        }

        if (isAbort(error) && document.visibilityState === 'visible' && this.failures === 0) {
          // Aborted on purpose (visibility change / retry) or the long poll timed out: poll again.
          first = true;
          continue;
        }

        if (error instanceof NetworkError || isAbort(error)) {
          this.failures++;
          first = true;
          this.set({ connection: this.failures >= 3 ? 'lost' : 'reconnecting' });
          await delay(Math.min(1000 * this.failures, 5000)).catch(() => undefined);
          continue;
        }

        await delay(2000).catch(() => undefined);
      } finally {
        clearTimeout(timeout);
      }
    }
  }

  private applyState(phone: PhoneState) {
    if (phone.status === 'closed' || phone.status === 'expired' || phone.status === 'failed') {
      this.end({ kind: phone.status === 'expired' ? 'expired' : 'closed', reason: phone.closeReason });
      return;
    }

    for (const batch of phone.uploads) {
      this.uploadJobs.get(batch.id)?.applyServerState(batch);
    }

    for (const batch of phone.offers) {
      this.downloadJobs.get(batch.id)?.applyServerState(batch);
    }

    const pendingOffers = phone.offers.filter((o) => o.status === 'awaitingApproval');
    const newOffer = pendingOffers.some((o) => !this.state.pendingOffers.some((p) => p.id === o.id));
    if (newOffer && 'vibrate' in navigator) {
      try {
        navigator.vibrate?.(120);
      } catch {
        // ignore
      }
    }

    this.set({
      phone,
      pendingOffers,
      phase: phone.approved ? 'connected' : 'awaitingApproval',
    });
    this.updateFastPolling();
  }

  /** Browser-managed downloads report progress through the computer: poll every second meanwhile. */
  private updateFastPolling() {
    const needed = [...this.downloadJobs.values()].some((j) => j.needsFastPolling);
    if (needed && !this.fastPollTimer) {
      this.fastPollTimer = setInterval(() => {
        this.api
          .get<PhoneState>('/api/v1/state')
          .then((phone) => this.applyState(phone))
          .catch(() => undefined);
      }, 1000);
    } else if (!needed && this.fastPollTimer) {
      clearInterval(this.fastPollTimer);
      this.fastPollTimer = null;
    }
  }

  private refreshJobs = () => {
    const sortByAge = (a: JobSnapshot, b: JobSnapshot) => b.createdAt - a.createdAt;
    this.set({
      uploads: [...this.uploadJobs.values()].map((j) => j.snapshot()).sort(sortByAge),
      downloads: [...this.downloadJobs.values()].map((j) => j.snapshot()).sort(sortByAge),
    });
    this.updateFastPolling();
  };

  private end(info: EndInfo) {
    this.polling = false;
    this.pollAbort?.abort();
    if (this.fastPollTimer) {
      clearInterval(this.fastPollTimer);
      this.fastPollTimer = null;
    }

    this.uploadJobs.forEach((j) => j.abandon());
    this.downloadJobs.forEach((j) => j.abandon());
    writeToken(null);
    this.api.setToken(null);
    this.set({ phase: 'ended', ended: info, view: 'home' });
    this.refreshJobs();
  }

  private onVisibility = () => {
    if (document.visibilityState === 'visible' && this.polling) {
      // Mobile browsers often freeze background tabs; refresh immediately on return.
      this.pollAbort?.abort();
    }
  };

  private codeOf(error: unknown): string {
    if (error instanceof ApiError) {
      return error.code;
    }

    if (error instanceof NetworkError) {
      return 'network';
    }

    return 'unknown';
  }

  private set(partial: Partial<AppSnapshot>) {
    this.state = { ...this.state, ...partial };
    this.listeners.forEach((l) => l());
  }
}

export const app = new FluxioApp();
