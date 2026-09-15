import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WhepClient } from './WhepClient.js';

/**
 * The parts of a `MediaStreamTrack` this harness needs — the same structural
 * shape the `receivers` fake below already uses, and the reason no browser is
 * required to exercise the attachment path.
 */
type FakeTrack = { kind: string; id: string; stop: () => void };

/**
 * A receiver double `setPlayoutTarget` can act on (spec 142 T001). Widened
 * from `{ track: { stop: () => void } }[]` — the pre-142 shape carried no
 * `kind` and no `jitterBufferTarget`, so `setPlayoutTarget`'s
 * `receiver.track?.kind !== 'video'` guard skipped every receiver this
 * harness could build, and no test here ever reached any branch of the
 * method under test.
 */
type FakeReceiver = { track: FakeTrack; jitterBufferTarget?: number | null };

class FakePeerConnection {
  static instances: FakePeerConnection[] = [];
  static initialIceGatheringState = 'complete';
  static lastInstance(): FakePeerConnection {
    return FakePeerConnection.instances[FakePeerConnection.instances.length - 1]!;
  }
  ontrack: ((event: { streams: MediaStream[]; track: FakeTrack }) => void) | null = null;
  onconnectionstatechange: (() => void) | null = null;
  connectionState = 'new';
  iceGatheringState: string;
  transceivers: { direction: string; kind: string }[] = [];
  localDescription: RTCSessionDescriptionInit | null = null;
  remoteDescription: RTCSessionDescriptionInit | null = null;
  closed = false;
  receivers: FakeReceiver[] = [];
  private iceGatheringListeners: (() => void)[] = [];

  constructor() {
    this.iceGatheringState = FakePeerConnection.initialIceGatheringState;
    FakePeerConnection.instances.push(this);
  }

  addTransceiver(kind: string, init: { direction: string }) {
    this.transceivers.push({ kind, direction: init.direction });
  }

  async createOffer(): Promise<RTCSessionDescriptionInit> {
    return { type: 'offer', sdp: 'v=0\r\no=fake 1 1 IN IP4 127.0.0.1\r\ns=-\r\n' };
  }

  async setLocalDescription(desc: RTCSessionDescriptionInit) {
    this.localDescription = desc;
  }

  async setRemoteDescription(desc: RTCSessionDescriptionInit) {
    this.remoteDescription = desc;
  }

  getReceivers() {
    return this.receivers;
  }

  close() {
    this.closed = true;
  }

  addEventListener(_type: string, listener: () => void) {
    this.iceGatheringListeners.push(listener);
  }

  removeEventListener(_type: string, listener: () => void) {
    this.iceGatheringListeners = this.iceGatheringListeners.filter((l) => l !== listener);
  }

  completeIceGathering() {
    this.iceGatheringState = 'complete';
    for (const listener of [...this.iceGatheringListeners]) {
      listener();
    }
  }

  setConnectionState(state: string) {
    this.connectionState = state;
    this.onconnectionstatechange?.();
  }

  /**
   * Delivers one `track` event, the way a real peer connection does for each
   * accepted `m=` section while applying the answer. `streams` defaults to
   * empty because that is the shape under test: an answer whose tracks carry
   * no `msid`.
   */
  fireTrack(track: FakeTrack, streams: MediaStream[] = []) {
    this.ontrack?.({ streams, track });
  }
}

/**
 * Stands in for the constructor a client needs to build a stream around a bare
 * track. Neither vitest environment in this package provides one — the `node`
 * environment has no WebRTC at all, and jsdom does not implement `MediaStream`
 * — so it is stubbed here for exactly the reason `RTCPeerConnection` already
 * is. No browser is required to exercise this path.
 */
class FakeMediaStream {
  private readonly tracks: FakeTrack[];

  constructor(tracks: FakeTrack[] = []) {
    this.tracks = [...tracks];
  }

  addTrack(track: FakeTrack) {
    if (!this.tracks.includes(track)) this.tracks.push(track);
  }

  getTracks(): FakeTrack[] {
    return [...this.tracks];
  }
}

function aTrack(kind: 'video' | 'audio'): FakeTrack {
  return { kind, id: `${kind}-track`, stop: () => undefined };
}

/**
 * Four receiver builders for `setPlayoutTarget` (spec 142 T001), one per
 * outcome `WhepClient.setPlayoutTarget` must be able to produce.
 */
function videoReceiverWithTarget(): FakeReceiver {
  return { track: aTrack('video'), jitterBufferTarget: null };
}

function videoReceiverWithoutTarget(): FakeReceiver {
  return { track: aTrack('video') };
}

function videoReceiverThatThrows(): FakeReceiver {
  const receiver = { track: aTrack('video') } as FakeReceiver;
  Object.defineProperty(receiver, 'jitterBufferTarget', {
    set() {
      throw new Error('engine refused the value');
    },
    get: () => null,
    configurable: true,
  });
  return receiver;
}

function audioReceiverWithTarget(): FakeReceiver {
  return { track: aTrack('audio'), jitterBufferTarget: null };
}

function attachedTracks(videoEl: HTMLVideoElement): FakeTrack[] {
  const attached = videoEl.srcObject;
  if (attached === null) return [];
  return (attached as unknown as FakeMediaStream).getTracks();
}

/**
 * The kinds a `<video>` would actually play — the operator-visible fact. A list
 * without `video` in it is a black tile, whatever the attachment did.
 */
function attachedKinds(videoEl: HTMLVideoElement): string[] {
  return attachedTracks(videoEl).map((track) => track.kind);
}

/** The `[resilience]` payloads logged under `transition`, in order. */
function resilienceLines(calls: unknown[][], transition: string): Record<string, unknown>[] {
  const lines: Record<string, unknown>[] = [];
  for (const [prefix, payload] of calls) {
    if (prefix !== '[resilience]' || typeof payload !== 'object' || payload === null) continue;
    const line = payload as Record<string, unknown>;
    if (line.transition === transition) lines.push(line);
  }
  return lines;
}

const answerSdp = 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n';

/**
 * Drains the async connect chain (offer -> POST -> answer). Bare, NOT
 * act-wrapped — this suite renders no React tree, so there is no effect
 * flush to fold in. The five same-named helpers in the composite suites
 * (CameraViewer.test.tsx and siblings) are act-wrapped and use 12 rounds;
 * both are sound and agree in semantics (#2392 phase 6 finding 6), but
 * copying this bare one into a React suite would silently drop the `act`
 * flush — copy-from-a-neighbour is exactly how the original flushConnect
 * defect spread (ADR-0150).
 */
async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 10; i += 1) {
    await Promise.resolve();
  }
}

describe('WhepClient', () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let videoEl: HTMLVideoElement;

  beforeEach(() => {
    FakePeerConnection.instances = [];
    FakePeerConnection.initialIceGatheringState = 'complete';
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    (globalThis as unknown as { MediaStream: typeof FakeMediaStream }).MediaStream = FakeMediaStream;
    videoEl = { srcObject: null } as unknown as HTMLVideoElement;
    fetchMock = vi.fn();
    globalThis.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('Posts an SDP offer with the bearer token and applies the answer', async () => {
    fetchMock.mockResolvedValue(
      new Response(answerSdp, {
        status: 200,
        headers: { 'Content-Type': 'application/sdp' },
      }),
    );
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token-xyz',
    });

    await client.connect(videoEl);

    expect(fetchMock).toHaveBeenCalledOnce();
    const [, init] = fetchMock.mock.calls[0]!;
    expect((init as RequestInit).method).toBe('POST');
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer token-xyz');
    expect(headers['Content-Type']).toBe('application/sdp');
    expect(FakePeerConnection.lastInstance().remoteDescription?.type).toBe('answer');
  });

  it('Throws WhepError(unauthorized) on a 401 response', async () => {
    fetchMock.mockResolvedValue(new Response('', { status: 401 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'bad-token',
    });

    await expect(client.connect(videoEl)).rejects.toMatchObject({
      name: 'WhepError',
      kind: 'unauthorized',
    });
  });

  it('Throws WhepError(stream-unavailable) when the body mentions unavailable', async () => {
    fetchMock.mockResolvedValue(new Response('stream is unavailable (offline)', { status: 403 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });

    await expect(client.connect(videoEl)).rejects.toMatchObject({
      name: 'WhepError',
      kind: 'stream-unavailable',
    });
  });

  it('Throws WhepError(forbidden) on a generic 403', async () => {
    fetchMock.mockResolvedValue(new Response('missing scope', { status: 403 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });

    await expect(client.connect(videoEl)).rejects.toMatchObject({
      name: 'WhepError',
      kind: 'forbidden',
    });
  });

  it('close() releases the peer connection', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    client.close();

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
  });

  it('Throws when reused without creating a new instance', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    await expect(client.connect(videoEl)).rejects.toThrow(/already connected/i);
  });

  it('Invokes onConnectionStateChange whenever the peer connection state changes', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const states: string[] = [];
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
      onConnectionStateChange: (state) => states.push(state),
    });
    await client.connect(videoEl);

    FakePeerConnection.lastInstance().setConnectionState('connected');
    FakePeerConnection.lastInstance().setConnectionState('failed');

    expect(states).toEqual(['connected', 'failed']);
  });

  it('close() DELETEs the captured WHEP session exactly once with the bearer token', async () => {
    fetchMock.mockResolvedValue(
      new Response(answerSdp, {
        status: 200,
        headers: { Location: '/cam-x/whep/sessions/abc' },
      }),
    );
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    client.close();
    client.close();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    const deleteCalls = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit).method === 'DELETE');
    expect(deleteCalls).toHaveLength(1);
    const [url, init] = deleteCalls[0]!;
    expect(url).toBe('http://mediamtx.test/cam-x/whep/sessions/abc');
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer token');
    expect((init as RequestInit).keepalive).toBe(true);
  });

  it('close() DELETEs with the token current at close time, not the connect-time one (FR-015)', async () => {
    let token = 'token-at-connect';
    fetchMock.mockResolvedValue(
      new Response(answerSdp, {
        status: 200,
        headers: { Location: '/cam-x/whep/sessions/abc' },
      }),
    );
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => token,
    });
    await client.connect(videoEl);

    token = 'token-after-renewal';
    client.close();
    await new Promise((resolve) => setTimeout(resolve, 0));

    const deleteCalls = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit).method === 'DELETE');
    expect(deleteCalls).toHaveLength(1);
    const headers = (deleteCalls[0]![1] as RequestInit).headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer token-after-renewal');
  });

  it('close() without a captured session URL performs local teardown only', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);
    fetchMock.mockClear();

    client.close();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('close() with no captured session URL logs nothing and asks nothing (#2198)', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);
    fetchMock.mockClear();

    client.close();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fetchMock, 'no release request must be issued when nothing was captured').not.toHaveBeenCalled();
    expect(resilienceLines(info.mock.calls, 'session-release-failed')).toHaveLength(0);
  });

  /**
   * Spec 142 T002 / FR-001…FR-005. `releaseSession()` discarded every DELETE
   * outcome via `.catch(() => undefined)` (#2198 item 1). Each case here
   * `mockResolvedValueOnce`s the offer/answer POST before setting the
   * DELETE's answer — one `vi.fn` serves both requests, so a bare
   * `mockResolvedValue` would hand the DELETE's failure to `connect()` and
   * fail for an unrelated reason (plan R4).
   */
  describe('a session release that fails (#2198)', () => {
    async function connectedSessionWithCapturedUrl(getToken: () => Promise<string | null>) {
      fetchMock.mockResolvedValueOnce(
        new Response(answerSdp, {
          status: 200,
          headers: { Location: '/cam-x/whep/sessions/abc' },
        }),
      );
      const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);
      const client = new WhepClient({
        whepUrl: 'http://mediamtx.test/cam-x/whep',
        getToken,
      });
      await client.connect(videoEl);
      return { client, info };
    }

    it('Reports session-release-failed with the error, and still tears the peer connection down locally, when the DELETE fetch rejects', async () => {
      const { client, info } = await connectedSessionWithCapturedUrl(async () => 'token');
      fetchMock.mockRejectedValueOnce(new Error('network down'));

      client.close();
      // Synchronous, not eventual: teardown must not wait on the DELETE's
      // outcome. A `close()` rewritten to `await` the release before tearing
      // down would still pass the two assertions below (both run after a
      // macrotask), so this is the one case in the file that pins the
      // ordering rather than merely the outcome.
      expect(FakePeerConnection.lastInstance().closed, 'teardown must not wait on the release').toBe(true);
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(resilienceLines(info.mock.calls, 'session-release-failed')).toEqual([
        { subsystem: 'stream', transition: 'session-release-failed', error: 'Error: network down' },
      ]);
      expect(FakePeerConnection.lastInstance().closed, 'the report must not have displaced local teardown').toBe(true);
    });

    it('Reports session-release-failed with the status when the DELETE resolves 401', async () => {
      const { client, info } = await connectedSessionWithCapturedUrl(async () => 'token');
      fetchMock.mockResolvedValueOnce(new Response('', { status: 401 }));

      client.close();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(resilienceLines(info.mock.calls, 'session-release-failed')).toEqual([
        { subsystem: 'stream', transition: 'session-release-failed', status: 401 },
      ]);
      expect(FakePeerConnection.lastInstance().closed, 'the report must not have displaced local teardown').toBe(true);
    });

    it('Reports session-release-failed with the status when the DELETE resolves 500', async () => {
      const { client, info } = await connectedSessionWithCapturedUrl(async () => 'token');
      fetchMock.mockResolvedValueOnce(new Response('', { status: 500 }));

      client.close();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(resilienceLines(info.mock.calls, 'session-release-failed')).toEqual([
        { subsystem: 'stream', transition: 'session-release-failed', status: 500 },
      ]);
      expect(FakePeerConnection.lastInstance().closed, 'the report must not have displaced local teardown').toBe(true);
    });

    it('Reports session-release-failed with the error when getToken rejects at release time', async () => {
      let resolvedOnce = false;
      const { client, info } = await connectedSessionWithCapturedUrl(async () => {
        if (!resolvedOnce) {
          resolvedOnce = true;
          return 'token';
        }
        throw new Error('token refresh failed');
      });

      client.close();
      await new Promise((resolve) => setTimeout(resolve, 0));

      const lines = resilienceLines(info.mock.calls, 'session-release-failed');
      expect(lines).toHaveLength(1);
      expect(lines[0]).toHaveProperty('error');
      expect(FakePeerConnection.lastInstance().closed, 'the report must not have displaced local teardown').toBe(true);
    });

    it('Says nothing when the release succeeds', async () => {
      const { client, info } = await connectedSessionWithCapturedUrl(async () => 'token');
      fetchMock.mockResolvedValueOnce(new Response('', { status: 200 }));

      client.close();
      await new Promise((resolve) => setTimeout(resolve, 0));

      const deleteCalls = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit).method === 'DELETE');
      expect(deleteCalls, 'the DELETE must actually have been issued').toHaveLength(1);
      expect(resilienceLines(info.mock.calls, 'session-release-failed')).toHaveLength(0);
    });
  });

  /**
   * Spec 142 T003/T004 / FR-006…FR-009. `setPlayoutTarget` collapsed three
   * distinct causes into one `false` (#2198 item 2); these cases pin the four
   * outcomes independently, written against the *values* `setPlayoutTarget`
   * returns rather than against a `PlayoutTargetOutcome` import — the type
   * does not exist yet, and `toBe` is not typed against the receiver, so each
   * case compiles and runs today, failing at the `toBe` with a legible
   * `expected <boolean> to be '<outcome>'` (spec 061 `24e6fc4c`; a `TS2305`
   * is not a red test).
   */
  describe('setPlayoutTarget (#2198)', () => {
    async function connectedClient(): Promise<{ client: WhepClient; pc: FakePeerConnection }> {
      fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
      const client = new WhepClient({
        whepUrl: 'http://mediamtx.test/cam-x/whep',
        getToken: async () => 'token',
      });
      await client.connect(videoEl);
      return { client, pc: FakePeerConnection.lastInstance() };
    }

    it('Answers not-connected when the client has never connected', () => {
      const client = new WhepClient({
        whepUrl: 'http://mediamtx.test/cam-x/whep',
        getToken: async () => 'token',
      });

      expect(client.setPlayoutTarget(120)).toBe('not-connected');
    });

    it('Answers not-connected when the peer connection reports no receivers (FR-007)', async () => {
      const { client, pc } = await connectedClient();
      pc.receivers = [];

      expect(client.setPlayoutTarget(120)).toBe('not-connected');
    });

    it('Answers not-connected and leaves the value untouched when the only receiver is audio (FR-007)', async () => {
      const { client, pc } = await connectedClient();
      const audio = audioReceiverWithTarget();
      pc.receivers = [audio];

      const outcome = client.setPlayoutTarget(120);

      expect(audio.jitterBufferTarget, 'an audio receiver must never be written to').toBe(null);
      expect(outcome).toBe('not-connected');
    });

    it('Answers unsupported when the one video receiver carries no jitterBufferTarget', async () => {
      const { client, pc } = await connectedClient();
      pc.receivers = [videoReceiverWithoutTarget()];

      expect(client.setPlayoutTarget(120)).toBe('unsupported');
    });

    it('Answers refused, without throwing, when the video receiver rejects the assignment (FR-008)', async () => {
      const { client, pc } = await connectedClient();
      pc.receivers = [videoReceiverThatThrows()];

      const outcome = client.setPlayoutTarget(120);

      expect(outcome).toBe('refused');
    });

    it('Answers applied when at least one video receiver accepts, even if another throws (plan invariant 3)', async () => {
      const { client, pc } = await connectedClient();
      const accepting = videoReceiverWithTarget();
      const throwing = videoReceiverThatThrows();
      pc.receivers = [accepting, throwing];

      const outcome = client.setPlayoutTarget(120);

      expect(accepting.jitterBufferTarget, 'the accepting receiver must have been written to').toBe(120);
      expect(outcome).toBe('applied');
    });

    it('Applies the target to a single accepting video receiver (FR-009, the actuation proof)', async () => {
      const { client, pc } = await connectedClient();
      const receiver = videoReceiverWithTarget();
      pc.receivers = [receiver];

      const outcome = client.setPlayoutTarget(120);

      expect(receiver.jitterBufferTarget, 'the actuation must actually have happened').toBe(120);
      expect(outcome).toBe('applied');
    });

    it('Applies the target to every qualifying video receiver, not just the first (plan invariant 2)', async () => {
      const { client, pc } = await connectedClient();
      const first = videoReceiverWithTarget();
      const second = videoReceiverWithTarget();
      pc.receivers = [first, second];

      const outcome = client.setPlayoutTarget(120);

      expect(first.jitterBufferTarget, 'the first receiver must have been written to').toBe(120);
      expect(second.jitterBufferTarget, 'the second receiver must have been written to too').toBe(120);
      expect(outcome).toBe('applied');
    });
  });

  it('Aborting mid-connect leaves no live peer connection', async () => {
    fetchMock.mockImplementation(
      (_url: unknown, init?: RequestInit) =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
        }),
    );
    const controller = new AbortController();
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    const pending = client.connect(videoEl, controller.signal);
    await flushMicrotasks();

    controller.abort();

    await expect(pending).rejects.toMatchObject({ name: 'WhepError', kind: 'network' });
    expect(FakePeerConnection.lastInstance().closed).toBe(true);
  });

  it('Waits for ICE gathering completion before posting the offer', async () => {
    FakePeerConnection.initialIceGatheringState = 'gathering';
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    const pending = client.connect(videoEl);
    await flushMicrotasks();

    expect(fetchMock).not.toHaveBeenCalled();

    FakePeerConnection.lastInstance().completeIceGathering();
    await pending;

    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('Caps the ICE gathering wait at 250 ms', async () => {
    vi.useFakeTimers();
    FakePeerConnection.initialIceGatheringState = 'gathering';
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    const pending = client.connect(videoEl);

    await vi.advanceTimersByTimeAsync(249);
    expect(fetchMock).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1);
    await pending;
    expect(fetchMock).toHaveBeenCalledOnce();
  });

  /**
   * Track attachment when the answer carries no `msid` (issue #2108).
   *
   * <p>
   * <b>The gap.</b> `ontrack` attached `event.streams[0]` and did nothing at
   * all when that array was empty. `msid` is negotiated, not guaranteed — a
   * renegotiating SFU, a MediaMTX whose SDP shape moves under a floating
   * `latest` tag (#2103), or a track added with no stream association all
   * produce an empty `streams` for every track. Nothing then reaches the
   * element, and because `useWhepSession` derives `live` from
   * `pc.connectionState` alone, the tile still labels itself <b>Live</b> over
   * a black picture. No error, no log, no state change: the most misleading
   * state the kiosk can reach.
   * </p>
   *
   * <p>
   * <b>Why building the stream here is safe.</b> Nothing downstream reads the
   * stream MediaMTX supplies — not its id, not its track events. The two
   * consumers of a live session, `stats()` and `setPlayoutTarget()`, both go
   * through `pc.getStats()` / `pc.getReceivers()`, and teardown stops receiver
   * tracks. `srcObject` is the only reader, and a `<video>` plays whatever
   * stream holds the tracks.
   * </p>
   *
   * <p>
   * <b>Why combining rather than replacing.</b> The client opens two recvonly
   * transceivers, so a session delivers two track events in an order nobody
   * controls. Building a fresh stream per event would let the second evict the
   * first — audio arriving last would silently unmount the video, which is the
   * same black tile by a different route. One stream per session, added to.
   * </p>
   *
   * <p>
   * <b>What this deliberately does not change, and what has since changed.</b>
   * When this was written, `live` followed the connection state, and gating it on
   * attachment was out of scope: it changes the hook's state machine and needs a
   * retry path for "connected but nothing ever arrived", or a tile trades a false
   * Live for a permanent Connecting…. That was filed as <b>#2111</b> — not #2109,
   * which carves this case out in its own second paragraph — and #2111 shipped in
   * spec 094 (`5ee1d6cd`). Promotion is now gated on
   * `getVideoPlaybackQuality().totalVideoFrames > 0`, polled every 250 ms, with a
   * 3000 ms fall-through into the existing retry ladder: the retry path this
   * paragraph called missing. What stays true is the scope of *this* suite —
   * it covers track association, not promotion.
   * </p>
   */
  describe('a track carrying no stream association (#2108)', () => {
    async function connectedSession(): Promise<FakePeerConnection> {
      fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
      const client = new WhepClient({
        whepUrl: 'http://mediamtx.test/cam-x/whep',
        getToken: async () => 'token',
      });
      await client.connect(videoEl);
      return FakePeerConnection.lastInstance();
    }

    /** RED. The gap itself: the track never reached the element. */
    it('Still reaches the video element', async () => {
      const pc = await connectedSession();
      const video = aTrack('video');

      pc.fireTrack(video);

      expect(videoEl.srcObject, 'a track with no msid must still be attached, not dropped').not.toBeNull();
      expect(attachedTracks(videoEl)).toEqual([video]);
    });

    /**
     * RED, and the one that pins the shape of the fix. Wrapping each event in
     * its own `MediaStream` would pass the case above and still lose the
     * picture whenever audio happens to arrive second.
     */
    it('Is combined with the session tracks already attached, never replacing them', async () => {
      const pc = await connectedSession();
      const video = aTrack('video');
      const audio = aTrack('audio');

      pc.fireTrack(video);
      pc.fireTrack(audio);

      expect(attachedTracks(videoEl), 'the audio track must join the video track, not evict it').toEqual([
        video,
        audio,
      ]);
    });

    /**
     * RED. An answer with no `msid` is a server shape this client did not
     * expect, and it stays repairable only if someone can see it happened.
     * `stream` is the subsystem `useWhepSession` already logs this tile's
     * transitions under, so no new channel is introduced.
     */
    it('Says so on the resilience log, once per track', async () => {
      const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);
      const pc = await connectedSession();

      pc.fireTrack(aTrack('video'));
      pc.fireTrack(aTrack('audio'));

      expect(resilienceLines(info.mock.calls, 'track-without-stream')).toEqual([
        expect.objectContaining({ subsystem: 'stream', transition: 'track-without-stream', kind: 'video' }),
        expect.objectContaining({ subsystem: 'stream', transition: 'track-without-stream', kind: 'audio' }),
      ]);
    });

    /**
     * CONTROL, green today and required to stay green. The working path is
     * every session against a MediaMTX that does send `msid`: the stream the
     * SFU supplied is attached as-is, and nothing is reported as a fault.
     */
    it('Leaves a track that does carry its stream attaching that stream, silently', async () => {
      const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);
      const pc = await connectedSession();
      const sfuStream = new FakeMediaStream([aTrack('video')]) as unknown as MediaStream;

      pc.fireTrack(aTrack('video'), [sfuStream]);

      expect(videoEl.srcObject, 'the SFU-supplied stream must be attached unchanged').toBe(sfuStream);
      expect(resilienceLines(info.mock.calls, 'track-without-stream')).toEqual([]);
    });

    /**
     * An answer that mixes the two shapes — one `m=` section carrying an `msid`
     * and one not.
     *
     * <p>
     * <b>`msid` is per-`m=`-section, not per-answer.</b> The two tests above
     * hold within the no-`msid` family and the control holds within the
     * with-`msid` family; neither says anything about a session where the
     * families meet. They do meet: a MediaMTX that publishes video with a
     * stream association and audio without produces exactly one event of each
     * shape, in an order nobody controls.
     * </p>
     *
     * <p>
     * <b>Both orders, because a one-sided guard rots.</b> The failing order is
     * the regression; the passing order is what the fix must not break on its
     * way to fixing the other. Each asserts the kinds a `&lt;video&gt;` would
     * play, because "the picture is still there" is the fact an operator can
     * check and the attachment mechanics are not.
     * </p>
     */
    describe('an answer mixing a track with a stream association and one without', () => {
      /**
       * RED. The accumulation runs inside the no-`msid` branch only, so the
       * bare audio track starts a fresh session stream holding audio alone and
       * assigns it over the SFU stream — the picture goes out. Pre-fix the bare
       * track was dropped and the picture survived, so this is a regression the
       * fix introduced rather than a gap it left.
       */
      it('Keeps the picture when the video track carried a stream and the audio track did not', async () => {
        const pc = await connectedSession();
        const sfuStream = new FakeMediaStream([aTrack('video')]) as unknown as MediaStream;

        pc.fireTrack(aTrack('video'), [sfuStream]);
        pc.fireTrack(aTrack('audio'));

        expect(attachedKinds(videoEl), 'a bare audio track must not evict the picture already attached').toContain(
          'video',
        );
      });

      /**
       * The same meeting from the other side, and no longer green by luck. This
       * order used to pass because the SFU stream was assigned wholesale over
       * whatever was attached: the picture arrived, and the audio that arrived
       * first was silently dropped on the way. Asserting only `toContain('video')`
       * could not tell the two apart, so it asserts both kinds now — and that
       * `srcObject` is still the stream the SFU supplied, because carrying the
       * session tracks into it must not become substituting a synthesised copy
       * for it.
       */
      it('Keeps both the picture and the audio when the audio track arrived first without a stream', async () => {
        const pc = await connectedSession();
        const sfuStream = new FakeMediaStream([aTrack('video')]) as unknown as MediaStream;

        pc.fireTrack(aTrack('audio'));
        pc.fireTrack(aTrack('video'), [sfuStream]);

        expect(attachedKinds(videoEl), 'a bare audio track must survive the SFU stream arriving after it').toEqual(
          expect.arrayContaining(['video', 'audio']),
        );
        expect(videoEl.srcObject, 'the SFU stream must be merged into, not replaced by a synthesised one').toBe(
          sfuStream,
        );
      });
    });

    /**
     * A reconnect attaching to the element the previous session left behind.
     *
     * <p>
     * <b>The retry path constructs a new client against the same element.</b>
     * `useWhepSession` builds a fresh `WhepClient` and hands it the same
     * `videoEl` (`useWhepSession.ts`, the `new WhepClient` inside the connect
     * effect), and nothing in this repo ever clears `srcObject`. So when the
     * first bare track of a reconnect arrives, the element is still holding the
     * dead session's stream, whose tracks teardown has already stopped.
     * </p>
     *
     * <p>
     * <b>Why that is the hole worth guarding.</b> Seeding the session stream
     * from `srcObject` — a shorter fix than the per-call local, and one that
     * passes every other test in this file — splices the live track in beside
     * stopped ones, including a stale video track a `&lt;video&gt;` may select
     * over the live one. That is the black tile #2108 set out to remove,
     * reached from the other side and this time with the tile reporting Live
     * over a frozen last frame.
     * </p>
     *
     * <p>
     * Both leftovers are covered, because a session ends holding one of two
     * different streams: the one this client synthesised for bare tracks, or
     * the one the SFU supplied with an `msid`.
     * </p>
     */
    describe('a reconnect against the element the previous session left attached', () => {
      async function aConnectedClient(): Promise<WhepClient> {
        fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
        const client = new WhepClient({
          whepUrl: 'http://mediamtx.test/cam-x/whep',
          getToken: async () => 'token',
        });
        await client.connect(videoEl);
        return client;
      }

      it('Attaches only the new track when the closed session left a stream built here', async () => {
        const stale: FakeTrack = { ...aTrack('video'), id: 'video-of-the-closed-session' };
        const previous = await aConnectedClient();
        FakePeerConnection.lastInstance().fireTrack(stale);
        previous.close();

        await aConnectedClient();
        const live: FakeTrack = { ...aTrack('video'), id: 'video-of-the-reconnected-session' };
        FakePeerConnection.lastInstance().fireTrack(live);

        expect(attachedTracks(videoEl), 'the reconnected session must not adopt the stopped track').not.toContain(
          stale,
        );
        expect(attachedTracks(videoEl)).toEqual([live]);
      });

      it('Attaches only the new track when the closed session left the SFU stream', async () => {
        const stale: FakeTrack = { ...aTrack('video'), id: 'video-of-the-closed-session' };
        const deadSfuStream = new FakeMediaStream([stale]) as unknown as MediaStream;
        const previous = await aConnectedClient();
        FakePeerConnection.lastInstance().fireTrack(aTrack('video'), [deadSfuStream]);
        previous.close();

        await aConnectedClient();
        const live: FakeTrack = { ...aTrack('audio'), id: 'audio-of-the-reconnected-session' };
        FakePeerConnection.lastInstance().fireTrack(live);

        expect(videoEl.srcObject, 'the dead SFU stream must not become the reconnected session stream').not.toBe(
          deadSfuStream,
        );
        expect(attachedTracks(videoEl), 'the stopped picture must not sit beside the live track').toEqual([live]);
      });
    });
  });
});
