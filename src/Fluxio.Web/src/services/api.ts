import type { CloseReason, ErrorInfo } from '../types/api';

/** The server answered with an error body ({ error: { code, message } }). */
export class ApiError extends Error {
  readonly code: string;
  readonly status: number;
  readonly received: number | null;
  readonly reason: CloseReason | null;

  constructor(status: number, info: ErrorInfo) {
    super(info.message);
    this.name = 'ApiError';
    this.status = status;
    this.code = info.code;
    this.received = info.received ?? null;
    this.reason = info.reason ?? null;
  }
}

/** The request never got an answer: the computer is unreachable, the network dropped, etc. */
export class NetworkError extends Error {
  constructor(message = 'network') {
    super(message);
    this.name = 'NetworkError';
  }
}

export const isAbort = (error: unknown) => error instanceof DOMException && error.name === 'AbortError';

export async function errorFromResponse(response: Response): Promise<ApiError> {
  try {
    const body = (await response.json()) as { error?: ErrorInfo };
    if (body?.error?.code) {
      return new ApiError(response.status, body.error);
    }
  } catch {
    // Not JSON (e.g. a proxy error page).
  }

  return new ApiError(response.status, { code: response.status >= 500 ? 'serverError' : 'badRequest', message: response.statusText });
}

/**
 * Small JSON client for the Fluxio API. The bearer token is sent in the Authorization header
 * (never in cookies or URLs), so other sites cannot make authenticated requests.
 */
export class ApiClient {
  private token: string | null;

  constructor(token: string | null = null) {
    this.token = token;
  }

  setToken(token: string | null) {
    this.token = token;
  }

  get authorization(): Record<string, string> {
    return this.token ? { Authorization: `Bearer ${this.token}` } : {};
  }

  async request<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    let response: Response;
    try {
      response = await fetch(path, {
        method,
        headers: {
          ...this.authorization,
          ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
        cache: 'no-store',
        credentials: 'omit',
        signal,
      });
    } catch (error) {
      if (isAbort(error)) {
        throw error;
      }

      throw new NetworkError();
    }

    if (!response.ok) {
      throw await errorFromResponse(response);
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }

  get<T>(path: string, signal?: AbortSignal) {
    return this.request<T>('GET', path, undefined, signal);
  }

  post<T>(path: string, body?: unknown, signal?: AbortSignal) {
    return this.request<T>('POST', path, body ?? {}, signal);
  }

  delete(path: string) {
    return this.request<void>('DELETE', path);
  }
}

export const delay = (ms: number, signal?: AbortSignal) =>
  new Promise<void>((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener(
      'abort',
      () => {
        clearTimeout(timer);
        reject(new DOMException('Aborted', 'AbortError'));
      },
      { once: true },
    );
  });
