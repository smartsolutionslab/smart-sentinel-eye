import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { Provider } from 'react-redux';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest';
import { store } from '../../app/store.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

// Set per test so the error banner can be exercised; the mutation hook is a
// module-level mock and cannot take arguments.
let createError: unknown = undefined;

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: createError, reset: vi.fn() }],
  };
});

/**
 * Spec 147 T004 — the frame-capture wiring this dialog gains. Mocked here
 * (rather than left real) so the pre-existing tests above are unaffected: once
 * `OverlayEditorDialog` builds a `getToken` and always passes it down (T010),
 * `OverlayEditor` calls this hook on every render of this file, including
 * theirs — so the default below has to be safe for a dialog that never
 * mentions a camera.
 */
const useListAllCameraChoicesQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: (...args: unknown[]) => useListAllCameraChoicesQueryMock(...args),
  };
});

const useGetStreamQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
  };
});

beforeEach(() => {
  useListAllCameraChoicesQueryMock.mockReturnValue({
    data: { items: [], count: 0, complete: true },
    isLoading: false,
    isFetching: false,
    isError: false,
  });
  useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });
});

const { OverlayEditorDialog } = await import('./OverlayEditorDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <OverlayEditorDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('OverlayEditorDialog', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    createError = undefined;
  });

  it('Renders the name input and the embedded WYSIWYG editor controls', () => {
    renderDialog();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByTestId('overlay-editor-text')).toBeInTheDocument();
    expect(screen.getByTestId('overlay-editor-font-size')).toBeInTheDocument();
  });

  it('Submits the form with the default Label and the typed name', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-1 Title');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledTimes(1);
    const payload = (
      createDraftMock.mock.calls[0] as unknown as ReadonlyArray<{
        name: string;
        label: { text: string; fontSizePx: number };
      }>
    )[0]!;
    expect(payload.name).toBe('Line-1 Title');
    expect(payload.label.text).toBe('Overlay text');
    expect(payload.label.fontSizePx).toBe(32);
  });

  it('Surfaces a validation error when the name is blank', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole('button', { name: /save as draft/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(createDraftMock).not.toHaveBeenCalled();
  });
});

/**
 * Spec 012 T050. This dialog only creates, so its 409 is OVERLAY_NAME_TAKEN —
 * never the stale-version conflict LayoutEditorDialog handles. Keying the copy
 * on the status alone would hand the operator "reload to see their version",
 * which is useless advice for a name collision.
 */
describe('Conflict copy (spec 012 T050)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    createError = undefined;
  });

  it('Names the collision instead of telling the operator to try again', async () => {
    createError = { status: 409, data: { title: 'OVERLAY_NAME_TAKEN' } };
    renderDialog();

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('already taken');
    expect(alert.textContent).not.toContain('Try again');
  });

  it("Prefers the server's own detail when it carries one", async () => {
    createError = {
      status: 409,
      data: { title: 'OVERLAY_NAME_TAKEN', detail: "An overlay named 'Line-1 Title' already exists." },
    };
    renderDialog();

    expect((await screen.findByRole('alert')).textContent).toContain("named 'Line-1 Title'");
  });

  it('Keeps retry wording for a failure that is not a name collision', async () => {
    createError = { status: 500, data: {} };
    renderDialog();

    expect((await screen.findByRole('alert')).textContent).toContain('Try again');
  });
});

/**
 * Spec 147 T004 — new behaviour, RED. `OverlayEditorDialog` does not offer a
 * camera or a capture today, so every test below fails on a missing control
 * rather than on an import or a compile error (ADR-0139).
 *
 * <p>
 * The double is copied — not shared — from `CameraViewerMedia.test.tsx:64-115`,
 * the same double `FrameCapture.test.tsx` uses at the `OverlayEditor` level.
 * This file exercises it once more, at the dialog, because two things are
 * dialog-specific and not `OverlayEditor`'s to prove: that <b>closing the
 * dialog</b> — not merely unmounting `OverlayEditor` directly — is what tears a
 * session down (FR-013, the real `Dialog`/Radix unmount path), and that the
 * <b>submitted payload</b> never carries anything a capture produced (the
 * load-bearing constraint of the whole spec — ADR-0115, ADR-0112 §2, spec 004
 * FR-015).
 * </p>
 */
describe('Frame capture (spec 147)', () => {
  class FakePeerConnection {
    static instances: FakePeerConnection[] = [];
    static lastInstance(): FakePeerConnection {
      return FakePeerConnection.instances[FakePeerConnection.instances.length - 1]!;
    }
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

  const SESSION_URL = 'http://sfu.test/cam-42/whep/session-1';
  const CAPTURED_DATA_URL = 'data:image/png;base64,STUB';

  const CAMERA = {
    cameraIdentifier: 'cam-42',
    version: 1,
    fab: 'munich',
    name: 'Line-1 Inlet',
    rtspUrl: 'rtsp://x',
    registeredAt: '2026-01-01T00:00:00Z',
    status: 'Registered',
  };

  function sdpResponse() {
    return {
      ok: true,
      status: 200,
      headers: { get: (name: string) => (name.toLowerCase() === 'location' ? SESSION_URL : null) },
      text: async () => 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n',
    };
  }

  function installCanvasStub() {
    const context = { drawImage: vi.fn() };
    vi.spyOn(window.HTMLCanvasElement.prototype, 'getContext').mockImplementation(((id: string) =>
      id === '2d' ? context : null) as typeof window.HTMLCanvasElement.prototype.getContext);
    vi.spyOn(window.HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue(CAPTURED_DATA_URL);
  }

  /** Drains the async connect chain (offer → POST → answer) inside act. */
  async function flushConnect() {
    await act(async () => {
      for (let i = 0; i < 12; i += 1) {
        await Promise.resolve();
      }
    });
  }

  function ControlledDialog() {
    const [open, setOpen] = useState(true);
    return <OverlayEditorDialog open={open} onOpenChange={setOpen} />;
  }

  function renderControlledDialog() {
    return render(
      <Provider store={store}>
        <ControlledDialog />
      </Provider>,
    );
  }

  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createDraftMock.mockClear();
    createError = undefined;
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    fetchMock = vi.fn().mockImplementation(async (_url: string, init?: { method?: string }) => {
      if (init?.method === 'DELETE') return { ok: true, status: 200 };
      return sdpResponse();
    });
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    useListAllCameraChoicesQueryMock.mockReturnValue({
      data: { items: [CAMERA], count: 1, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    });
    useGetStreamQueryMock.mockReturnValue({
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
    });
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('Keeps the form submittable after a failed capture', async () => {
    useGetStreamQueryMock.mockReturnValue({
      data: undefined,
      error: { status: 500 },
      isLoading: false,
    });
    renderDialog();

    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Line-1 Title' } });
    fireEvent.change(screen.getByRole('combobox', { name: /camera/i }), { target: { value: CAMERA.cameraIdentifier } });
    fireEvent.click(screen.getByRole('button', { name: /^capture frame$/i }));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });

    expect(screen.getByRole('alert')).toHaveTextContent(/could not be captured/i);

    fireEvent.click(screen.getByRole('button', { name: /save as draft/i }));
    await act(async () => {
      await Promise.resolve();
    });

    expect(createDraftMock).toHaveBeenCalledTimes(1);
  });

  it('Submits a byte-identical { name, label } payload whether or not a frame was captured', async () => {
    installCanvasStub();
    renderDialog();

    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Line-1 Title' } });
    fireEvent.change(screen.getByRole('combobox', { name: /camera/i }), { target: { value: CAMERA.cameraIdentifier } });
    fireEvent.click(screen.getByRole('button', { name: /^capture frame$/i }));

    await flushConnect();
    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('connected');
    });
    await flushConnect();

    expect(screen.getByRole('radio', { name: 'Captured frame' })).toBeChecked();

    fireEvent.click(screen.getByRole('button', { name: /save as draft/i }));
    await act(async () => {
      await Promise.resolve();
    });

    expect(createDraftMock).toHaveBeenCalledTimes(1);
    const payload = (
      createDraftMock.mock.calls[0] as unknown as ReadonlyArray<{ name: string; label: Record<string, unknown> }>
    )[0]!;
    expect(Object.keys(payload).sort()).toEqual(['label', 'name']);
    expect(Object.keys(payload.label).sort()).toEqual(
      ['fontSizePx', 'normalizedHeight', 'normalizedWidth', 'normalizedX', 'normalizedY', 'text'].sort(),
    );
  });

  it('Closes the session when the dialog is closed mid-capture', async () => {
    renderControlledDialog();

    fireEvent.change(screen.getByRole('combobox', { name: /camera/i }), { target: { value: CAMERA.cameraIdentifier } });
    fireEvent.click(screen.getByRole('button', { name: /^capture frame$/i }));
    await flushConnect();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(SESSION_URL, expect.objectContaining({ method: 'DELETE' }));
  });
});
