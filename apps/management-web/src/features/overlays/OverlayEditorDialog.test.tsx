import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { Provider } from 'react-redux';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest';
import { store } from '../../app/store.js';
import type { OverlayEditTarget } from './OverlayEditorDialog.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 1 }));

// Set per test so the error banner can be exercised; the mutation hooks are
// module-level mocks and cannot take arguments.
let createError: unknown = undefined;
let editError: unknown = undefined;
const refetchChainMock = vi.fn();

/**
 * Spec 152 FR-011. The dialog re-reads the chain's version rather than
 * inferring it, so this is the version `editDraftOverlayRevision` must carry.
 * Deliberately not `EDIT_TARGET.revisionNumber` (1) and not `revisionNumber + 1`
 * (2) — either would pass a test asserting `version + 1`, which FR-012 forbids.
 */
let chainQueryState: { data?: { overlayIdentifier: string; version: number }; isLoading: boolean; isError: boolean } = {
  data: undefined,
  isLoading: false,
  isError: false,
};

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: createError, reset: vi.fn() }],
    useEditDraftOverlayRevisionMutation: () => [editDraftMock, { isLoading: false, error: editError, reset: vi.fn() }],
    // The dialog reads the chain back to learn its current version (FR-011);
    // the page target is one write behind by construction (US1: possibly, US2:
    // certainly, since branching is itself a write) — see LayoutEditorDialog.tsx:59-65.
    useGetOverlayQuery: () => ({
      data: chainQueryState.data,
      isLoading: chainQueryState.isLoading,
      isError: chainQueryState.isError,
      refetch: refetchChainMock,
    }),
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

function renderDialog(editTarget?: OverlayEditTarget, onOpenChange: (open: boolean) => void = () => {}) {
  return render(
    <Provider store={store}>
      <OverlayEditorDialog open={true} onOpenChange={onOpenChange} editTarget={editTarget} />
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
 * Spec 152 T001-T003 — new behaviour, RED (ADR-0139). `OverlayEditorDialog`
 * accepts `editTarget` today only as an unused scaffold prop (T008-T011 wire
 * it up): the dialog renders in create mode regardless of what is passed, so
 * every assertion below fails for a real reason — a rendered Name field that
 * should be hidden, a title/description that never changed, a mutation that
 * is never called — not on a missing export or a type error.
 */
describe('OverlayEditorDialog — edit', () => {
  const EDIT_TARGET: OverlayEditTarget = {
    overlayIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    revisionNumber: 1,
    name: 'Line-1 Title',
    label: {
      text: 'Line 1',
      normalizedX: 0.1,
      normalizedY: 0.1,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 32,
    },
  };

  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
    createError = undefined;
    editError = undefined;
    // The queried chain version (7) is deliberately not EDIT_TARGET.revisionNumber
    // (1) and not revisionNumber + 1 (2) — see the FR-011/FR-012 test below.
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isLoading: false,
      isError: false,
    };
  });

  it('Seeds the label field from the target, hides the Name field, and titles the dialog for that revision', () => {
    renderDialog(EDIT_TARGET);

    expect(screen.queryByLabelText(/name/i)).not.toBeInTheDocument();
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Line 1');
    expect(screen.getByText(/^edit overlay draft$/i)).toBeInTheDocument();

    // FR-008: the description names the revision AND the overlay, because on
    // a chain with two drafts there are two revisions' worth of ambiguity
    // about which one is about to be overwritten.
    const description = screen.getByText(/draft v1/i);
    expect(description.textContent).toContain('Line-1 Title');
  });

  it(
    'Calls editDraftOverlayRevision exactly once with the queried chain version — never ' +
      'the revision number or revision number + 1 — and a label-only body',
    async () => {
      const user = userEvent.setup();
      renderDialog(EDIT_TARGET);

      const textInput = screen.getByTestId('overlay-editor-text');
      await user.clear(textInput);
      await user.type(textInput, 'Line 2');

      await user.click(screen.getByRole('button', { name: /^save draft$/i }));

      expect(editDraftMock).toHaveBeenCalledTimes(1);
      expect(createDraftMock).not.toHaveBeenCalled();
      const body = (
        editDraftMock.mock.calls[0] as unknown as ReadonlyArray<{
          overlayIdentifier: string;
          revisionNumber: number;
          version: number;
          label: Record<string, unknown>;
        }>
      )[0]!;
      expect(body.overlayIdentifier).toBe(EDIT_TARGET.overlayIdentifier);
      expect(body.revisionNumber).toBe(1);
      // The point of the test (FR-012): 7 is the queried version. 1 would mean
      // the client reused the revision number; 2 would mean it computed +1.
      expect(body.version).toBe(7);
      expect(Object.keys(body).sort()).toEqual(['label', 'overlayIdentifier', 'revisionNumber', 'version']);
      expect(body.label.text).toBe('Line 2');
    },
  );

  it('Closes the dialog once the edit is saved', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderDialog(EDIT_TARGET, onOpenChange);

    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  describe('The version has to be read before Save can be trusted (FR-013)', () => {
    it('Disables Save while the chain query has not resolved', () => {
      chainQueryState = { data: undefined, isLoading: true, isError: false };
      renderDialog(EDIT_TARGET);

      expect(screen.getByRole('button', { name: /^save draft$/i })).toBeDisabled();
    });

    it('Disables Save and offers a retry — not a silent no-op — when the chain read fails', async () => {
      const user = userEvent.setup();
      chainQueryState = { data: undefined, isLoading: false, isError: true };
      renderDialog(EDIT_TARGET);

      expect(screen.getByRole('button', { name: /^save draft$/i })).toBeDisabled();
      const alert = screen.getByRole('alert');
      expect(alert.textContent).toMatch(/could not be read/i);

      await user.click(screen.getByRole('button', { name: /retry/i }));
      expect(refetchChainMock).toHaveBeenCalledTimes(1);
    });
  });

  describe('A conflict names what to do, never "try again" (FR-014/FR-015)', () => {
    it('Shows the server detail and a Reload control for a stale version, never "try again"', async () => {
      editError = {
        status: 409,
        data: {
          title: 'OVERLAY_REVISION_STALE',
          detail: 'Overlay has changed since version 7 (now 8). Re-read it and reapply the change.',
        },
      };
      renderDialog(EDIT_TARGET);

      const alert = await screen.findByRole('alert');
      expect(alert.textContent).toContain('changed since version 7');
      expect(alert.textContent).not.toMatch(/try again/i);
      expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
    });

    it('Reloads rather than resubmitting when the operator clicks Reload', async () => {
      const user = userEvent.setup();
      editError = { status: 409, data: { title: 'OVERLAY_REVISION_STALE' } };
      renderDialog(EDIT_TARGET);

      await user.click(screen.getByRole('button', { name: /reload/i }));

      expect(refetchChainMock).toHaveBeenCalledTimes(1);
      expect(editDraftMock).not.toHaveBeenCalled();
    });

    it('Says the revision is no longer a draft and offers Reload, without the stale wording', async () => {
      editError = {
        status: 409,
        data: { title: 'OVERLAY_REVISION_NOT_DRAFT', detail: 'Revision 1 of Line-1 Title is no longer a draft.' },
      };
      renderDialog(EDIT_TARGET);

      const alert = await screen.findByRole('alert');
      expect(alert.textContent).toContain('no longer a draft');
      expect(alert.textContent).not.toMatch(/someone else changed this/i);
      expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
    });
  });

  it('Refuses an empty label before any request, and calls no mutation', async () => {
    const user = userEvent.setup();
    renderDialog(EDIT_TARGET);

    const textInput = screen.getByTestId('overlay-editor-text');
    await user.clear(textInput);
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(await screen.findByText(/text is required/i)).toBeInTheDocument();
    expect(editDraftMock).not.toHaveBeenCalled();
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

    expect(FakePeerConnection.lastInstance().closed, 'teardown must not wait on the release').toBe(true);

    // The release is fire-and-forget behind getToken() — at least one
    // microtask beyond close() returning, so the DELETE cannot have been
    // issued synchronously with the Cancel click (WhepClient.close():
    // releaseSession() awaits getToken() before it ever calls fetch).
    await flushConnect();

    expect(fetchMock).toHaveBeenCalledWith(SESSION_URL, expect.objectContaining({ method: 'DELETE' }));
  });
});
