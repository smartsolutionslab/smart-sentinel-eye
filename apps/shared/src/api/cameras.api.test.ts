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

/**
 * Spec 048 T004 — the paging arithmetic, tested where a boundary bug is visible.
 *
 * <p>
 * <b>Every fixture here exceeds one page before anything is asserted.</b> Below
 * 200 cameras the source returns everything in one request, so a test against a
 * handful of cameras passes with the whole feature deleted — it is the same trap
 * spec 045 hit with a wall that was already aligned and spec 046 hit again with
 * label text seeded at mount. The passing state and the broken state look
 * identical until something induces the condition.
 * </p>
 *
 * <p>
 * These live here rather than in the dialog for a reason a component test cannot
 * cover: rendering 250 options proves the <em>total</em> is right without proving
 * <em>which</em> camera was dropped at offset 200.
 * </p>
 */
describe('listAllCameraChoices gathers every camera the operator may choose (spec 048)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const PAGE = 200;

  function camera(index: number): Record<string, unknown> {
    return {
      cameraIdentifier: `cam-${String(index).padStart(4, '0')}`,
      version: 1,
      fab: 'fab-1',
      // Padded so lexical order matches numeric order — otherwise "camera 10"
      // sorts before "camera 9" and the last-camera assertion tests nothing.
      name: `Camera ${String(index).padStart(4, '0')}`,
      rtspUrl: 'rtsp://10.0.5.1/stream',
      registeredAt: '2026-01-01T00:00:00Z',
      status: 'Registered',
    };
  }

  /** A source holding `total` cameras, served `PAGE` at a time. */
  function sourceOf(total: number, overrides: { countReported?: number } = {}) {
    return vi.fn((request: Request) => {
      const url = new URL(request.url);
      const offset = Number(url.searchParams.get('offset') ?? '0');
      const limit = Number(url.searchParams.get('limit') ?? String(PAGE));
      const items = Array.from({ length: Math.max(0, Math.min(limit, total - offset)) }, (_unused, index) =>
        camera(offset + index),
      );
      return Promise.resolve(
        new Response(JSON.stringify({ items, count: overrides.countReported ?? total, offset, limit }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    });
  }

  async function choices(fetchMock: ReturnType<typeof sourceOf>) {
    vi.stubGlobal('fetch', fetchMock);
    const result = await createStore().dispatch(camerasApi.endpoints.listAllCameraChoices.initiate());
    return result.data as { items: { cameraIdentifier: string; name: string }[]; count: number; complete: boolean };
  }

  /**
   * **The core claim.** 250 is the constitution's production target and the
   * number the picker failed at. Two pages, every camera, including the one the
   * old single request could never reach.
   */
  it('Returns all 250 cameras of a full fab, across two requests', async () => {
    const fetchMock = sourceOf(250);
    const data = await choices(fetchMock);

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(data.items).toHaveLength(250);
    expect(data.count).toBe(250);
    expect(data.complete).toBe(true);
    // The alphabetically last camera lives on the second page. Asserting only
    // the length passes against a loop that fetches page one twice.
    expect(data.items.at(-1)?.name).toBe('Camera 0249');
  });

  it('Asks for cameras by name, ascending, rather than the newest-first default', async () => {
    const fetchMock = sourceOf(250);
    await choices(fetchMock);

    const url = new URL((fetchMock.mock.calls[0]?.[0] as Request).url);
    expect(url.searchParams.get('sort')).toBe('name');
    expect(url.searchParams.get('order')).toBe('asc');
  });

  it('Asks for the largest page the source will serve, so a full fab costs two requests', async () => {
    const fetchMock = sourceOf(250);
    await choices(fetchMock);

    const url = new URL((fetchMock.mock.calls[0]?.[0] as Request).url);
    expect(url.searchParams.get('limit')).toBe('200');
    expect(url.searchParams.get('offset')).toBe('0');
    expect(new URL((fetchMock.mock.calls[1]?.[0] as Request).url).searchParams.get('offset')).toBe('200');
  });

  /**
   * A camera registered mid-loop shifts every later page down by one, so the
   * camera sitting on a page boundary arrives twice. In a `<select>` that is two
   * identical options and a duplicate React key.
   */
  it('Offers a camera once when paging delivers it twice', async () => {
    // Page two repeats the last camera of page one, which is exactly what an
    // insertion at the head of the list produces.
    const fetchMock = vi.fn((request: Request) => {
      const offset = Number(new URL(request.url).searchParams.get('offset') ?? '0');
      const items =
        offset === 0
          ? Array.from({ length: PAGE }, (_unused, index) => camera(index))
          : [camera(PAGE - 1), ...Array.from({ length: 50 }, (_unused, index) => camera(PAGE + index))];
      return Promise.resolve(
        new Response(JSON.stringify({ items, count: 250, offset, limit: PAGE }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    });

    const data = await choices(fetchMock as unknown as ReturnType<typeof sourceOf>);

    const identifiers = data.items.map((item) => item.cameraIdentifier);
    expect(new Set(identifiers).size, 'no camera appears twice').toBe(identifiers.length);
    expect(identifiers.filter((id) => id === 'cam-0199')).toHaveLength(1);
  });

  /**
   * **The bound must be observable, not merely believed.** Five pages of 200 is
   * 1000 — four times the production target — and past it the picker stops and
   * says so rather than issuing requests indefinitely behind an open dialog.
   */
  it('Stops at the page bound and reports the list as incomplete', async () => {
    const fetchMock = sourceOf(1_200);
    const data = await choices(fetchMock);

    expect(fetchMock).toHaveBeenCalledTimes(5);
    expect(data.items).toHaveLength(1_000);
    expect(data.count).toBe(1_200);
    expect(data.complete, 'reaching the bound is not completeness').toBe(false);
  });

  /**
   * The count is the source's total, passed through untouched. Reporting
   * `items.length` would make the picker agree with itself no matter how much it
   * dropped — the gap between the two is the only thing the operator is told
   * about.
   */
  it('Reports the sources total rather than the number of rows it gathered', async () => {
    const data = await choices(sourceOf(1_200));

    expect(data.count).toBe(1_200);
    expect(data.count).not.toBe(data.items.length);
  });

  /**
   * A retirement mid-loop shifts later pages up, so a camera can be missed
   * entirely. De-duplication cannot recover that one — and does not need to.
   * Fewer items than the count is exactly what the picker reports.
   */
  it('Declares itself incomplete when the list shrank under the paging loop', async () => {
    // The source says 250 but only ever yields 249 — a camera retired between
    // the two requests.
    const data = await choices(sourceOf(249, { countReported: 250 }));

    expect(data.items).toHaveLength(249);
    expect(data.count).toBe(250);
    expect(data.complete, 'a short list is not a complete one').toBe(false);
  });

  /**
   * **The case a surviving mutation exposed.** A fab of exactly 1000 arrives as
   * five full pages, so the loop ends by reaching the bound rather than by
   * seeing a short page — and an earlier version required a short page before
   * it would call anything complete. It reported a list holding every camera as
   * incomplete.
   *
   * <p>
   * Nothing else here covers it: 250 ends on a short page and 1200 is genuinely
   * incomplete, so both agreed with the broken rule. Off-by-one at the boundary
   * was the first named risk in this feature’s task list, and this is it.
   * </p>
   */
  it('Reports a fab sitting exactly on the page bound as complete', async () => {
    const fetchMock = sourceOf(1_000);
    const data = await choices(fetchMock);

    expect(fetchMock).toHaveBeenCalledTimes(5);
    expect(data.items).toHaveLength(1_000);
    expect(data.complete, 'five full pages holding every camera is complete').toBe(true);
  });

  it('Reports a fab that fits in one page as complete', async () => {
    const fetchMock = sourceOf(30);
    const data = await choices(fetchMock);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(data.items).toHaveLength(30);
    expect(data.complete).toBe(true);
  });
});

/**
 * Spec 048, found in review — paging turned one request that could fail into
 * five, and a failure on any of them threw away everything already gathered.
 */
describe('A page that fails does not discard the cameras already gathered (spec 048)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const PAGE = 200;

  function camera(index: number): Record<string, unknown> {
    return {
      cameraIdentifier: `cam-${String(index).padStart(4, '0')}`,
      version: 1,
      fab: 'fab-1',
      name: `Camera ${String(index).padStart(4, '0')}`,
      rtspUrl: 'rtsp://10.0.5.1/stream',
      registeredAt: '2026-01-01T00:00:00Z',
      status: 'Registered',
    };
  }

  /** Serves `total` cameras but fails on the page at `failOn`. */
  function flakySource(total: number, failOn: number) {
    return vi.fn((request: Request) => {
      const offset = Number(new URL(request.url).searchParams.get('offset') ?? '0');
      if (offset === failOn * PAGE) {
        return Promise.resolve(new Response('upstream is unwell', { status: 503 }));
      }
      const items = Array.from({ length: Math.max(0, Math.min(PAGE, total - offset)) }, (_unused, index) =>
        camera(offset + index),
      );
      return Promise.resolve(
        new Response(JSON.stringify({ items, count: total, offset, limit: PAGE }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    });
  }

  async function choices(fetchMock: ReturnType<typeof flakySource>) {
    vi.stubGlobal('fetch', fetchMock);
    const result = await createStore().dispatch(camerasApi.endpoints.listAllCameraChoices.initiate());
    return result;
  }

  /**
   * **Induced on a later page, which is the case that matters.** Failing the
   * first page proves nothing about keeping partial results, because there are
   * none to keep.
   */
  it('Keeps what it gathered when a later page fails, and says the list is short', async () => {
    const result = await choices(flakySource(1_200, 3));
    const data = result.data as { items: unknown[]; count: number; complete: boolean };

    expect(data.items, 'three pages survived the fourth failing').toHaveLength(600);
    expect(data.complete, 'and the picker is told they are not all of them').toBe(false);
    expect(result.error).toBeUndefined();
  });

  /**
   * The counterpart. With nothing gathered there is nothing to degrade to, and
   * an empty list presented as complete would be a worse lie than an error.
   */
  it('Fails outright when the very first page fails', async () => {
    const result = await choices(flakySource(1_200, 0));

    expect(result.data).toBeUndefined();
    expect(result.error, 'nothing was gathered, so there is nothing to show').toBeDefined();
  });

  /**
   * A fab whose size is an exact multiple of the page size used to cost a whole
   * extra round trip to discover an empty page — in front of an operator waiting
   * on a dialog.
   */
  it('Does not spend a request discovering an empty page', async () => {
    const fetchMock = vi.fn((request: Request) => {
      const offset = Number(new URL(request.url).searchParams.get('offset') ?? '0');
      const items = Array.from({ length: Math.max(0, Math.min(PAGE, 400 - offset)) }, (_unused, index) =>
        camera(offset + index),
      );
      return Promise.resolve(
        new Response(JSON.stringify({ items, count: 400, offset, limit: PAGE }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    });

    vi.stubGlobal('fetch', fetchMock);
    const result = await createStore().dispatch(camerasApi.endpoints.listAllCameraChoices.initiate());
    const data = result.data as { items: unknown[]; complete: boolean };

    expect(fetchMock, '400 cameras is exactly two pages').toHaveBeenCalledTimes(2);
    expect(data.items).toHaveLength(400);
    expect(data.complete).toBe(true);
  });
});
