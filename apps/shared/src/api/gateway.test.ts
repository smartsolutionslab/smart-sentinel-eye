import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { BaseQueryApi } from '@reduxjs/toolkit/query';

// gateway.ts resolves the origin once at module load; stub it before importing
// so the node test environment builds absolute request URLs (Node's fetch and
// Request reject relative ones).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { gatewayBaseQuery, setAccessTokenProvider, setOnSessionExpired, setSessionRenewer } =
  await import('./gateway.js');

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

// Spec 205 (#2301): #2301's suggested `fetchMock.mock.calls[1][1].headers` would
// throw. RTK Query 2.12.0 builds a `Request` and calls `fetchFn(request)` with
// ONE argument (@reduxjs/toolkit/dist/query/rtk-query.modern.mjs:226,233), so
// `calls[n][1]` is `undefined` and the header lives on the `Request` at
// `calls[n][0]`. Confirmed against the installed 2.12.0 dist, not assumed.
const authorizationOf = (call: unknown[]): string | null => (call[0] as Request).headers.get('authorization');

const fetchMock = vi.fn();

describe('gatewayBaseQuery reauth', () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    // Reset all three module-level singletons between tests, not just the
    // provider — a renewer or expiry handler left over from a previous test
    // is order-dependent even where it happens to be harmless today
    // (phase-6 review, #2301).
    setAccessTokenProvider(() => undefined);
    setSessionRenewer(() => Promise.resolve(undefined));
    setOnSessionExpired(() => undefined);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('Reads the provider registered after the client was constructed', async () => {
    // Every real RTK client calls gatewayBaseQuery(route) at MODULE scope, at
    // import time - long before AuthGate renders and registers the provider.
    // gatewayBaseQuery must defer the read (a thunk), never capture the
    // provider binding's value at construction time - the exact class of
    // stale-closure bug #2301 exists to close, one line away from being
    // reintroduced by a "simplification" that drops the wrapper (phase-6
    // review). This test constructs the client BEFORE registering the
    // provider, mirroring the real ordering.
    const baseQuery = gatewayBaseQuery('cameras');
    setAccessTokenProvider(() => 'registered-later');
    fetchMock.mockResolvedValueOnce(ok());

    await baseQuery('items', queryApi, {});

    expect(authorizationOf(fetchMock.mock.calls[0]!)).toBe('Bearer registered-later');
  });

  it('Renews once on 401 and retries with the token the renewal minted', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(ok());
    setAccessTokenProvider(() => 'old-token');
    const renew = vi.fn(() => Promise.resolve('new-token'));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.data).toEqual({ ok: true });
    expect(expired).not.toHaveBeenCalled();
    // This is the assertion #2301 asks for, and it is red today: the retry
    // re-sends the bearer that just failed instead of the renewal's token.
    expect(authorizationOf(fetchMock.mock.calls[0]!)).toBe('Bearer old-token');
    expect(authorizationOf(fetchMock.mock.calls[1]!)).toBe('Bearer new-token');
  });

  it('A renewal that yields no token is a failed renewal', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized());
    const renew = vi.fn(() => Promise.resolve(undefined));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.error?.status).toBe(401);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it('A renewal that yields an empty token is a failed renewal', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized());
    const renew = vi.fn(() => Promise.resolve(''));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.error?.status).toBe(401);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it('A renewal that rejects is a failed renewal', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized());
    // Explicit type argument: Promise.reject<T>()'s default T=never would
    // otherwise fix this mock's inferred type, and the reuse below (a
    // different renewer resolving a token) wouldn't typecheck against it.
    const renew = vi.fn(() => Promise.reject<string | undefined>(new Error('renewal transport failed')));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.error?.status).toBe(401);
    expect(expired).toHaveBeenCalledTimes(1);

    // Pins that the in-flight renewal promise is cleared on rejection too, so
    // a later 401 renews again rather than reusing a settled promise.
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(ok());
    renew.mockImplementation(() => Promise.resolve('later-token'));
    await gatewayBaseQuery('cameras')('items', queryApi, {});
    expect(renew).toHaveBeenCalledTimes(2);
  });

  it('A retry the server still refuses expires the session', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(unauthorized());
    setAccessTokenProvider(() => 'old-token');
    const renew = vi.fn(() => Promise.resolve('new-token'));
    const expired = vi.fn();
    setSessionRenewer(renew);
    setOnSessionExpired(expired);

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.error?.status).toBe(401);
    expect(expired).toHaveBeenCalledTimes(1);
    // Proves the escalation is not caused by a stale credential: the retry the
    // server refused already carried the renewal's own token.
    expect(authorizationOf(fetchMock.mock.calls[1]!)).toBe('Bearer new-token');
  });

  it('Passes non-401 errors through without renewing', async () => {
    fetchMock.mockResolvedValueOnce(serverError());
    const renew = vi.fn(() => Promise.resolve('new-token'));
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
    const renew = vi.fn(() => Promise.resolve('new-token'));
    setSessionRenewer(renew);
    setOnSessionExpired(vi.fn());

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(result.data).toEqual({ ok: true });
    expect(renew).not.toHaveBeenCalled();
  });

  it('A request with no registered token carries no Authorization header', async () => {
    fetchMock.mockResolvedValueOnce(ok());
    setOnSessionExpired(vi.fn());

    await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(authorizationOf(fetchMock.mock.calls[0]!)).toBeNull();
  });

  it('Shares one renewal between concurrent 401s and retries both with the same new token', async () => {
    let resolveRenew: (renewed: string | undefined) => void = () => undefined;
    const renew = vi.fn(
      () =>
        new Promise<string | undefined>((resolve) => {
          resolveRenew = resolve;
        }),
    );
    const expired = vi.fn();
    setAccessTokenProvider(() => 'old-token');
    setSessionRenewer(renew);
    setOnSessionExpired(expired);
    fetchMock.mockImplementation(() => Promise.resolve(ok()));
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(unauthorized());

    const baseQuery = gatewayBaseQuery('cameras');
    const first = baseQuery('a', queryApi, {});
    const second = baseQuery('b', queryApi, {});
    await vi.waitFor(() => expect(renew).toHaveBeenCalledTimes(1));

    resolveRenew('new-token');
    const [firstResult, secondResult] = await Promise.all([first, second]);

    expect(renew).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(4);
    expect(firstResult.data).toEqual({ ok: true });
    expect(secondResult.data).toEqual({ ok: true });
    expect(expired).not.toHaveBeenCalled();
    expect(authorizationOf(fetchMock.mock.calls[2]!)).toBe('Bearer new-token');
    expect(authorizationOf(fetchMock.mock.calls[3]!)).toBe('Bearer new-token');
  });
});
