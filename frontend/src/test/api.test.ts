import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, login, mediaUrl, query, resetCsrf } from '../api';
import { payload } from '../forms';
import { clientFields, platformFields, providerFields } from '../settings-schema';

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
describe('API session boundaries', () => {
  beforeEach(() => resetCsrf());
  it('refreshes the CSRF token after account sign-in and never uses an API key in browser requests', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: 'anonymous' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(json({ token: 'session' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);
    await login({ username: 'admin', password: 'private-password' });
    await api('games/1', { method: 'PUT', body: { monitored: true } });
    expect(fetch.mock.calls.map(([url]) => url)).toEqual([
      '/api/v1/auth/csrf',
      '/api/v1/auth/login',
      '/api/v1/auth/csrf',
      '/api/v1/games/1',
    ]);
    expect(fetch.mock.calls[1][1].headers.get('X-CSRF-Token')).toBe('anonymous');
    expect(fetch.mock.calls[3][1].headers.get('X-CSRF-Token')).toBe('session');
    expect(fetch.mock.calls[3][1].headers.has('X-Api-Key')).toBe(false);
    expect(fetch.mock.calls[3][1].credentials).toBe('same-origin');
    expect(fetch.mock.calls[1][1].body).toContain('private-password');
    expect(fetch.mock.calls[3][1].body).not.toContain('private-password');
  });
  it('shares CSRF initialization between concurrent writes', async () => {
    const fetch = vi.fn(async (url: string) =>
      url.endsWith('/csrf') ? json({ token: 'shared' }) : new Response(null, { status: 204 }),
    );
    vi.stubGlobal('fetch', fetch);
    await Promise.all([api('a', { method: 'POST' }), api('b', { method: 'DELETE' })]);
    expect(fetch.mock.calls.filter(([url]) => url.endsWith('/csrf'))).toHaveLength(1);
  });
  it('does not replay a failed mutation and obtains a new token on the next action', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: 'old' }))
      .mockResolvedValueOnce(json({ code: 'InvalidCsrfToken' }, 400))
      .mockResolvedValueOnce(json({ token: 'new' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);
    await expect(api('grab', { method: 'POST' })).rejects.toMatchObject({ code: 'InvalidCsrfToken' });
    expect(fetch).toHaveBeenCalledTimes(2);
    await api('grab', { method: 'POST' });
    expect(fetch.mock.calls[3][1].headers.get('X-CSRF-Token')).toBe('new');
  });
  it('dispatches expired session events but not for a rejected login', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url.endsWith('csrf') ? json({ token: 't' }) : new Response(null, { status: 401 }),
      ),
    );
    const event = vi.fn();
    window.addEventListener('ludus:unauthorized', event);
    await expect(api('games')).rejects.toBeInstanceOf(ApiError);
    expect(event).toHaveBeenCalledTimes(1);
    await expect(api('auth/login', { method: 'POST' })).rejects.toBeInstanceOf(ApiError);
    expect(event).toHaveBeenCalledTimes(1);
    window.removeEventListener('ludus:unauthorized', event);
  });
  it('keeps multipart boundaries browser-owned and sends ZIP files as raw data', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: 't' }))
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);
    const form = new FormData();
    form.append('platformId', '1');
    await api('dat-files', { method: 'POST', body: form });
    expect(fetch.mock.calls[1][1].headers.has('Content-Type')).toBe(false);
    expect(fetch.mock.calls[1][1].body).toBe(form);
    const zip = new Blob(['zip']);
    await api('system/backups/import', { method: 'POST', body: zip, raw: true });
    expect(fetch.mock.calls[2][1].body).toBe(zip);
    expect(fetch.mock.calls[2][1].headers.get('Content-Type')).toBe('application/zip');
  });
});
describe('configuration payloads', () => {
  it('whitelists request fields and preserves the three secret states', () => {
    const result = payload(clientFields, {
      id: 50,
      version: 'opaque',
      createdAtUtc: 'date',
      name: 'Client',
      category: 'other',
      apiKeyConfigured: true,
      username: null,
      password: 'replacement',
      apiKey: '',
      priority: '25',
    });
    expect(result).toMatchObject({ username: null, password: 'replacement', apiKey: '', priority: 25 });
    for (const key of ['id', 'version', 'createdAtUtc', 'apiKeyConfigured', 'category'])
      expect(result).not.toHaveProperty(key);
    expect(payload(providerFields, { enabled: true }).clientSecret).toBeNull();
  });
  it('converts optional system IDs and category lists without sending server metadata', () => {
    const result = payload(platformFields, {
      allowedExtensions: '.nes, .zip',
      torznabCategories: '1000, 1010\n1020',
      igdbPlatformId: '',
      screenScraperSystemId: '3',
    });
    expect(result).toMatchObject({
      allowedExtensions: ['.nes', '.zip'],
      torznabCategories: [1000, 1010, 1020],
      igdbPlatformId: null,
      screenScraperSystemId: 3,
    });
  });
});
it('rejects foreign or unrelated media URLs', () => {
  expect(mediaUrl('/api/v1/media/content?token=signed')).toBe('/api/v1/media/content?token=signed');
  for (const url of [
    'https://untrusted.example/api/v1/media/content?token=x',
    'javascript:alert(1)',
    '/api/v1/auth/api-key',
  ])
    expect(() => mediaUrl(url)).toThrow();
  expect(query({ query: 'A&B', id: 2, empty: '', ignored: undefined })).toBe('query=A%26B&id=2');
});
