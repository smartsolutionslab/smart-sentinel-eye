import { configureStore } from '@reduxjs/toolkit';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the origin at module load; stub it before the dynamic
// import so fetchBaseQuery builds absolute URLs (Node's Request rejects
// relative ones).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { rulesApi } = await import('./rules.api.js');

function okResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

function createStore() {
  return configureStore({
    reducer: { [rulesApi.reducerPath]: rulesApi.reducer },
    middleware: (getDefault) => getDefault().concat(rulesApi.middleware),
  });
}

// fetchBaseQuery calls fetch with a Request, so the header assertions read it
// off the first argument. The mock declares that parameter so the call tuple
// is typed — a zero-arg vi.fn() gives `calls: []` and cannot be indexed.
function ifMatchOf(request: Request | undefined): string | null {
  return request?.headers.get('If-Match') ?? null;
}

/**
 * Spec 012 T048. `dryRunRule` is a POST that persists nothing — it sits in the
 * server's *reads* group behind the read scope — so it must not carry a
 * precondition. Sending one would make a read fail because somebody else
 * edited the rule, and `ConcurrencyHeaders` rejects a malformed value outright.
 *
 * The exclusion is invisible in the slice (it is the absence of a `headers`
 * line), which is exactly the kind of thing a later "make the mutations
 * consistent" pass would undo.
 */
describe('If-Match is sent only by rule mutations that persist (spec 012 T048)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Omits If-Match on dryRunRule, which is a mutation only in RTK’s HTTP-verb sense', async () => {
    const fetchMock = vi.fn((_request: Request) =>
      Promise.resolve(okResponse({ matched: true, evaluatedValue: null })),
    );
    vi.stubGlobal('fetch', fetchMock);

    await createStore().dispatch(
      rulesApi.endpoints.dryRunRule.initiate({
        name: 'high-oee',
        sampleEvent: '{"payload":{"cycleTime":25}}',
      }),
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(ifMatchOf(fetchMock.mock.calls[0]?.[0])).toBeNull();
  });

  it('Sends If-Match on publishRule, which does persist', async () => {
    const fetchMock = vi.fn((_request: Request) => Promise.resolve(okResponse('high-oee')));
    vi.stubGlobal('fetch', fetchMock);

    await createStore().dispatch(rulesApi.endpoints.publishRule.initiate({ name: 'high-oee', version: 4 }));

    expect(ifMatchOf(fetchMock.mock.calls[0]?.[0])).toBe('"4"');
  });

  it('Sends If-Match on archiveRule, which does persist', async () => {
    const fetchMock = vi.fn((_request: Request) => Promise.resolve(okResponse('high-oee')));
    vi.stubGlobal('fetch', fetchMock);

    await createStore().dispatch(rulesApi.endpoints.archiveRule.initiate({ name: 'high-oee', version: 4 }));

    expect(ifMatchOf(fetchMock.mock.calls[0]?.[0])).toBe('"4"');
  });
});
