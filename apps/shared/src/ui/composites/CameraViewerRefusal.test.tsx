// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';

/**
 * Spec 233 (issue #2355) — a WHEP authorization refusal (401/403) ends the
 * session as a terminal "Access refused" state, not a reconnect (FR-001).
 *
 * <p>
 * <b>RED cases below are new behaviour (ADR-0139).</b> Today `useWhepSession`
 * sends every `connect()` rejection — including a `WhepError` of kind
 * `'unauthorized'`/`'forbidden'` — to `scheduleRetry`, so these fail against
 * "Access refused", against the POST/peer-connection counts after 60 s, and
 * against the `whep-refused` resilience line, which does not exist yet.
 * </p>
 *
 * <p>
 * <b>Characterisation cases are green today and must stay green</b> (spec §6):
 * the transient ladder for a 403 "stream unavailable", a 500, and a rejected
 * fetch, plus the camera-swap exit, which already works via the existing
 * ladder.
 * </p>
 *
 * <p>
 * <b>The harness is copied from `CameraViewer.test.tsx`</b>, not shared — that
 * file is the ladder's own characterisation and stays byte-identical.
 * </p>
 */

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

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

/** Minimal duck-typed successful WHEP answer; jsdom has no Response constructor. */
function sdpResponse(location: string | null) {
  return {
    ok: true,
    status: 200,
    headers: { get: (name: string) => (name.toLowerCase() === 'location' ? location : null) },
    text: async () => 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n',
  };
}

/** Minimal duck-typed refused/transient WHEP answer. */
function errorResponse(status: number, body: string) {
  return { ok: false, status, text: async () => body };
}

const REFUSED_ANNOUNCEMENT =
  'Access refused. The stream server refused this viewer. This tile will not retry on its own.';

let streamHealthByCamera: Record<string, StreamHealth | undefined>;

function setHealth(cameraIdentifier: string, state: StreamHealth['state'], error: string | null = null) {
  streamHealthByCamera[cameraIdentifier] = {
    cameraIdentifier,
    state,
    whepUrl: `http://sfu.test/${cameraIdentifier}/whep`,
    transcodeMode: 'Passthrough',
    lastSuccessAt: null,
    error,
  };
}

function viewer(cameraIdentifier = 'cam-42', getToken: () => Promise<string | null> = async () => 'token') {
  return (
    <CameraViewer
      cameraIdentifier={cameraIdentifier}
      getToken={getToken}
      stream={streamHealthByCamera[cameraIdentifier]}
    />
  );
}

function renderViewer(cameraIdentifier = 'cam-42', getToken?: () => Promise<string | null>) {
  return render(viewer(cameraIdentifier, getToken));
}

/**
 * Drains the async connect chain (offer → POST → answer) inside act.
 *
 * N microtask rounds bound an N-deep microtask chain — no wall-clock
 * dependence, so this is a bound and not an assumption (ADR-0150). Not a
 * substitute for `waitFor` when the work crosses into the timer phase.
 */
async function flushMicrotasks() {
  await act(async () => {
    for (let i = 0; i < 12; i += 1) {
      await Promise.resolve();
    }
  });
}

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('CameraViewer WHEP refusal (spec 233, issue #2355)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let infoSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockResolvedValue(sdpResponse('http://sfu.test/cam-42/whep/session-1'));
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    streamHealthByCamera = {};
    // Pins the ±20% jitter factor to 1.0, as the neighbouring suites do, so
    // the ladder's delays are the base delays.
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  function resilienceLines(transition: string) {
    return infoSpy.mock.calls.filter(
      (call: unknown[]) =>
        call[0] === '[resilience]' && (call[1] as { transition?: unknown } | undefined)?.transition === transition,
    );
  }

  function postCount() {
    return fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method !== 'DELETE').length;
  }

  describe('a refusal is terminal (T001, RED)', () => {
    it('Reads Access refused on a 401 and makes no further POST after sixty seconds', async () => {
      fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
      setHealth('cam-42', 'Healthy');
      renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Access refused')).toBeVisible();
      expect(screen.getByTestId('camera-viewer-status').textContent).toBe(REFUSED_ANNOUNCEMENT);

      await advance(60_000);

      expect(postCount()).toBe(1);
      expect(FakePeerConnection.instances).toHaveLength(1);

      const refused = resilienceLines('whep-refused');
      expect(refused).toHaveLength(1);
      expect(refused[0]![1]).toMatchObject({
        subsystem: 'stream',
        cameraIdentifier: 'cam-42',
        kind: 'unauthorized',
      });
    });

    it('Reads Access refused on a 403 "forbidden" with resilience kind forbidden', async () => {
      fetchMock.mockResolvedValue(errorResponse(403, 'forbidden'));
      setHealth('cam-42', 'Healthy');
      renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Access refused')).toBeVisible();

      await advance(60_000);
      expect(postCount()).toBe(1);

      const refused = resilienceLines('whep-refused');
      expect(refused).toHaveLength(1);
      expect(refused[0]![1]).toMatchObject({ cameraIdentifier: 'cam-42', kind: 'forbidden' });
    });

    it('Ends the ladder in refusal when a retry is answered with 401, making no third POST', async () => {
      fetchMock.mockRejectedValueOnce(new Error('gateway down')).mockResolvedValue(errorResponse(401, 'unauthorized'));
      setHealth('cam-42', 'Healthy');
      renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Reconnecting…')).toBeVisible();
      expect(postCount()).toBe(1);

      await advance(1000); // base retry delay; jitter factor pinned to 1.0
      await flushMicrotasks();

      expect(screen.getByText('Access refused')).toBeVisible();
      expect(postCount()).toBe(2);

      await advance(60_000);
      expect(postCount()).toBe(2);
    });

    it('Refuses a tile with no token at all, rather than retrying', async () => {
      fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
      setHealth('cam-42', 'Healthy');
      renderViewer('cam-42', async () => null);
      await flushMicrotasks();

      expect(screen.getByText('Access refused')).toBeVisible();
      expect(postCount()).toBe(1);

      await advance(60_000);
      expect(postCount()).toBe(1);
    });

    it('Makes exactly one fresh attempt on a Degraded-to-Healthy recovery, and refuses again with no more', async () => {
      fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
      setHealth('cam-42', 'Healthy');
      const view = renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Access refused')).toBeVisible();
      expect(postCount()).toBe(1);

      setHealth('cam-42', 'Degraded', 'Source unreachable.');
      view.rerender(viewer());
      setHealth('cam-42', 'Healthy');
      view.rerender(viewer());
      await flushMicrotasks();

      expect(postCount()).toBe(2);
      expect(screen.getByText('Access refused')).toBeVisible();

      await advance(60_000);
      expect(postCount()).toBe(2);
    });

    it('Makes no further POST once a refused tile unmounts', async () => {
      fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
      setHealth('cam-42', 'Healthy');
      const view = renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Access refused')).toBeVisible();
      expect(postCount()).toBe(1);

      view.unmount();
      await advance(60_000);

      expect(postCount()).toBe(1);
    });
  });

  describe('transient failures and the camera-swap exit stay as they are (T002, characterisation)', () => {
    it('Stays on Reconnecting… for a 403 "stream unavailable" and logs no whep-refused line', async () => {
      fetchMock.mockResolvedValue(errorResponse(403, 'stream unavailable'));
      setHealth('cam-42', 'Healthy');
      renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Reconnecting…')).toBeVisible();
      expect(postCount()).toBe(1);

      await advance(1000);
      await flushMicrotasks();

      expect(postCount()).toBe(2);
      expect(resilienceLines('whep-refused')).toHaveLength(0);
    });

    it('Stays on Reconnecting… for a 500 and logs no whep-refused line', async () => {
      fetchMock.mockResolvedValue(errorResponse(500, 'internal error'));
      setHealth('cam-42', 'Healthy');
      renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Reconnecting…')).toBeVisible();
      expect(postCount()).toBe(1);

      await advance(1000);
      await flushMicrotasks();

      expect(postCount()).toBe(2);
      expect(resilienceLines('whep-refused')).toHaveLength(0);
    });

    it('Stays on Reconnecting… when the WHEP fetch rejects and logs no whep-refused line', async () => {
      fetchMock.mockRejectedValue(new Error('network down'));
      setHealth('cam-42', 'Healthy');
      renderViewer();
      await flushMicrotasks();

      expect(screen.getByText('Reconnecting…')).toBeVisible();
      expect(postCount()).toBe(1);

      await advance(1000);
      await flushMicrotasks();

      expect(postCount()).toBe(2);
      expect(resilienceLines('whep-refused')).toHaveLength(0);
    });

    // Not preconditioned on the "Access refused" text itself — that label is
    // the RED half above. What this pins is the swap mechanism: whatever
    // terminal-ish state the old camera's session left behind (today
    // 'reconnecting' off a 401; after T001's fix, the terminal 'error'),
    // changing `cameraIdentifier` already tears the old session down and
    // opens exactly one fresh one for the new camera, via the existing
    // camera-swap effect (plan D4) — unaffected by this spec.
    it('Makes exactly one POST for the new camera on a camera swap away from a refused session', async () => {
      fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
      setHealth('cam-42', 'Healthy');
      const view = renderViewer('cam-42');
      await flushMicrotasks();

      expect(postCount()).toBe(1);

      fetchMock.mockResolvedValue(sdpResponse('http://sfu.test/cam-43/whep/session-1'));
      setHealth('cam-43', 'Healthy');
      view.rerender(viewer('cam-43'));
      await flushMicrotasks();

      const cam43Posts = fetchMock.mock.calls.filter(
        ([url, init]) => String(url).includes('cam-43') && (init as RequestInit | undefined)?.method !== 'DELETE',
      );
      expect(cam43Posts).toHaveLength(1);
      expect(screen.queryByText('Access refused')).toBeNull();
    });
  });
});

/**
 * Spec 439 (issue #2781) T001 — RED. The viewer tells its owner when it is
 * terminally refused, and which kind, so a wall can recognise every tile
 * refused at once. Today the prop does not exist and nothing is reported.
 */
describe('CameraViewer reports a terminal refusal to its owner (spec 439 T001, RED)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockResolvedValue(sdpResponse('http://sfu.test/cam-42/whep/session-1'));
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    streamHealthByCamera = {};
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  function renderWith(onRefused: (kind: 'unauthorized' | 'forbidden') => void) {
    setHealth('cam-42', 'Healthy');
    return render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={async () => 'token'}
        stream={streamHealthByCamera['cam-42']}
        onRefused={onRefused}
      />,
    );
  }

  it('Reports unauthorized once when the WHEP answer is 401', async () => {
    fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
    const onRefused = vi.fn();
    renderWith(onRefused);
    await flushMicrotasks();

    expect(onRefused).toHaveBeenCalledTimes(1);
    expect(onRefused).toHaveBeenCalledWith('unauthorized');
  });

  it('Reports forbidden once when the WHEP answer is 403 forbidden', async () => {
    fetchMock.mockResolvedValue(errorResponse(403, 'forbidden'));
    const onRefused = vi.fn();
    renderWith(onRefused);
    await flushMicrotasks();

    expect(onRefused).toHaveBeenCalledTimes(1);
    expect(onRefused).toHaveBeenCalledWith('forbidden');
  });

  it('Reports nothing for a 403 stream unavailable, which stays on the ladder', async () => {
    fetchMock.mockResolvedValue(errorResponse(403, 'stream unavailable'));
    const onRefused = vi.fn();
    renderWith(onRefused);
    await flushMicrotasks();

    expect(screen.getByText('Reconnecting…')).toBeVisible();
    expect(onRefused).not.toHaveBeenCalled();
  });

  it('Reports nothing when the WHEP fetch rejects', async () => {
    fetchMock.mockRejectedValue(new Error('network down'));
    const onRefused = vi.fn();
    renderWith(onRefused);
    await flushMicrotasks();

    expect(onRefused).not.toHaveBeenCalled();
  });
});

/**
 * Review of #2781 (items 4 and 5) — RED. The wall must hear when a tile is
 * live again (an episode ends there) and when a reported refusal ends without
 * a remount, or a live tile keeps counting as unauthorized.
 */
describe('CameraViewer tells its owner when it is live or a refusal lifts (review of #2781, RED)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockResolvedValue(sdpResponse('http://sfu.test/cam-42/whep/session-1'));
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    streamHealthByCamera = {};
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  const element = (handlers: { onLive?: () => void; onRefusalLifted?: () => void }) => (
    <CameraViewer
      cameraIdentifier="cam-42"
      getToken={async () => 'token'}
      stream={streamHealthByCamera['cam-42']}
      {...handlers}
    />
  );

  it('Reports live once the session connects', async () => {
    setHealth('cam-42', 'Healthy');
    const onLive = vi.fn();
    render(element({ onLive }));
    await flushMicrotasks();
    expect(onLive).not.toHaveBeenCalled();

    act(() => FakePeerConnection.lastInstance().setConnectionState('connected'));
    await flushMicrotasks();

    expect(onLive).toHaveBeenCalledTimes(1);
  });

  it('Lifts a reported refusal when the viewer unmounts', async () => {
    fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
    setHealth('cam-42', 'Healthy');
    const onLifted = vi.fn();
    const view = render(element({ onRefusalLifted: onLifted }));
    await flushMicrotasks();
    expect(onLifted).not.toHaveBeenCalled();

    view.unmount();

    expect(onLifted).toHaveBeenCalledTimes(1);
  });

  it('Lifts a reported refusal when the stream goes Offline without a remount', async () => {
    fetchMock.mockResolvedValue(errorResponse(401, 'unauthorized'));
    setHealth('cam-42', 'Healthy');
    const onLifted = vi.fn();
    const view = render(element({ onRefusalLifted: onLifted }));
    await flushMicrotasks();

    setHealth('cam-42', 'Offline', 'camera unplugged');
    view.rerender(element({ onRefusalLifted: onLifted }));
    await flushMicrotasks();

    expect(onLifted).toHaveBeenCalledTimes(1);
  });

  it('Lifts nothing on unmount when no refusal was reported', async () => {
    setHealth('cam-42', 'Healthy');
    const onLifted = vi.fn();
    const view = render(element({ onRefusalLifted: onLifted }));
    await flushMicrotasks();

    view.unmount();

    expect(onLifted).not.toHaveBeenCalled();
  });
});
