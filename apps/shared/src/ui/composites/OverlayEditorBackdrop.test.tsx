// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

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

const useListAllCameraChoicesQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: (...args: unknown[]) => useListAllCameraChoicesQueryMock(...args),
  };
});

const { OverlayEditor } = await import('./OverlayEditor.js');

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

/** Drains the async connect chain (offer → POST → answer) inside act. */
async function flushConnect() {
  await act(async () => {
    for (let i = 0; i < 12; i += 1) {
      await Promise.resolve();
    }
  });
}

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
      isLoading: false,
      error: undefined,
    }));
    useListAllCameraChoicesQueryMock.mockReturnValue({
      data: { items: [CAMERA], count: 1, complete: true },
      isLoading: false,
      isFetching: false,
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
    expect(canvas.style.backgroundColor).toBe('rgb(255, 255, 255)');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('Paints black and emits no onChange when the Black field backdrop is selected (FR-003/FR-005)', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    fireEvent.click(screen.getByRole('radio', { name: 'Black field' }));

    const canvas = screen.getByTestId('overlay-editor-canvas');
    expect(canvas.style.backgroundColor).toBe('rgb(0, 0, 0)');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('Restores the checkerboard exactly after switching away and back (FR-002)', () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

    fireEvent.click(screen.getByRole('radio', { name: 'White field' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Checkerboard' }));

    const canvas = screen.getByTestId('overlay-editor-canvas');
    expect(canvas.style.background).toBe(
      'repeating-linear-gradient(45deg, rgb(31, 41, 55), rgb(31, 41, 55) 12px, rgb(17, 24, 39) 12px, rgb(17, 24, 39) 24px)',
    );
  });

  it('Disables the Captured frame backdrop until a frame has been captured (FR-004)', () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);
    expect(isDisabled(screen.getByRole('radio', { name: 'Captured frame' }))).toBe(true);
  });

  it('Shows the captured still, letterboxed over black, once a frame is captured (FR-004/FR-019)', async () => {
    installCanvasStub();
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} getToken={async () => 'token'} />);

    fireEvent.change(screen.getByRole('combobox', { name: /camera/i }), {
      target: { value: CAMERA.cameraIdentifier },
    });
    fireEvent.click(screen.getByRole('button', { name: /capture frame/i }));

    await flushConnect();
    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });
    await flushConnect();

    const capturedRadio = screen.getByRole('radio', { name: 'Captured frame' });
    expect(isDisabled(capturedRadio)).toBe(false);
    expect(isChecked(capturedRadio)).toBe(true);

    const canvas = screen.getByTestId('overlay-editor-canvas');
    expect(canvas.style.backgroundImage).toBe(`url("${CAPTURED_DATA_URL}")`);
    expect(canvas.style.backgroundSize).toBe('contain');
    expect(canvas.style.backgroundPosition).toBe('center center');
    expect(canvas.style.backgroundRepeat).toBe('no-repeat');
    expect(canvas.style.backgroundColor).toBe('rgb(0, 0, 0)');
  });
});
