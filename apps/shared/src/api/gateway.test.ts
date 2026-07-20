import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { BaseQueryApi } from '@reduxjs/toolkit/query';

// gateway.ts resolves the origin once at module load; stub it before importing
// so the node test environment builds absolute request URLs (Node's fetch and
// Request reject relative ones).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { gatewayBaseQuery, setOnSessionExpired, setSessionRenewer } = await import('./gateway.js');

const queryApi = {
  signal: new AbortController().signal,
  abort: () => undefined,
  dispatch: () => undefined,
  getState: () => ({}),
  extra: undefined,
  endpoint: 'test',
  type: 'query',
  forced: false,
} as unknown as BaseQueryApi;

const ok = () =>
  new Response(JSON.stringify({ ok: true }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
const unauthorized = () => new Response(null, { status: 401 });
const serverError = () =>
  new Response(JSON.stringify({ detail: 'boom' }), {
    status: 500,
    headers: { 'content-type': 'application/json' },
  });

const fetchMock = vi.fn();

describe('gatewayBaseQuery reauth', () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('Renews once on 401 and returns the result of the retried request', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(ok());
    const renew = vi.fn(() => Promise.resolve(true));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.data).toEqual({ ok: true });
    expect(expired).not.toHaveBeenCalled();
  });

  it('Escalates to onSessionExpired and returns the 401 when the renewer reports failure', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized());
    const renew = vi.fn(() => Promise.resolve(false));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.error?.status).toBe(401);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it('Escalates to onSessionExpired once when the retried request is rejected again', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(unauthorized());
    const renew = vi.fn(() => Promise.resolve(true));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.error?.status).toBe(401);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it('Passes non-401 errors through without renewing', async () => {
    fetchMock.mockResolvedValueOnce(serverError());
    const renew = vi.fn(() => Promise.resolve(true));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(result.error?.status).toBe(500);
    expect(renew).not.toHaveBeenCalled();
    expect(expired).not.toHaveBeenCalled();
  });

  it('Passes successful responses through without renewing', async () => {
    fetchMock.mockResolvedValueOnce(ok());
    const renew = vi.fn(() => Promise.resolve(true));
    setSessionRenewer(renew);
    setOnSessionExpired(vi.fn());

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(result.data).toEqual({ ok: true });
    expect(renew).not.toHaveBeenCalled();
  });

  it('Shares one in-flight renewal between concurrent 401s and retries both afterwards', async () => {
    let resolveRenew: (renewed: boolean) => void = () => undefined;
    const renew = vi.fn(
      () =>
        new Promise<boolean>((resolve) => {
          resolveRenew = resolve;
        }),
    );
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);
    fetchMock.mockImplementation(() => Promise.resolve(ok()));
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(unauthorized());

    const baseQuery = gatewayBaseQuery('cameras');
    const first = baseQuery('a', queryApi, {});
    const second = baseQuery('b', queryApi, {});
    await vi.waitFor(() => expect(renew).toHaveBeenCalledTimes(1));

    resolveRenew(true);
    const [firstResult, secondResult] = await Promise.all([first, second]);

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(4);
    expect(firstResult.data).toEqual({ ok: true });
    expect(secondResult.data).toEqual({ ok: true });
    expect(expired).not.toHaveBeenCalled();
  });
});
