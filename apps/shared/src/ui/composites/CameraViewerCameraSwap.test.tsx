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
  ontrack: ((event: { streams: unknown[] }) => void) | null = null;
  onconnectionstatechange: (() => void) | null = null;
  connectionState = 'new';
  iceGatheringState = 'complete';
  localDescription: { type: string; sdp: string } | null = null;
  closed = false;
  remoteDescriptionSet = false;

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

  async setRemoteDescription() {
    this.remoteDescriptionSet = true;
  }

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

/**
 * A real, if minimal, stand-in for `MediaStream` — `WhepClient.ts`'s
 * `ontrack` handler calls `.getTracks()` on whatever `sessionStream` holds
 * when a second track event arrives for the same session, and a plain
 * `{ id: string }` object does not have one. Mirrors
 * `apps/shared/src/streaming/WhepClient.test.ts`'s own `FakeMediaStream`.
 */
class FakeMediaStream {
  private readonly tracks: { kind: string; id: string; stop: () => void }[];

  constructor(tracks: { kind: string; id: string; stop: () => void }[] = []) {
    this.tracks = [...tracks];
  }

  addTrack(track: { kind: string; id: string; stop: () => void }) {
    if (!this.tracks.includes(track)) this.tracks.push(track);
  }

  getTracks() {
    return [...this.tracks];
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
 * A real wall-clock wait, for the one scenario that needs the 5 s poll to
 * actually elapse. What follows this call is a mix of negative assertions
 * (no further POST to camera A) and one positive one already on screen
 * before this wait even starts — RTK's `writePendingCacheEntry` preserves
 * `error` across a pending refetch, so the "Viewer error" label does not
 * flash back to "Connecting…" every 5 s, and there is nothing new for it to
 * arrive at here.
 */
async function realWait(ms: number) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
}

/**
 * A bounded DRIVE, not a synchronisation primitive (ADR-0150 §2 permits the
 * former, bans the latter). Used at exactly two call sites below, each
 * immediately before a genuinely NEGATIVE assertion — nothing must have
 * happened — for which no condition can be polled: on the correct
 * trajectory nothing further occurs at that point, so there is no state for
 * `waitUntil` to wait FOR. A fixed number of TIMER-phase yields is what
 * gives the async connect chain (`getToken()` → `createOffer()` →
 * `setLocalDescription()` → the WHEP POST → `setRemoteDescription()`,
 * `WhepClient.ts:101-164`) a real chance to advance before the assertion
 * samples it, so a reintroduced re-dial actually gets caught rather than
 * merely not-yet-observed (#2386; #2392 phase 6 findings 1/2).
 *
 * Selector B (ADR-0150 §2 amended) bans this shape outright, by
 * construction, regardless of role — it cannot distinguish driving an
 * assertion of absence from synchronising a positive one. The
 * `eslint-disable-next-line` below is the documented escape hatch the
 * config comment names for exactly this case, not a workaround around it.
 */
async function driveConnectChain() {
  await act(async () => {
    for (let i = 0; i < 10; i += 1) {
      // Bounded DRIVE, not a synchronisation primitive: see the docblock
      // above (ADR-0150 §2 amended escape hatch; #2392 phase 6 findings 1/2).
      // eslint-disable-next-line no-restricted-syntax -- see comment above
      await new Promise((resolve) => setTimeout(resolve, 0));
      for (let j = 0; j < 5; j += 1) {
        await Promise.resolve();
      }
    }
  });
}

/**
 * Polls `condition` on real timers, inside `act`, until it is true or
 * `timeoutMs` elapses. This is the deadline-poll idiom `waitForNewPeerConnection`
 * (below) already proved out for the connect chain, generalised so every
 * caller in this file waits on the state it actually needs rather than on a
 * fixed count of macrotask yields (#2386).
 */
async function waitUntil(
  condition: () => boolean,
  description: string | (() => string),
  timeoutMs = 4000,
): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() >= deadline) {
      const resolvedDescription = typeof description === 'function' ? description() : description;
      throw new Error(`Timed out after ${timeoutMs}ms waiting for ${resolvedDescription}.`);
    }
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
    });
  }
}

/**
 * Waits for a NEW `FakePeerConnection` to exist beyond `before`, polling the
 * actual condition on real timers with a deadline — not a fixed number of
 * macrotask yields. The fixed-count settle this file used to end with was
 * reliably enough locally, but CI showed it was an assumption, not a
 * guarantee (ADR-0150): under contention it can run out before React has
 * even dispatched the effect that constructs a
 * connection, and `FakePeerConnection.lastInstance()` then silently returns
 * either `undefined` (nothing built yet) or a STALE connection from an
 * earlier call — the second of which is worse, because firing `ontrack` a
 * second time on an already-`ontrack`ed connection hits
 * `WhepClient.ts`'s "carry the previous session's tracks into the new
 * stream" branch and throws on a fake stream with no `getTracks`.
 */
async function waitForNewPeerConnection(before: number, timeoutMs = 4000): Promise<FakePeerConnection> {
  // The "have" count is built lazily, inside a callback, so it is read at
  // TIMEOUT time rather than at this call site's time: `waitUntil` invokes
  // the description only if the deadline is reached, by which point more
  // instances may have been constructed than existed when `waitUntil` was
  // called. A description built eagerly here would report a stale count.
  await waitUntil(
    () => FakePeerConnection.instances.length > before,
    () => `a new RTCPeerConnection (had ${before}, still have ${FakePeerConnection.instances.length})`,
    timeoutMs,
  );
  return FakePeerConnection.instances[FakePeerConnection.instances.length - 1]!;
}

/** Waits for a new peer connection (see above), lets its connect chain
 * (offer → POST → answer) actually settle, then drives it to `connected`
 * with a track attached — the fake stand-in for a frame having actually
 * arrived, so `srcObject` genuinely holds something the later assertions
 * can prove gets cleared. */
async function goLive(): Promise<FakePeerConnection> {
  const before = FakePeerConnection.instances.length;
  const pc = await waitForNewPeerConnection(before);
  await waitUntil(() => pc.remoteDescriptionSet, 'the WHEP answer to be applied');
  act(() => {
    pc.ontrack?.({ streams: [new FakeMediaStream()] });
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

    // A bounded drive, not a wait for a condition — see driveConnectChain's
    // docblock. Nothing about pcA.closed below needs it (rerender is
    // act-wrapped, so cleanup is already synchronous by this point); it is
    // here for postsToA() and videoEl.srcObject below, which are genuinely
    // negative and need the async connect chain given a real chance to
    // advance before they sample it (#2392 phase 6 findings 1/2).
    await driveConnectChain();

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

    // FR-003. The tile reads "Connecting…" here because the camera-change
    // effect calls `transitionTo('connecting')` on the swap, before the new
    // camera's own read has answered — not, as an earlier (unfixed-baseline)
    // version of this comment said, because a re-dial to camera A's own
    // effect setup happened to leave it there. There is no re-dial once
    // `currentData` and FR-003 are both in place (see the instance-count
    // assertion below); this is the correct mechanism producing the same
    // label. (plan.md §5's table calls this row FAILS against fully-unfixed
    // code for "the `!whepUrl` early return never fires" — that reasoning
    // was for the FR-001-only intermediate state the spec traces
    // separately, and against the actual unfixed baseline the label read
    // "Connecting…" too, just for the wrong reason. Recorded as a plan
    // discrepancy, not silently corrected.)
    expect(screen.getByText('Connecting…')).toBeVisible();

    // The true post-fix property, stated at the strongest level available:
    // no SECOND peer connection is ever constructed for this swap at all.
    // With `currentData`, `whepUrl` reads `undefined` in the SAME
    // render/effect cycle as the swap, so the session effect's
    // `if (!whepUrl || !videoEl) return undefined;` guard early-returns
    // before a client is ever built — there is no errant negotiation to
    // camera A left to complete. (An earlier version of this test drove a
    // second, distinct `FakePeerConnection` to `connected` to prove the
    // headline defect directly; that instance never exists once the fix is
    // in place, so `postsToA()` above and this instance count are what
    // prove its absence instead — asserting object identity against a
    // connection that cannot exist doesn't hold against a correct
    // implementation, and forcing events onto the already-closed `pcA`
    // would test something no real, closed `RTCPeerConnection` can do:
    // `ontrack` never fires again after `close()`.)
    expect(FakePeerConnection.instances).toHaveLength(1);

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
    expect(pcA.closed).toBe(true);

    // Every read for camera B fails, from here on — including the 5 s poll's
    // refetches.
    setStreamAnswer(CAM_B, () => errorResponse(500));
    await waitUntil(
      () => screen.queryByText(/could not reach the streaming service/i) !== null,
      'the tile to report a failed read for camera B',
    );

    // RED — FR-005. The tile has no stream for the current camera and every
    // read has failed; it must present as an explicit error, not read as
    // still trying to connect.
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(screen.getByText(/could not reach the streaming service/i)).toBeVisible();

    // RED — the previous camera's picture must not survive.
    expect(videoEl.srcObject).toBeNull();

    const postsToA = fetchMock.mock.calls.filter(isPostTo(CAM_A_WHEP_URL));

    // The 5 s poll re-errors repeatedly; it must never resolve to camera A's
    // stream, and must never open a further session against camera A's URL.
    // A genuine wall-clock wait — `pollingInterval` is real RTK Query
    // machinery, not a timer this file owns to fast-forward.
    await realWait(5200);
    // Asserts the property spec.md verified in RTK's writePendingCacheEntry:
    // `error` survives a pending refetch, so the tile does not flash back to
    // "Connecting…" every 5 s. A plain assertion, not a wait: `realWait`
    // already drained everything async, and the label was already showing
    // as of the `getByText` above (label and hint render from the same
    // `ViewerOverlay` call) — there is no condition left to poll for.
    expect(screen.getByText('Viewer error')).toBeVisible();
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
    expect(pcA.closed).toBe(true);

    setStreamAnswer(CAM_B, () => errorResponse(403));
    await waitUntil(() => screen.queryByText('Viewer error') !== null, 'the tile to report a failed read for camera B');

    // RED — an explicit error, not camera A's picture under camera B's name.
    expect(screen.getByText('Viewer error')).toBeVisible();
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(videoEl.srcObject).toBeNull();
  });

  it('Reads "Stream is offline" rather than Connecting when the new camera answers Offline', async () => {
    setStreamAnswer(CAM_A, () => jsonResponse(healthyStream(CAM_A, CAM_A_WHEP_URL)));
    const view = render(viewerFor(CAM_A));
    await goLive();
    const videoEl = videoElement();

    view.rerender(viewerFor(CAM_B));

    setStreamAnswer(CAM_B, () => jsonResponse(offlineStream(CAM_B, CAM_B_WHEP_URL, 'Source powered down.')));
    await waitUntil(
      () => screen.queryByText('Stream is offline') !== null,
      "camera B's offline state to reach the tile",
    );

    // Existing behaviour, pinned rather than exercised for the first time:
    // an Offline read already short-circuits before any client is built
    // (useWhepSession.ts:152-162). Labelled here as a characterisation of
    // that path through the swap, not as a new red.
    expect(screen.getByText('Stream is offline')).toBeVisible();
    expect(screen.getByText('Source powered down.')).toBeVisible();

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

  it('Reads Stream is offline, not Connecting forever, when camera B is already warm in the cache', async () => {
    setStreamAnswer(CAM_A, () => jsonResponse(healthyStream(CAM_A, CAM_A_WHEP_URL)));
    setStreamAnswer(CAM_B, () => jsonResponse(offlineStream(CAM_B, CAM_B_WHEP_URL, 'Source powered down.')));

    // Warm camera B into the RTK Query cache: mount a second CameraViewer on
    // it (same store), let its own read settle, then unmount it. The cache
    // entry survives losing its last subscriber for keepUnusedDataFor (60 s
    // default) — a camera permuted between tiles on the same wall, or shown
    // anywhere in the last minute, leaves exactly this residue behind.
    const warm = render(viewerFor(CAM_B));
    // Wait for the premise itself, not a fixed count: if the read for camera
    // B has not actually landed in the cache when it unmounts, the entry
    // below is discarded and this test would silently go on to exercise the
    // COLD ordering while claiming the warm one (#2386).
    await waitUntil(
      () => streamsApi.endpoints.getStream.select(CAM_B)(store.getState()).data !== undefined,
      'camera B to be warm in the RTK Query cache',
    );
    warm.unmount();

    const view = render(viewerFor(CAM_A));
    const pcA = await goLive();
    const videoEl = videoElement();

    // Act — the swap. Camera B's data is ALREADY cached, so `currentData`
    // (and therefore `offlineMessage`) resolves in the SAME commit as this
    // rerender — unlike every other scenario in this file, where camera B's
    // GET is still in flight when the effects first run. This is the only
    // scenario in the file that exercises that ordering.
    view.rerender(viewerFor(CAM_B));

    // Deliberately a synchronous read, NOT a `waitUntil` — this assertion
    // exists solely to pin that the camera-change effect's
    // `transitionTo('connecting')` and the session effect's
    // `transitionTo('offline', ...)` fire in the SAME commit as this
    // rerender, in that order (see the GUARD comment below). `rerender` is
    // itself act-wrapped, so if that same-commit guarantee holds, the label
    // is already on screen the instant `rerender` returns — no wait is
    // needed to observe it. Polling here would ALSO pass if the label
    // instead arrived in a LATER commit (e.g. a shortened
    // `keepUnusedDataFor` evicting the warm cache entry, forcing a cold
    // refetch that answers a tick later): exactly the ordering the premise
    // wait above exists to rule out, silently handed back at the one
    // assertion this whole scenario exists to make. Do not convert this to
    // a wait.
    expect(screen.queryByText('Stream is offline')).not.toBeNull();

    expect(videoElement()).toBe(videoEl);
    expect(pcA.closed).toBe(true);

    // GUARD, not a red: this passes against the shipped code, because the
    // camera-change effect (useWhepSession.ts) is declared above the session
    // effect, so the session effect's cleanup for camera A always runs
    // before the camera-change effect's setup, and — the part nothing had
    // written down — the camera-change effect's `transitionTo('connecting')`
    // runs BEFORE the session effect's `transitionTo('offline', ...)` when
    // both fire in this same commit, so 'offline' is the one that lands
    // last and wins. Reordering the two effect declarations flips which one
    // wins and the tile reads "Connecting…" forever instead — see the
    // counterfactual recorded in the PR body, not committed here.
    expect(screen.getByText('Stream is offline')).toBeVisible();
    expect(screen.getByText('Source powered down.')).toBeVisible();
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(videoEl.srcObject).toBeNull();
    expect(fetchMock.mock.calls.filter(isPostTo(CAM_B_WHEP_URL))).toHaveLength(0);
  });

  it('Reads Viewer error, not Idle, on a first mount whose stream read fails', async () => {
    // FR-007 note, not a swap scenario: FR-005's error branch
    // (CameraViewer.tsx's `failedRead`) is not scoped to a camera change —
    // it fires on ANY failed read with no stream for the current camera,
    // including a first mount, where the tile previously read "Idle". An
    // improvement, and within FR-005 as written, but it contradicts FR-007's
    // "behaviour outside a camera change is unchanged", and nothing else in
    // the suite covers a first-mount failure. Pinned so a future change to
    // that scoping is a deliberate decision, not a silent one.
    setStreamAnswer(CAM_A, () => errorResponse(500));
    render(viewerFor(CAM_A));
    await waitUntil(
      () => screen.queryByText('Viewer error') !== null,
      'the first-mount read failure to reach the tile',
    );

    expect(screen.getByText('Viewer error')).toBeVisible();
    expect(screen.getByText('Could not reach the streaming service.')).toBeVisible();
    expect(screen.queryByText('Idle')).toBeNull();
    expect(FakePeerConnection.instances).toHaveLength(0);
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

    // Genuinely negative — nothing must have happened — and there is no
    // condition to poll for: on the correct trajectory nothing further
    // occurs here at all. A bounded drive is the only instrument (#2386;
    // #2392 phase 6 findings 1/2); see driveConnectChain's docblock.
    await driveConnectChain();

    expect(pcA.closed).toBe(false);
    expect(videoEl.srcObject).toBe(streamBefore);
    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(fetchMock.mock.calls.filter(isPostTo(CAM_A_WHEP_URL))).toHaveLength(1);
  });
});
