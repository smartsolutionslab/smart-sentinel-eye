// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

/**
 * Spec 228 US2 (item 3). CameraViewer's accessibility surface: a live-region
 * announcement of stream-state changes (FR-004/FR-005/FR-006, the #2346
 * lesson — always mounted, never inserted with its content) and an
 * accessible name on the `<video>` (FR-007).
 *
 * <p>
 * <b>Mirrors CameraViewer.test.tsx's harness</b> — same FakePeerConnection,
 * same `useGetStreamQuery` mock — because these scenarios ride the identical
 * session state machine; only the assertions differ (what a screen reader
 * hears, not what a sighted operator sees).
 * </p>
 */

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
let streamQueryError: unknown;

function setHealth(state: string, error: string | null = null) {
  streamHealth = { state, whepUrl: 'http://sfu.test/cam-42/whep', error };
  streamQueryError = undefined;
}

/** Simulates the read for this camera failing outright — no stream, ever. */
function setStreamReadError(error: unknown) {
  streamHealth = undefined;
  streamQueryError = error;
}

function viewer(cameraName?: string) {
  return (
    <CameraViewer
      cameraIdentifier="cam-42"
      getToken={async () => 'token'}
      // FR-007 (spec 228 item 3): phase 4b added `cameraName` to
      // `CameraViewerProps`, so this is a plain prop pass-through now — no
      // `@ts-expect-error` needed (one was here at 4a-red, when the prop did
      // not exist yet and `tsc --noEmit` would otherwise have blocked on it).
      cameraName={cameraName}
    />
  );
}

function renderViewer(cameraName?: string) {
  return render(viewer(cameraName));
}

/**
 * Drains the async connect chain (offer → POST → answer) inside act.
 *
 * N microtask rounds bound an N-deep microtask chain — no wall-clock
 * dependence, so this is a bound and not an assumption (ADR-0150).
 */
async function flushMicrotasks() {
  await act(async () => {
    for (let i = 0; i < 12; i += 1) {
      await Promise.resolve();
    }
  });
}

async function goLive() {
  await flushMicrotasks();
  act(() => {
    FakePeerConnection.lastInstance().setConnectionState('connected');
  });
}

/** The always-mounted status region (FR-004), asserted to actually carry the role. */
function statusRegion(): HTMLElement {
  const region = screen.getByTestId('camera-viewer-status');
  expect(region).toHaveAttribute('role', 'status');
  return region;
}

function videoElement(): HTMLVideoElement | null {
  return document.querySelector('video');
}

describe('CameraViewer accessibility — announcements and the video name', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockResolvedValue(sdpResponse());
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    streamHealth = undefined;
    streamQueryError = undefined;
    useGetStreamQueryMock.mockImplementation(() => ({
      data: streamHealth,
      currentData: streamHealth,
      isLoading: false,
      error: streamQueryError,
    }));
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  /**
   * Scenario "The region pre-exists its message" (spec US2). Never inserted
   * with its content — the #2346 lesson — so it must already be in the DOM,
   * empty, while live.
   */
  it('Mounts a status region that pre-exists its message, empty while live', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    expect(statusRegion().textContent).toBe('');
  });

  /** Scenario "A reconnect is announced". */
  it('Announces a reconnect through the status region', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('failed');
    });

    expect(statusRegion().textContent).toBe('Reconnecting…');
  });

  /** Scenario "Offline and error are announced with the painted wording" (first half). */
  it('Announces the stream going offline through the status region', async () => {
    setHealth('Healthy');
    const view = renderViewer();
    await goLive();

    setHealth('Offline', 'Source powered down.');
    view.rerender(viewer());

    expect(statusRegion().textContent).toBe('Stream is offline');
  });

  /** Scenario "Offline and error are announced with the painted wording" (second half). */
  it('Announces a failed read through the status region', async () => {
    setStreamReadError('boom');
    renderViewer();
    await flushMicrotasks();

    expect(statusRegion().textContent).toBe('Viewer error');
  });

  /** Scenario "Recovery clears the region". */
  it('Clears the status region on recovery to live', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('failed');
    });
    expect(statusRegion().textContent).toBe('Reconnecting…');

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });

    expect(statusRegion().textContent).toBe('');
  });

  /** Scenario "The video is named" (first half). */
  it('Names the video with the given camera name', async () => {
    setHealth('Healthy');
    renderViewer('Line-1 Inlet');
    await flushMicrotasks();

    expect(videoElement()?.getAttribute('aria-label')).toBe('Live video: Line-1 Inlet');
  });

  /** Scenario "The video is named" (second half). */
  it('Names the video generically when no camera name is given', async () => {
    setHealth('Healthy');
    renderViewer();
    await flushMicrotasks();

    expect(videoElement()?.getAttribute('aria-label')).toBe('Live camera video');
  });

  /**
   * Scenario "The painted overlay is not read twice" — FR-006. The visible
   * overlay must carry `aria-hidden="true"` once the status region also
   * paints the same text, so the message reaches assistive tech from one
   * place only, while staying visible on screen for a sighted operator.
   */
  it('Hides the visible overlay from the accessibility tree so the message is not read twice', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('failed');
    });

    const region = statusRegion();
    const visibleMatches = screen.getAllByText('Reconnecting…').filter((element) => element !== region);
    expect(visibleMatches).toHaveLength(1);
    const [visible] = visibleMatches;
    expect(visible).toBeVisible();
    expect(visible!.closest('[aria-hidden="true"]')).not.toBeNull();
  });
});
