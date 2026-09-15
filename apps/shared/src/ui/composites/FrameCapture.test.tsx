// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Spec 147 T003 — new behaviour, RED. `OverlayEditor` gains a bounded WHEP
 * capture (FR-006–FR-016): at most one session, only while a capture is in
 * flight, released on every exit path (FR-014, "the negative the whole design
 * rests on"). None of this exists yet.
 *
 * <p>
 * <b>The double.</b> Copied — not shared — from `CameraViewerMedia.test.tsx:64-115`,
 * because that file is itself a characterisation and must not be touched. A
 * `FakePeerConnection` plus a duck-typed SDP `fetch` response, exactly as spec
 * 094 built it. The WHEP `DELETE` is asserted **on the `fetch` stub**, never on
 * a mocked `WhepClient` method — the point being that this proves a real HTTP
 * call happened, not that the code called a method it also wrote.
 * </p>
 *
 * <p>
 * <b>`live` fires on `connected` alone.</b> jsdom has no
 * `getVideoPlaybackQuality`, so `useWhepSession` promotes on transport
 * `connected` (spec 094 FR-005) — the fallback every WHEP consumer in this repo
 * relies on under test, this one included.
 * </p>
 *
 * <p>
 * <b>The canvas.</b> `getContext('2d')` is `null` in jsdom without the optional
 * `canvas` package, which is deliberately not installed (it would test the
 * fake). `HTMLCanvasElement.prototype.getContext`/`toDataURL` are stubbed
 * instead — the same shape as spec 094's `installFrameCounter` for the missing
 * `getVideoPlaybackQuality`: supplying the platform primitive, not a seam into
 * the component.
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

function sdpResponse(location: string | null) {
  return {
    ok: true,
    status: 200,
    headers: { get: (name: string) => (name.toLowerCase() === 'location' ? location : null) },
    text: async () => 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n',
  };
}

const CAPTURED_DATA_URL = 'data:image/png;base64,STUB';

function installCanvasStub() {
  const context = { drawImage: vi.fn() };
  vi.spyOn(window.HTMLCanvasElement.prototype, 'getContext').mockImplementation(((id: string) =>
    id === '2d' ? context : null) as typeof window.HTMLCanvasElement.prototype.getContext);
  vi.spyOn(window.HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue(CAPTURED_DATA_URL);
}

const useGetStreamQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const useListAllCameraChoicesQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: (...args: unknown[]) => useListAllCameraChoicesQueryMock(...args),
  };
});

const { OverlayEditor } = await import('./OverlayEditor.js');

const CAMERA_42 = {
  cameraIdentifier: 'cam-42',
  version: 1,
  fab: 'munich',
  name: 'Line-1 Inlet',
  rtspUrl: 'rtsp://x',
  registeredAt: '2026-01-01T00:00:00Z',
  status: 'Registered',
};
const CAMERA_7 = { ...CAMERA_42, cameraIdentifier: 'cam-7', name: 'Line-2 Outlet' };

const SESSION_URL_42 = 'http://sfu.test/cam-42/whep/session-1';
const SESSION_URL_7 = 'http://sfu.test/cam-7/whep/session-1';

const BASE_LABEL: OverlayLabel = {
  text: 'Line-1 Inlet',
  normalizedX: 0.1,
  normalizedY: 0.1,
  normalizedWidth: 0.3,
  normalizedHeight: 0.08,
  fontSizePx: 32,
};

function buildLabel(overrides: Partial<OverlayLabel> = {}): OverlayLabel {
  return { ...BASE_LABEL, ...overrides };
}

// `apps/shared` does not carry `@testing-library/jest-dom` (only
// `apps/management-web` does), so `.checked`/`.disabled` are read directly off
// the element rather than via `toBeChecked()`/`toBeDisabled()`/`toBeEnabled()`.
function isChecked(el: HTMLElement): boolean {
  return (el as HTMLInputElement).checked;
}

function isDisabled(el: HTMLElement): boolean {
  return (el as HTMLInputElement | HTMLButtonElement).disabled;
}

function healthyStream(cameraIdentifier: string) {
  return {
    cameraIdentifier,
    state: 'Healthy',
    whepUrl: `http://sfu.test/${cameraIdentifier}/whep`,
    transcodeMode: 'Passthrough',
    lastSuccessAt: null,
    error: null,
  };
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

function selectCamera(cameraIdentifier: string) {
  fireEvent.change(screen.getByRole('combobox', { name: /camera/i }), { target: { value: cameraIdentifier } });
}

function pressCapture() {
  fireEvent.click(screen.getByRole('button', { name: /^capture frame$/i }));
}

describe('OverlayEditor frame capture (spec 147 T003)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        return { ok: true, status: 200 };
      }
      const target = String(url).includes('cam-7') ? SESSION_URL_7 : SESSION_URL_42;
      return sdpResponse(target);
    });
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    useGetStreamQueryMock.mockImplementation((cameraIdentifier: string) => ({
      data: healthyStream(cameraIdentifier),
      currentData: healthyStream(cameraIdentifier),
      isLoading: false,
      error: undefined,
    }));
    useListAllCameraChoicesQueryMock.mockReturnValue({
      data: { items: [CAMERA_42, CAMERA_7], count: 2, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    });
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  /** FR-014, the negative the whole design rests on. */
  it('Opens no WHEP session while no capture is in flight', () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);
    expect(FakePeerConnection.instances).toHaveLength(0);

    selectCamera(CAMERA_42.cameraIdentifier);
    expect(FakePeerConnection.instances).toHaveLength(0);
  });

  /** FR-007/FR-008. */
  it('Opens exactly one WHEP session against the selected camera when Capture is pressed', async () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();
    await flushMicrotasks();

    expect(FakePeerConnection.instances).toHaveLength(1);
  });

  /** FR-009. */
  it('Captures nothing before the session reports live', async () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();
    await flushMicrotasks();

    // Transport has negotiated but never reported `connected`.
    expect(isDisabled(screen.getByRole('radio', { name: 'Captured frame' }))).toBe(true);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  /** FR-010, asserted on the `fetch` stub — not a mocked `WhepClient` method. */
  it('Closes the session and issues the WHEP DELETE once a frame is captured', async () => {
    installCanvasStub();
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();
    await flushMicrotasks();
    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });
    await flushMicrotasks();

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(SESSION_URL_42, expect.objectContaining({ method: 'DELETE' }));
  });

  /** FR-011. */
  it('Abandons a capture that produces no frame within 10 seconds', async () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();
    await flushMicrotasks();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    expect(screen.getByRole('alert')).toBeDefined();
    expect(isChecked(screen.getByRole('radio', { name: 'Checkerboard' }))).toBe(true);
    // The operator is not stuck: capture can be tried again.
    expect(isDisabled(screen.getByRole('button', { name: /^capture frame$/i }))).toBe(false);
  });

  /** FR-012: an in-flight capture can be cancelled. */
  it('Closes the session when a capture in flight is cancelled', async () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();
    await flushMicrotasks();

    fireEvent.click(screen.getByRole('button', { name: /cancel capture/i }));

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    expect(isDisabled(screen.getByRole('button', { name: /^capture frame$/i }))).toBe(false);
  });

  /** FR-012: changing the camera mid-capture closes the old session and applies nothing from it. */
  it('Closes the session against the old camera when the camera is changed mid-capture', async () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();
    await flushMicrotasks();
    const firstConnection = FakePeerConnection.lastInstance();

    selectCamera(CAMERA_7.cameraIdentifier);

    expect(firstConnection.closed).toBe(true);

    // The stale session reporting `connected` after the fact must not land a
    // frame from the camera the operator has already moved away from.
    act(() => {
      firstConnection.setConnectionState('connected');
    });
    await flushMicrotasks();

    expect(isDisabled(screen.getByRole('radio', { name: 'Captured frame' }))).toBe(true);
  });

  /** FR-013: unmounting the editor (the dialog-close path) closes an in-flight session. */
  it('Closes the session when the editor unmounts mid-capture', async () => {
    const { unmount } = render(
      <OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />,
    );

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();
    await flushMicrotasks();

    unmount();

    expect(FakePeerConnection.lastInstance().closed, 'teardown must not wait on the release').toBe(true);

    // The release is fire-and-forget behind getToken() — at least one
    // microtask beyond close() returning, so the DELETE cannot have been
    // issued synchronously with unmount() (WhepClient.close():
    // releaseSession() awaits getToken() before it ever calls fetch).
    await flushMicrotasks();

    expect(fetchMock).toHaveBeenCalledWith(SESSION_URL_42, expect.objectContaining({ method: 'DELETE' }));
  });

  /** FR-016: a stream-lookup failure degrades exactly like a timeout — same alert, nothing else changes. */
  it('Shows the could-not-capture alert when the stream lookup fails, and changes nothing else', async () => {
    useGetStreamQueryMock.mockImplementation(() => ({
      data: undefined,
      isLoading: false,
      error: { status: 500 },
    }));
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    selectCamera(CAMERA_42.cameraIdentifier);
    pressCapture();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });

    expect(screen.getByRole('alert')).toBeDefined();
    expect(isChecked(screen.getByRole('radio', { name: 'Checkerboard' }))).toBe(true);
    expect(FakePeerConnection.instances).toHaveLength(0);
  });
});
