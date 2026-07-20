// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

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

/** Minimal duck-typed WHEP answer; jsdom has no Response constructor. */
function sdpResponse(location: string | null = 'http://sfu.test/cam-42/whep/session-1') {
  return {
    ok: true,
    status: 200,
    headers: { get: (name: string) => (name.toLowerCase() === 'location' ? location : null) },
    text: async () => 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n',
  };
}

let streamHealth: { state: string; whepUrl: string; error: string | null } | undefined;

function setHealth(state: string, error: string | null = null) {
  streamHealth = { state, whepUrl: 'http://sfu.test/cam-42/whep', error };
}

function viewer() {
  return <CameraViewer cameraIdentifier="cam-42" getToken={async () => 'token'} />;
}

function renderViewer() {
  return render(viewer());
}

/** Drains the async connect chain (offer → POST → answer) inside act. */
async function flushConnect() {
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

async function goLive() {
  await flushConnect();
  act(() => {
    FakePeerConnection.lastInstance().setConnectionState('connected');
  });
}

describe('CameraViewer stream session state machine', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection =
      FakePeerConnection;
    fetchMock = vi.fn().mockResolvedValue(sdpResponse());
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    streamHealth = undefined;
    useGetStreamQueryMock.mockImplementation(() => ({
      data: streamHealth,
      isLoading: false,
      error: undefined,
    }));
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('Does not claim Live until the peer connection reports connected', async () => {
    setHealth('Healthy');
    renderViewer();
    await flushConnect();

    // The WHEP POST has succeeded, but media transport is not up yet.
    expect(screen.getByText('Connecting…')).toBeDefined();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });

    expect(screen.queryByText('Connecting…')).toBeNull();
    expect(screen.queryByText('Reconnecting…')).toBeNull();
  });

  it('Leaves Live and schedules an immediate retry when the peer connection fails', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('failed');
    });

    expect(screen.getByText('Reconnecting…')).toBeDefined();
    expect(FakePeerConnection.instances).toHaveLength(1);

    await advance(1000); // base delay; jitter factor pinned to 1.0 via Math.random = 0.5
    await flushConnect();

    expect(FakePeerConnection.instances).toHaveLength(2);
  });

  it('Keeps a disconnected peer connection alive through the five second grace window', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('disconnected');
    });
    await advance(4999);

    expect(screen.queryByText('Reconnecting…')).toBeNull();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });
    await advance(30000);

    expect(FakePeerConnection.instances).toHaveLength(1);
    expect(screen.queryByText('Reconnecting…')).toBeNull();
  });

  it('Retries when a disconnected peer connection does not recover within the grace window', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('disconnected');
    });
    await advance(5000);

    expect(screen.getByText('Reconnecting…')).toBeDefined();

    await advance(1000);
    await flushConnect();

    expect(FakePeerConnection.instances).toHaveLength(2);
  });

  it('Retries rejected connections with exponential backoff capped at fifteen seconds', async () => {
    fetchMock.mockRejectedValue(new Error('gateway down'));
    setHealth('Healthy');
    renderViewer();
    await flushConnect();

    expect(screen.getByText('Reconnecting…')).toBeDefined();
    expect(FakePeerConnection.instances).toHaveLength(1);

    const expectedDelays = [1000, 2000, 4000, 8000, 15000, 15000];
    for (const [index, delay] of expectedDelays.entries()) {
      await advance(delay);
      await flushConnect();
      expect(FakePeerConnection.instances).toHaveLength(index + 2);
    }
  });

  it('Applies ±20 percent jitter to retry delays', async () => {
    fetchMock.mockRejectedValue(new Error('gateway down'));
    vi.spyOn(Math, 'random').mockReturnValueOnce(0).mockReturnValueOnce(1);
    setHealth('Healthy');
    renderViewer();
    await flushConnect();

    // First retry: base 1000 ms at the jitter floor (factor 0.8).
    await advance(799);
    expect(FakePeerConnection.instances).toHaveLength(1);
    await advance(1);
    await flushConnect();
    expect(FakePeerConnection.instances).toHaveLength(2);

    // Second retry: base 2000 ms at the jitter ceiling (factor 1.2).
    await advance(2399);
    expect(FakePeerConnection.instances).toHaveLength(2);
    await advance(1);
    await flushConnect();
    expect(FakePeerConnection.instances).toHaveLength(3);
  });

  it('Suspends retries while stream health is Offline and reconnects on recovery', async () => {
    fetchMock.mockRejectedValue(new Error('gateway down'));
    setHealth('Healthy');
    const view = renderViewer();
    await flushConnect();

    expect(screen.getByText('Reconnecting…')).toBeDefined();

    setHealth('Offline', 'Source powered down.');
    view.rerender(viewer());

    expect(screen.getByText('Stream is offline')).toBeDefined();

    await advance(120000);
    expect(FakePeerConnection.instances).toHaveLength(1);

    fetchMock.mockResolvedValue(sdpResponse());
    setHealth('Healthy');
    view.rerender(viewer());
    await flushConnect();

    expect(FakePeerConnection.instances).toHaveLength(2);
  });

  it('Re-establishes a real session when stream health recovers from Degraded', async () => {
    setHealth('Healthy');
    const view = renderViewer();
    await goLive();

    setHealth('Degraded', 'Source unreachable.');
    view.rerender(viewer());

    expect(screen.getByText('Reconnecting…')).toBeDefined();
    expect(FakePeerConnection.instances).toHaveLength(1);

    setHealth('Healthy');
    view.rerender(viewer());
    await flushConnect();

    expect(FakePeerConnection.instances).toHaveLength(2);
    expect(FakePeerConnection.instances[0]!.closed).toBe(true);

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });
    expect(screen.queryByText('Reconnecting…')).toBeNull();
  });

  it('Aborts the session and releases it on unmount', async () => {
    setHealth('Healthy');
    const view = renderViewer();
    await goLive();

    view.unmount();
    await act(async () => {
      for (let i = 0; i < 6; i += 1) {
        await Promise.resolve();
      }
    });

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    const deleteCalls = fetchMock.mock.calls.filter(
      ([, init]) => (init as RequestInit | undefined)?.method === 'DELETE',
    );
    expect(deleteCalls).toHaveLength(1);
  });

  it('Logs every stream state transition with the resilience prefix', async () => {
    const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);
    setHealth('Healthy');
    renderViewer();
    await goLive();

    const transitions = info.mock.calls
      .filter(([prefix]) => prefix === '[resilience]')
      .map(([, payload]) => payload as Record<string, unknown>);
    expect(transitions).toEqual([
      expect.objectContaining({
        subsystem: 'stream',
        transition: 'idle→connecting',
        cameraIdentifier: 'cam-42',
      }),
      expect.objectContaining({
        subsystem: 'stream',
        transition: 'connecting→live',
        cameraIdentifier: 'cam-42',
      }),
    ]);
  });
});
