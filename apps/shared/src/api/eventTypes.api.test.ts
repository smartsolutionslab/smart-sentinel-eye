import { configureStore } from '@reduxjs/toolkit';
import { afterEach, describe, expect, it, vi } from 'vitest';

// Mirrors eventIngestion.api.test.ts/systemVariables.api.test.ts: stub the
// gateway origin before the dynamic import so fetchBaseQuery builds absolute
// URLs (Node's Request rejects relative ones).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { eventTypesApi } = await import('./eventTypes.api.js');

function registeredResponse(): Response {
  return new Response(JSON.stringify('d290f1ee-6c54-4b01-90e6-d701748f0851'), {
    status: 201,
    headers: { 'Content-Type': 'application/json' },
  });
}

function createStore() {
  return configureStore({
    reducer: { [eventTypesApi.reducerPath]: eventTypesApi.reducer },
    middleware: (getDefault) => getDefault().concat(eventTypesApi.middleware),
  });
}

describe('promoteEventType (issue #2855)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('POSTs to /event-types with the kind in the body and fabId as a query param', async () => {
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(registeredResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(
      eventTypesApi.endpoints.promoteEventType.initiate({ fab: 'munich', kind: 'TemperatureAlarm' }),
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [requested, init] = fetchMock.mock.calls[0] as [RequestInfo | URL, RequestInit | undefined];
    const requestedUrl = new URL(requested instanceof Request ? requested.url : (requested as string));

    // The gateway route (ADR-0106): the service-internal group is
    // /event-types, reached here as /event-ingestion/event-types.
    expect(requestedUrl.pathname).toBe('/event-ingestion/event-types');
    expect(requestedUrl.searchParams.get('fabId')).toBe('munich');

    const method = requested instanceof Request ? requested.method : init?.method;
    expect(method).toBe('POST');

    const body =
      requested instanceof Request ? await requested.clone().json() : JSON.parse((init?.body as string) ?? '{}');
    expect(body).toEqual({ kind: 'TemperatureAlarm' });
  });

  it('Sends a distinct Idempotency-Key per call (ADR-0142)', async () => {
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(registeredResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(
      eventTypesApi.endpoints.promoteEventType.initiate({ fab: 'munich', kind: 'TemperatureAlarm' }),
    );
    await store.dispatch(eventTypesApi.endpoints.promoteEventType.initiate({ fab: 'munich', kind: 'PressureAlarm' }));

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const headerOf = (call: unknown[]) => {
      const [requested, init] = call as [RequestInfo | URL, RequestInit | undefined];
      return requested instanceof Request
        ? requested.headers.get('Idempotency-Key')
        : (init?.headers as Record<string, string> | undefined)?.['Idempotency-Key'];
    };
    const first = headerOf(fetchMock.mock.calls[0] as unknown[]);
    const second = headerOf(fetchMock.mock.calls[1] as unknown[]);

    expect(first).toBeTruthy();
    expect(second).toBeTruthy();
    expect(first).not.toBe(second);
  });
});
