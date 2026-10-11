// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { CameraChoicesResult } from './BackdropControls.js';

/**
 * Spec 147 T002 — new behaviour, RED. `OverlayEditor` gains a four-way backdrop
 * (`checkerboard` | `white` | `black` | `captured`) so an operator can judge a
 * translucent label against something other than a checkerboard (FR-001–FR-005,
 * FR-019). None of this exists yet; every test here is expected to fail on a
 * missing control, not on an import or a compile error (ADR-0139).
 *
 * <p>
 * <b>The one test that needs a real capture</b> ("shows the captured still…")
 * reuses the `FakePeerConnection` + duck-typed SDP `fetch` double from
 * `CameraViewerMedia.test.tsx:64-115` — copied, not shared, for the same reason
 * that file gives: it is itself a characterisation. `jsdom` has no
 * `getVideoPlaybackQuality`, so a session promotes to `live` on transport
 * `connected` alone (spec 094 FR-005) — the same fallback every WHEP consumer in
 * this repo relies on under test.
 * </p>
 *
 * <p>
 * <b>The canvas has no `getContext('2d')` in jsdom either</b> — it returns
 * `null` without the optional `canvas` package, which this repo does not
 * install (it would test the fake, not the feature). `HTMLCanvasElement`'s
 * `getContext`/`toDataURL` are stubbed at the prototype instead, the same shape
 * spec 094's `installFrameCounter` already uses for the missing
 * `getVideoPlaybackQuality` — supplying the platform primitive the code needs
 * to run, not injecting a seam into the component under test.
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

function sdpResponse(location: string | null = 'http://sfu.test/cam-42/whep/session-1') {
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
  return context;
}

const useGetStreamQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { OverlayEditor } = await import('./OverlayEditor.js');
const { FrameGrabber } = await import('./FrameGrabber.js');
const { useGetStreamQuery } = await import('@smart-sentinel-eye/shared/api/streams.api');

/**
 * ADR-0169: `OverlayEditor` no longer calls `useGetStreamQuery` itself — the
 * caller does, and passes `renderFrameGrabber`. This mirrors
 * `OverlayDraftFormFrameGrabber` (the real caller's wrapper), reading the
 * same mocked hook this file already set up.
 */
function TestFrameGrabber({
  cameraIdentifier,
  onCaptured,
  onFailed,
}: {
  cameraIdentifier: string;
  onCaptured: (dataUrl: string) => void;
  onFailed: () => void;
}) {
  const { currentData: stream } = useGetStreamQuery(cameraIdentifier);
  return (
    <FrameGrabber
      cameraIdentifier={cameraIdentifier}
      stream={stream}
      getToken={async () => 'token'}
      onCaptured={onCaptured}
      onFailed={onFailed}
    />
  );
}

const CAMERA = {
  cameraIdentifier: 'cam-42',
  version: 1,
  fab: 'munich',
  name: 'Line-1 Inlet',
  rtspUrl: 'rtsp://x',
  registeredAt: '2026-01-01T00:00:00Z',
  status: 'Registered',
};

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
// the element rather than via `toBeChecked()`/`toBeDisabled()`.
function isChecked(el: HTMLElement): boolean {
  return (el as HTMLInputElement).checked;
}

function isDisabled(el: HTMLElement): boolean {
  return (el as HTMLInputElement | HTMLButtonElement).disabled;
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

// A fixture factory, not a hook mock — `BackdropControls` no longer calls
// `useListAllCameraChoicesQuery` itself (ADR-0169); the caller reads it and
// passes the result down as `cameras`, so this `beforeEach`-set value is
// just read directly where a test needs it.
const cameraChoicesFixture = vi.fn();

describe('OverlayEditor backdrop selection (spec 147 T002)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockResolvedValue(sdpResponse());
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    useGetStreamQueryMock.mockImplementation(() => ({
      data: {
        cameraIdentifier: CAMERA.cameraIdentifier,
        state: 'Healthy',
        whepUrl: 'http://sfu.test/cam-42/whep',
        transcodeMode: 'Passthrough',
        lastSuccessAt: null,
        error: null,
      },
      currentData: {
        cameraIdentifier: CAMERA.cameraIdentifier,
        state: 'Healthy',
        whepUrl: 'http://sfu.test/cam-42/whep',
        transcodeMode: 'Passthrough',
        lastSuccessAt: null,
        error: null,
      },
      isLoading: false,
      error: undefined,
    }));
    cameraChoicesFixture.mockReturnValue({
      data: { items: [CAMERA], count: 1, complete: true },
      isLoading: false,
      isError: false,
    });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('Selects the checkerboard backdrop by default (FR-001)', () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);
    expect(isChecked(screen.getByRole('radio', { name: 'Checkerboard' }))).toBe(true);
  });

  it('Paints white and emits no onChange when the White field backdrop is selected (FR-003/FR-005)', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    fireEvent.click(screen.getByRole('radio', { name: 'White field' }));

    const canvas = screen.getByTestId('overlay-editor-canvas');
    // Spec 293 §6: the White field now cites --color-bg-video-inverse, a
    // content role pinned to var(--white) in every theme. jsdom cannot run
    // the cascade, so the closest this test gets is the citation, not the
    // resolved colour; e2e/overlays.spec.ts's White-field test (revised in
    // this issue's phase-6 round, S1) is the real-browser proof that it
    // *resolves* to white — but only in the default theme, since nothing
    // there switches `data-theme`. No test proves the light or
    // high-contrast theme resolves it to white too.
    expect(canvas.style.backgroundColor).toBe('var(--color-bg-video-inverse)');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('Paints black and emits no onChange when the Black field backdrop is selected (FR-003/FR-005)', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    fireEvent.click(screen.getByRole('radio', { name: 'Black field' }));

    const canvas = screen.getByTestId('overlay-editor-canvas');
    // Spec 293 §6: --color-bg-video — the wall's own letterbox role
    // (CameraViewer.tsx), pinned to var(--black) in every theme (unchanged
    // by this spec; it was already the carve-out-free precedent).
    expect(canvas.style.backgroundColor).toBe('var(--color-bg-video)');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('Restores the checkerboard exactly after switching away and back (FR-002)', () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

    fireEvent.click(screen.getByRole('radio', { name: 'White field' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Checkerboard' }));

    const canvas = screen.getByTestId('overlay-editor-canvas');
    // Spec 293 §6: same token references OverlayEditorCharacterisation.test.tsx's
    // GRADIENT_RENDERED pins, not duplicated as an import — this test's own
    // point is that switching away and back restores the exact string.
    expect(canvas.style.background).toBe(
      'repeating-linear-gradient(45deg, var(--color-border-subtle), var(--color-border-subtle) 12px, var(--color-bg-elevated) 12px, var(--color-bg-elevated) 24px)',
    );
  });

  /**
   * Phase-6/review should-fix: `getToken`, `cameras` and `renderFrameGrabber`
   * are three independently optional props on `OverlayEditor`, but FR-017's
   * degradation is all-or-nothing — "absent ⇒ not offered at all". Before
   * the fix, a caller that wired `getToken`/`cameras` but not
   * `renderFrameGrabber` still got a working-looking camera picker and
   * "Capture frame" button that would silently fail after the 10s
   * `CAPTURE_TIMEOUT_MS` once pressed, because nothing ever mounted the
   * actual grabber. This proves the gate: with `renderFrameGrabber` absent,
   * the whole camera-capture section — picker and button alike — is not
   * offered at all, same as when `getToken`/`cameras` themselves are absent.
   */
  it('Offers no camera picker or Capture button when renderFrameGrabber is absent, even with getToken and cameras set', () => {
    render(
      <OverlayEditor
        value={buildLabel()}
        onChange={vi.fn()}
        getToken={async () => 'token'}
        cameras={cameraChoicesFixture() as CameraChoicesResult}
      />,
    );

    expect(screen.queryByRole('combobox', { name: /camera/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /capture frame/i })).toBeNull();
  });

  it('Disables the Captured frame backdrop until a frame has been captured (FR-004)', () => {
    render(
      <OverlayEditor
        value={buildLabel()}
        onChange={vi.fn()}
        getToken={async () => 'token'}
        cameras={cameraChoicesFixture() as CameraChoicesResult}
        renderFrameGrabber={(p) => <TestFrameGrabber {...p} />}
      />,
    );
    expect(isDisabled(screen.getByRole('radio', { name: 'Captured frame' }))).toBe(true);
  });

  it('Shows the captured still, letterboxed over black, once a frame is captured (FR-004/FR-019)', async () => {
    installCanvasStub();
    render(
      <OverlayEditor
        value={buildLabel()}
        onChange={vi.fn()}
        getToken={async () => 'token'}
        cameras={cameraChoicesFixture() as CameraChoicesResult}
        renderFrameGrabber={(p) => <TestFrameGrabber {...p} />}
      />,
    );

    fireEvent.change(screen.getByRole('combobox', { name: /camera/i }), {
      target: { value: CAMERA.cameraIdentifier },
    });
    fireEvent.click(screen.getByRole('button', { name: /capture frame/i }));

    await flushMicrotasks();
    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });
    await flushMicrotasks();

    const capturedRadio = screen.getByRole('radio', { name: 'Captured frame' });
    expect(isDisabled(capturedRadio)).toBe(false);
    expect(isChecked(capturedRadio)).toBe(true);

    const canvas = screen.getByTestId('overlay-editor-canvas');
    expect(canvas.style.backgroundImage).toBe(`url("${CAPTURED_DATA_URL}")`);
    expect(canvas.style.backgroundSize).toBe('contain');
    expect(canvas.style.backgroundPosition).toBe('center center');
    expect(canvas.style.backgroundRepeat).toBe('no-repeat');
    // Spec 293 §6: the letterbox is --color-bg-video, same role as the
    // Black field above.
    expect(canvas.style.backgroundColor).toBe('var(--color-bg-video)');
  });
});
