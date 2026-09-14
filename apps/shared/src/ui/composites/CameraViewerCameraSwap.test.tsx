// @vitest-environment jsdom
import { configureStore } from '@reduxjs/toolkit';
import { act, cleanup, render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load (rules.api.test.ts /
// OverlayEditorDialogChainRetention.test.tsx stub it the same way): stub the
// env before any dynamic import reaches streams.api, so fetchBaseQuery builds
// absolute URLs — Node's `Request` rejects a relative one.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

/**
 * Issue #2370 / spec 157. `useGetStreamQuery` is REAL here, and so is
 * `WhepClient` — that is the whole point. Every mocked double of this hook in
 * the repo answers `{ data, isLoading, error }` regardless of its argument, so
 * a mocked suite cannot express RTK Query's own distinction between `data`
 * (the last successful result for ANY argument this hook instance has ever
 * been called with) and `currentData` (resets to `undefined` on an argument
 * change). It could not go red against this defect, and it will go red
 * against the fix once the mocks are widened (T001) — the same structural
 * blindness phase 6 found on the overlay twin, PR #2369
 * (`apps/management-web/src/features/overlays/OverlayEditorDialogChainRetention.test.tsx`),
 * which this file mirrors in shape.
 *
 * `CellPage` keys tiles on grid position, not camera (spec 095), so a
 * reassignment changes only the `cameraIdentifier` prop of an
 * already-mounted `CameraViewer` — the `<video>` element is never remounted.
 * A test that remounted to perform the swap would prove nothing about the
 * defect at all; every scenario below captures the `<video>` node before the
 * swap and asserts it is the SAME node after (spec § "Why this cannot be
 * proved by a remount").
 */

const { streamsApi } = await import('@smart-sentinel-eye/shared/api/streams.api');
const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

const CAM_A = 'cam-a';
const CAM_B = 'cam-b';
const CAM_A_WHEP_URL = 'http://sfu.test/whep/cam-a';
const CAM_B_WHEP_URL = 'http://sfu.test/whep/cam-b';

class FakePeerConnection {
  static instances: FakePeerConnection[] = [];
  static lastInstance(): FakePeerConnection {
    return FakePeerConnection.instances[FakePeerConnection.instances.length - 1]!;
  }
  ontrack: ((event: { streams: unknown[] }) => void) | null = null;
  onconnectionstatechange: (() => void) | null = null;
  connectionState = 'new';
  iceGatheringState = 'complete';
  localDescription: { type: string; sdp: string } | null = null;
  closed = false;

  constructor() {
    FakePeerConnection.instances.push(this);
  }

  addTransceiver() {}

  async createOffer() {
    return { type: 'offer', sdp: 'v=0\r\no=fake 1 1 IN IP4 127.0.0.1\r\ns=-\r\n' };
  }

  async setLocalDescription(desc: { type: string; sdp: string }) {
    this.localDescription = desc;
  }

  async setRemoteDescription() {}

  getReceivers() {
    return [];
  }

  addEventListener() {}

  removeEventListener() {}

  close() {
    this.closed = true;
  }

  setConnectionState(state: string) {
    this.connectionState = state;
    this.onconnectionstatechange?.();
  }
}

/** Minimal duck-typed WHEP answer for the raw `fetch(whepUrl, init)` calls `WhepClient` makes directly. */
function sdpAnswer(location: string) {
  return {
    ok: true,
    status: 200,
    headers: { get: (name: string) => (name.toLowerCase() === 'location' ? location : null) },
    text: async () => 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n',
  };
}

function healthyStream(cameraIdentifier: string, whepUrl: string) {
  return {
    cameraIdentifier,
    state: 'Healthy',
    whepUrl,
    transcodeMode: 'Passthrough',
    lastSuccessAt: null,
    error: null,
  };
}

function offlineStream(cameraIdentifier: string, whepUrl: string, reason: string) {
  return {
    cameraIdentifier,
    state: 'Offline',
    whepUrl,
    transcodeMode: 'Passthrough',
    lastSuccessAt: null,
    error: reason,
  };
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

function errorResponse(status: number): Response {
  return new Response('', { status });
}

/**
 * Per-camera `GET /streams/{id}` control. Unset means the read is HELD —
 * dispatched, never resolved — which is what lets a test observe the gap
 * between the swap and the new camera's own read answering (spec § "the gap
 * is not empty, it is A"). Setting an answer resolves any GET already in
 * flight for that camera AND makes every future GET (including the 5 s poll's
 * refetches) answer the same way, so a persistent failure stays persistent.
 */
let persistentAnswers: Map<string, () => Response>;
let heldResolvers: Map<string, Array<(response: Response) => void>>;

function setStreamAnswer(cameraIdentifier: string, answer: () => Response): void {
  persistentAnswers.set(cameraIdentifier, answer);
  const held = heldResolvers.get(cameraIdentifier);
  if (held !== undefined && held.length > 0) {
    heldResolvers.set(cameraIdentifier, []);
    for (const resolve of held) resolve(answer());
  }
}

function streamAnswerFor(cameraIdentifier: string): Promise<Response> {
  const persistent = persistentAnswers.get(cameraIdentifier);
  if (persistent !== undefined) return Promise.resolve(persistent());
  return new Promise<Response>((resolve) => {
    const held = heldResolvers.get(cameraIdentifier) ?? [];
    held.push(resolve);
    heldResolvers.set(cameraIdentifier, held);
  });
}

function isRequestLike(input: unknown): input is Request {
  return typeof Request !== 'undefined' && input instanceof Request;
}

async function fetchStub(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const url = isRequestLike(input) ? input.url : String(input);
  const method = isRequestLike(input) ? input.method : (init?.method ?? 'GET');

  if (method === 'DELETE') {
    return { ok: true, status: 200 } as Response;
  }
  if (url.includes('/streams/')) {
    const cameraIdentifier = url.slice(url.lastIndexOf('/') + 1);
    return streamAnswerFor(cameraIdentifier);
  }
  // Anything else is the WHEP POST offer.
  return sdpAnswer(`${url}/session-1`) as unknown as Response;
}

function isPostTo(url: string) {
  return (call: unknown[]): boolean => {
    const [input, init] = call as [RequestInfo | URL, RequestInit | undefined];
    const actualUrl = isRequestLike(input) ? input.url : String(input);
    const method = isRequestLike(input) ? input.method : (init?.method ?? 'GET');
    return method === 'POST' && actualUrl === url;
  };
}

function createStore() {
  return configureStore({
    reducer: { [streamsApi.reducerPath]: streamsApi.reducer },
    middleware: (getDefault) => getDefault().concat(streamsApi.middleware),
  });
}

let store: ReturnType<typeof createStore>;

function viewerFor(cameraIdentifier: string, getToken: () => Promise<string | null> = async () => 'token') {
  return (
    <Provider store={store}>
      <CameraViewer cameraIdentifier={cameraIdentifier} getToken={getToken} />
    </Provider>
  );
}

/**
 * Drains the async connect chain (RTK Query's GET → the reducer → the
 * effect → `createOffer`/`setLocalDescription`/the WHEP POST →
 * `setRemoteDescription`).
 *
 * A pure microtask spin (`await Promise.resolve()`, however many times) is
 * NOT enough here — unlike the fully-mocked `CameraViewer.test.tsx`, this
 * file drives the REAL `fetchBaseQuery`, and Node's `Response` body plumbing
 * genuinely yields to the event loop at least once. A real `setTimeout(0)`
 * macrotask, interleaved with microtask draining, is what actually lets that
 * continue — confirmed empirically: a 100-tick microtask-only loop left zero
 * `RTCPeerConnection` instances ever constructed, while this passes on the
 * first attempt. Real timers throughout this file, deliberately — no
 * `vi.useFakeTimers()` — because faking `setTimeout` stops it from providing
 * that yield too.
 */
async function flushConnect() {
  await act(async () => {
    for (let i = 0; i < 10; i += 1) {
      await new Promise((resolve) => setTimeout(resolve, 0));
      for (let j = 0; j < 5; j += 1) {
        await Promise.resolve();
      }
    }
  });
}

/** A real wall-clock wait, for the one scenario that needs the 5 s poll to actually elapse. */
async function realWait(ms: number) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
  await flushConnect();
}

/** Drains the connect chain, then drives the most recently created fake peer
 * connection to `connected` with a track attached — the fake stand-in for a
 * frame having actually arrived, so `srcObject` genuinely holds something the
 * later assertions can prove gets cleared. */
async function goLive(): Promise<FakePeerConnection> {
  await flushConnect();
  const pc = FakePeerConnection.lastInstance();
  act(() => {
    pc.ontrack?.({ streams: [{ id: `fake-stream-${FakePeerConnection.instances.indexOf(pc)}` }] });
    pc.setConnectionState('connected');
  });
  return pc;
}

function videoElement(): HTMLVideoElement {
  const el = document.querySelector('video');
  if (el === null) throw new Error('no <video> element rendered');
  return el as HTMLVideoElement;
}

describe('CameraViewer — a tile reassigned from camera A to camera B (spec 157)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    persistentAnswers = new Map();
    heldResolvers = new Map();
    fetchMock = vi.fn(fetchStub);
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    store = createStore();
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('Stops showing camera A the instant the tile is reassigned to camera B', async () => {
    setStreamAnswer(CAM_A, () => jsonResponse(healthyStream(CAM_A, CAM_A_WHEP_URL)));
    const view = render(viewerFor(CAM_A));
    const pcA = await goLive();

    const videoEl = videoElement();
    // Sanity: camera A is genuinely Live with a picture, or nothing below means anything.
    expect(videoEl.srcObject).not.toBeNull();
    expect(screen.queryByText('Connecting…')).toBeNull();
    const postsToA = () => fetchMock.mock.calls.filter(isPostTo(CAM_A_WHEP_URL));
    expect(postsToA()).toHaveLength(1);

    // Act — reassign this tile to camera B. Nothing else changes, and camera
    // B's own stream read is deliberately left unanswered past this point.
    view.rerender(viewerFor(CAM_B));

    // The premise, not the finding: this must be the SAME <video> element.
    // A test that remounts to perform the swap proves nothing about the
    // defect, because the defect only exists because the tile is NOT
    // remounted (spec 095, CellPage.tsx position keying).
    expect(videoElement()).toBe(videoEl);

    await flushConnect();

    // Camera A's session is closed — Link 3, confirmed: the effect does
    // re-run on a camera change (transitionTo's [cameraIdentifier] is in its
    // dep array), it just re-dials the wrong camera.
    expect(pcA.closed).toBe(true);

    // RED — SC-002. `data` (not `currentData`) still answers camera A's
    // whepUrl after the prop change, so the re-run effect reconnects to it.
    expect(postsToA()).toHaveLength(1);

    // RED — `WhepClient.teardownLocally` never clears `srcObject`, so camera
    // A's last decoded frame survives on the element under camera B's name.
    expect(videoEl.srcObject).toBeNull();

    // Sanity, not a red claim: the tile already reads "Connecting…" here,
    // because `transitionTo('connecting')` runs synchronously in the
    // re-dial's own effect setup. (plan.md §5's table calls this row FAILS
    // for "the `!whepUrl` early return never fires" — that reasoning is
    // for the FR-001-only intermediate state the spec traces separately;
    // against today's actual, fully unfixed code the label already reads
    // "Connecting…", just for the wrong reason. Recorded as a plan
    // discrepancy in the PR, not silently corrected here.)
    expect(screen.getByText('Connecting…')).toBeDefined();

    // RED — the real headline defect, reproduced rather than inferred: the
    // erroneous reconnect above is a genuine session against camera A's own
    // real URL, so nothing stops it succeeding in production. Let it.
    const errantPc = FakePeerConnection.lastInstance();
    expect(errantPc).not.toBe(pcA);
    act(() => {
      errantPc.ontrack?.({ streams: [{ id: 'fake-stream-errant-a-again' }] });
      errantPc.setConnectionState('connected');
    });

    // RED — today this flips straight back to Live, showing camera A's
    // video, logged under camera B's identity (`cameraIdentifier: 'cam-b'`
    // in the resilience transition — `transitionTo` reads the *current*
    // camera, not the one the session actually belongs to). This is #2370
    // exactly: "camera A's picture under camera B's identity."
    expect(videoEl.srcObject).toBeNull();
    expect(screen.getByText('Connecting…')).toBeDefined();

    // Now let camera B's own read answer.
    setStreamAnswer(CAM_B, () => jsonResponse(healthyStream(CAM_B, CAM_B_WHEP_URL)));
    const pcB = await goLive();

    expect(pcB).not.toBe(pcA);
    const postsToB = fetchMock.mock.calls.filter(isPostTo(CAM_B_WHEP_URL));
    expect(postsToB).toHaveLength(1);
    expect(videoElement()).toBe(videoEl);
    expect(videoEl.srcObject).not.toBeNull();
    expect(screen.queryByText('Connecting…')).toBeNull();
  });

  it('Never resolves to camera A when every stream read for camera B fails', async () => {
    setStreamAnswer(CAM_A, () => jsonResponse(healthyStream(CAM_A, CAM_A_WHEP_URL)));
    const view = render(viewerFor(CAM_A));
    const pcA = await goLive();
    const videoEl = videoElement();

    view.rerender(viewerFor(CAM_B));
    await flushConnect();
    expect(pcA.closed).toBe(true);

    // Every read for camera B fails, from here on — including the 5 s poll's
    // refetches.
    setStreamAnswer(CAM_B, () => errorResponse(500));
    await flushConnect();

    // RED — FR-005. The tile has no stream for the current camera and every
    // read has failed; it must present as an explicit error, not read as
    // still trying to connect.
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(screen.getByText(/could not reach the streaming service/i)).toBeDefined();

    // RED — the previous camera's picture must not survive.
    expect(videoEl.srcObject).toBeNull();

    const postsToA = fetchMock.mock.calls.filter(isPostTo(CAM_A_WHEP_URL));

    // The 5 s poll re-errors repeatedly; it must never resolve to camera A's
    // stream, and must never open a further session against camera A's URL.
    // A genuine wall-clock wait — `pollingInterval` is real RTK Query
    // machinery, not a timer this file owns to fast-forward.
    await realWait(5200);
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(videoEl.srcObject).toBeNull();
    expect(fetchMock.mock.calls.filter(isPostTo(CAM_A_WHEP_URL))).toEqual(postsToA);
  });

  it("Never resolves to camera A when the gateway refuses camera B's stream read with 403", async () => {
    setStreamAnswer(CAM_A, () => jsonResponse(healthyStream(CAM_A, CAM_A_WHEP_URL)));
    const view = render(viewerFor(CAM_A));
    const pcA = await goLive();
    const videoEl = videoElement();

    view.rerender(viewerFor(CAM_B));
    await flushConnect();
    expect(pcA.closed).toBe(true);

    setStreamAnswer(CAM_B, () => errorResponse(403));
    await flushConnect();

    // RED — an explicit error, not camera A's picture under camera B's name.
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(videoEl.srcObject).toBeNull();
  });

  it('Reads "Stream is offline" rather than Connecting when the new camera answers Offline', async () => {
    setStreamAnswer(CAM_A, () => jsonResponse(healthyStream(CAM_A, CAM_A_WHEP_URL)));
    const view = render(viewerFor(CAM_A));
    await goLive();
    const videoEl = videoElement();

    view.rerender(viewerFor(CAM_B));
    await flushConnect();

    setStreamAnswer(CAM_B, () => jsonResponse(offlineStream(CAM_B, CAM_B_WHEP_URL, 'Source powered down.')));
    await flushConnect();

    // Existing behaviour, pinned rather than exercised for the first time:
    // an Offline read already short-circuits before any client is built
    // (useWhepSession.ts:152-162). Labelled here as a characterisation of
    // that path through the swap, not as a new red.
    expect(screen.getByText('Stream is offline')).toBeDefined();
    expect(screen.getByText('Source powered down.')).toBeDefined();

    // RED — this part is NOT already correct. Camera A's frame from before
    // the swap is still attached; nothing on the offline path clears
    // `srcObject` either (that is FR-003's job, and FR-003 applies
    // regardless of which state the new camera turns out to be in).
    expect(videoEl.srcObject).toBeNull();

    const postsToA = fetchMock.mock.calls.filter(isPostTo(CAM_A_WHEP_URL));
    const postsToB = fetchMock.mock.calls.filter(isPostTo(CAM_B_WHEP_URL));
    // No session is opened for the NEW camera while it is offline.
    expect(postsToB).toHaveLength(0);
    // But the wrong-camera re-dial this whole spec is about already happened
    // by the time B's offline answer arrived (Link 3) — recorded, not hidden.
    expect(postsToA.length).toBeGreaterThanOrEqual(1);
  });

  it('Keeps the session to camera A across an unrelated re-render with a new getToken closure', async () => {
    setStreamAnswer(CAM_A, () => jsonResponse(healthyStream(CAM_A, CAM_A_WHEP_URL)));
    const view = render(viewerFor(CAM_A));
    const pcA = await goLive();
    const videoEl = videoElement();
    const streamBefore = videoEl.srcObject;

    // Same camera, a fresh getToken closure — the shape FR-003 must not
    // regress: this is not a camera change.
    view.rerender(viewerFor(CAM_A, async () => 'a-different-token'));
    await flushConnect();

    expect(pcA.closed).toBe(false);
    expect(videoEl.srcObject).toBe(streamBefore);
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(fetchMock.mock.calls.filter(isPostTo(CAM_A_WHEP_URL))).toHaveLength(1);
  });
});
