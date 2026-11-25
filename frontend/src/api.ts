export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    public fields: string[] = [],
  ) {
    super(code);
  }
}
let csrfPromise: Promise<string> | undefined;
export function resetCsrf() {
  csrfPromise = undefined;
}
export function csrfToken(): Promise<string> {
  csrfPromise ??= fetch('/api/v1/auth/csrf', { credentials: 'same-origin', cache: 'no-store' })
    .then(async (response) => {
      if (!response.ok) throw new ApiError(response.status, 'CsrfUnavailable');
      const body = (await response.json()) as { token: string };
      return body.token;
    })
    .catch((error) => {
      csrfPromise = undefined;
      throw error;
    });
  return csrfPromise;
}
type RequestOptions = { method?: string; body?: unknown; signal?: AbortSignal; raw?: boolean };
export async function api<T = void>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? 'GET';
  const headers = new Headers({ Accept: 'application/json' });
  const mutation = !['GET', 'HEAD'].includes(method);
  if (mutation) headers.set('X-CSRF-Token', await csrfToken());
  let body: BodyInit | undefined;
  if (options.body !== undefined) {
    if (options.body instanceof FormData) body = options.body;
    else if (options.raw && options.body instanceof Blob) {
      body = options.body;
      headers.set('Content-Type', 'application/zip');
    } else {
      body = JSON.stringify(options.body);
      headers.set('Content-Type', 'application/json');
    }
  }
  const response = await fetch(`/api/v1/${path}`, {
    method,
    headers,
    body,
    signal: options.signal,
    credentials: 'same-origin',
    cache: 'no-store',
  });
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as {
      code?: string;
      errors?: Record<string, string[]>;
    };
    if (response.status === 401 && path !== 'auth/login')
      window.dispatchEvent(new Event('ludus:unauthorized'));
    if (problem.code === 'InvalidCsrfToken') resetCsrf();
    throw new ApiError(
      response.status,
      problem.code ?? `Http${response.status}`,
      Object.keys(problem.errors ?? {}),
    );
  }
  if (mutation && !path.startsWith('auth/') && path !== 'media/links')
    window.dispatchEvent(new Event('ludus:refresh'));
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
export async function login(credentials: { apiKey?: string; username?: string; password?: string }) {
  resetCsrf();
  await api('auth/login', { method: 'POST', body: credentials });
  resetCsrf();
  await csrfToken();
  window.dispatchEvent(new Event('ludus:reconnect'));
}
export function query(values: Record<string, string | number | undefined | null>) {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  });
  return params.toString();
}
export function mediaUrl(value: string): string {
  const url = new URL(value, window.location.origin);
  if (url.origin !== window.location.origin || url.pathname !== '/api/v1/media/content')
    throw new Error('InvalidMediaUrl');
  return url.pathname + url.search;
}
