// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Issue #2846 (ADR-0169) safety net, at this call site. `CameraViewer`
 * stopped calling `useGetStreamQuery` itself; `CameraDetailViewer` is the
 * seam that now makes that real call and hands the result down as props.
 * This proves the whole chain — hook result in, the right `whepUrl` reaching
 * the WHEP session, a failed read painting "Viewer error" — survives that
 * split, the same way `CameraTile.test.tsx` does for the kiosk wrapper.
 */

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraDetailViewer } = await import('./CameraDetailViewer.js');

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
function sdpResponse(location: string | null) {
  return {
    ok: true,
    status: 200,
    headers: { get: (name: string) => (name.toLowerCase() === 'location' ? location : null) },
    text: async () => 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n',
  };
}

async function flushMicrotasks() {
  await act(async () => {
    for (let i = 0; i < 12; i += 1) {
      await Promise.resolve();
    }
  });
}

describe('CameraDetailViewer — the hook/composite seam', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockResolvedValue(sdpResponse('http://sfu.test/cam-42/whep/session-1'));
    globalThis.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("Opens the WHEP session against the hook's own whepUrl", async () => {
    useGetStreamQueryMock.mockReturnValue({
      data: { state: 'Healthy', whepUrl: 'http://sfu.test/cam-42/whep', error: null },
      currentData: { state: 'Healthy', whepUrl: 'http://sfu.test/cam-42/whep', error: null },
      isLoading: false,
      error: undefined,
    });

    render(<CameraDetailViewer cameraIdentifier="cam-42" getToken={async () => 'token'} />);
    await flushMicrotasks();

    const posts = fetchMock.mock.calls.filter(([url]) => String(url).startsWith('http://sfu.test/cam-42/whep'));
    expect(posts).toHaveLength(1);
  });

  it('Reads Viewer error when the hook itself failed to read the stream', async () => {
    useGetStreamQueryMock.mockReturnValue({
      data: undefined,
      currentData: undefined,
      isLoading: false,
      error: new Error('socket closed'),
    });

    render(<CameraDetailViewer cameraIdentifier="cam-42" getToken={async () => 'token'} />);
    await flushMicrotasks();

    expect(screen.getByText('Viewer error')).toBeVisible();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
