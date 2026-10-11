import { configureStore } from '@reduxjs/toolkit';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the origin at module load; stub it before the dynamic
// import so fetchBaseQuery builds absolute URLs (Node's Request rejects
// relative ones). Mirrors systemVariables.api.test.ts/cameras.api.test.ts.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { eventIngestionApi, HELD_EVENT_TYPES_LIMIT } = await import('./eventIngestion.api.js');

function deadLettersResponse(): Response {
  return new Response(JSON.stringify([]), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

function createStore() {
  return configureStore({
    reducer: { [eventIngestionApi.reducerPath]: eventIngestionApi.reducer },
    middleware: (getDefault) => getDefault().concat(eventIngestionApi.middleware),
  });
}

describe('listHeldEventTypes (issue #2780)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Requests /dead-letters with the fixed reason/state and the server-maximum limit', async () => {
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(deadLettersResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(eventIngestionApi.endpoints.listHeldEventTypes.initiate(undefined));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const requested = fetchMock.mock.calls[0]?.[0];
    const requestedUrl = new URL(requested instanceof Request ? requested.url : (requested as string));

    // The gateway route (ADR-0106): the service-internal group is
    // /events/dead-letters, reached here as /event-ingestion/events/...
    expect(requestedUrl.pathname).toBe('/event-ingestion/events/dead-letters');
    expect(requestedUrl.searchParams.get('reason')).toBe('UnknownEventType');
    expect(requestedUrl.searchParams.get('state')).toBe('Held');
    // Should-fix 1 (PR #2856): sent explicitly so a kind whose rows are all
    // older than the server's own 100-row default does not silently drop out
    // of the grouped list.
    expect(requestedUrl.searchParams.get('limit')).toBe(String(HELD_EVENT_TYPES_LIMIT));
    expect(requestedUrl.searchParams.has('fabId')).toBe(false);
  });

  it('Passes fabId through when supplied, alongside the fixed filters', async () => {
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(deadLettersResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(eventIngestionApi.endpoints.listHeldEventTypes.initiate({ fabId: 'munich' }));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const requested = fetchMock.mock.calls[0]?.[0];
    const requestedUrl = new URL(requested instanceof Request ? requested.url : (requested as string));

    expect(requestedUrl.searchParams.get('fabId')).toBe('munich');
    expect(requestedUrl.searchParams.get('reason')).toBe('UnknownEventType');
    expect(requestedUrl.searchParams.get('state')).toBe('Held');
    expect(requestedUrl.searchParams.get('limit')).toBe(String(HELD_EVENT_TYPES_LIMIT));
  });
});
