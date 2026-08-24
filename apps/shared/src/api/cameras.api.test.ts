import { configureStore } from '@reduxjs/toolkit';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the origin at module load; stub it before the dynamic
// import so fetchBaseQuery builds absolute URLs (Node's Request rejects
// relative ones).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { camerasApi } = await import('./cameras.api.js');

function noContent(): Response {
  return new Response(null, { status: 204 });
}

function createStore() {
  return configureStore({
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer },
    middleware: (getDefault) => getDefault().concat(camerasApi.middleware),
  });
}

function ifMatchOf(request: Request | undefined): string | null {
  return request?.headers.get('If-Match') ?? null;
}

/**
 * Spec 032 T008 — FR-016.
 *
 * Retirement is idempotent rather than version-checked (spec 028). The endpoint
 * answers `204` whether or not the camera was already retired, and declares no
 * `409`, `412` or `428`. Sending a precondition would invent a failure mode the
 * server does not have.
 *
 * <p>
 * The exclusion is invisible in the slice — it is the *absence* of a `headers`
 * line, three endpoints below one that has it — and `CameraDetailPage` already
 * holds a version for the address correction, so threading it in here is the
 * natural mistake rather than a careless one. Nothing but this assertion would
 * catch it: with a version attached the request still succeeds, because the
 * server ignores a header it never reads.
 * </p>
 *
 * Mirrors `rules.api.test.ts`, which guards the same property for `dryRunRule`
 * for a related reason.
 */
describe('If-Match is sent only by camera mutations that are version-checked (spec 032 FR-016)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Omits If-Match on retireCamera, which is idempotent rather than versioned', async () => {
    const fetchMock = vi.fn((_request: Request) => Promise.resolve(noContent()));
    vi.stubGlobal('fetch', fetchMock);

    await createStore().dispatch(
      camerasApi.endpoints.retireCamera.initiate({
        cameraIdentifier: '0192f3c1-0000-7000-8000-000000000001',
      }),
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(ifMatchOf(fetchMock.mock.calls[0]?.[0])).toBeNull();
  });

  it('Posts to the retire sub-resource', async () => {
    const fetchMock = vi.fn((_request: Request) => Promise.resolve(noContent()));
    vi.stubGlobal('fetch', fetchMock);

    await createStore().dispatch(
      camerasApi.endpoints.retireCamera.initiate({
        cameraIdentifier: '0192f3c1-0000-7000-8000-000000000001',
      }),
    );

    const request = fetchMock.mock.calls[0]?.[0];
    expect(request?.method).toBe('POST');
    expect(request?.url).toContain('/0192f3c1-0000-7000-8000-000000000001/retire');
  });

  /**
   * The counterpart, without which the assertion above passes against a slice
   * that sends no headers anywhere — including on the one mutation that must.
   */
  it('Still sends If-Match on changeCameraAddress, which is version-checked', async () => {
    const fetchMock = vi.fn((_request: Request) => Promise.resolve(noContent()));
    vi.stubGlobal('fetch', fetchMock);

    await createStore().dispatch(
      camerasApi.endpoints.changeCameraAddress.initiate({
        cameraIdentifier: '0192f3c1-0000-7000-8000-000000000001',
        rtspUrl: 'rtsp://10.0.5.44/h264',
        version: 7,
      }),
    );

    expect(ifMatchOf(fetchMock.mock.calls[0]?.[0])).toBe('"7"');
  });
});
