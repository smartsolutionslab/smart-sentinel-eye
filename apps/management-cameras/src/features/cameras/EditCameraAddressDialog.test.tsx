import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

const changeAddress = vi.hoisted(() => vi.fn());
const mutationState = vi.hoisted(() => ({
  current: { isLoading: false, error: undefined as unknown, reset: vi.fn() },
}));

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useChangeCameraAddressMutation: () => [changeAddress, mutationState.current],
  };
});

// gateway.ts resolves the API origin at module load; stub it before the dynamic imports below, because
// fetchBaseQuery needs an absolute URL (see CameraDetailRevocation.test.tsx).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

const { EditCameraAddressDialog } = await import('./EditCameraAddressDialog.js');
const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { streamsApi } = await import('@smart-sentinel-eye/shared/api/streams.api');
// Spec 316 T009: see CamerasPage.test.tsx's own comment.
const store = createApiStore([camerasApi, streamsApi]);

function renderDialog() {
  return render(
    <Provider store={store}>
      <EditCameraAddressDialog
        open
        onOpenChange={vi.fn()}
        cameraIdentifier="11111111-1111-1111-1111-111111111111"
        version={7}
        currentUrl="rtsp://10.0.5.12/h264"
      />
    </Provider>,
  );
}

/** A refusal shaped as RTK Query delivers it. */
const refusal = (status: number, title: string, detail: string) => ({ status, data: { title, detail } });

const CAMERA = '11111111-1111-1111-1111-111111111111';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

/** Spec 438: the address read the dialog makes on open (`GET /cameras/{camera}/address`). */
const isAddressRead = (request: Request) =>
  request.method === 'GET' && new URL(request.url).pathname.endsWith(`/cameras/${CAMERA}/address`);

function stubNetwork(answer: (request: Request) => Promise<Response>) {
  const fetchMock = vi.fn((request: Request) => answer(request));
  vi.stubGlobal('fetch', fetchMock);
  return {
    addressReads: () => fetchMock.mock.calls.filter(([request]) => isAddressRead(request as Request)),
  };
}

describe('EditCameraAddressDialog', () => {
  beforeEach(() => {
    changeAddress.mockReset();
    changeAddress.mockResolvedValue({ data: undefined });
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
    // Spec 438 setup: the dialog fetches its own address, so the tests below that
    // predate it are served the address they already pass as props.
    stubNetwork(() => Promise.resolve(jsonResponse({ rtspUrl: 'rtsp://10.0.5.12/h264', version: 7 })));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Sends the corrected address with the version the operator was shown', async () => {
    const user = userEvent.setup();
    renderDialog();

    const field = screen.getByLabelText(/rtsp url/i);
    await user.clear(field);
    await user.type(field, 'rtsp://10.0.5.44/h264');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    // The version is the whole point: without it the server answers 428 rather
    // than silently accepting a blind write (ADR-0113).
    expect(changeAddress).toHaveBeenCalledWith({
      cameraIdentifier: '11111111-1111-1111-1111-111111111111',
      rtspUrl: 'rtsp://10.0.5.44/h264',
      version: 7,
    });
  });

  /**
   * T029 — the words, not the fact of an error.
   *
   * A test asserting "a message appeared" stays green while the operator is
   * told to try again, and trying again replays their change over the other
   * writer's. That is the lost update the version mechanism exists to prevent,
   * so the forbidden phrase is asserted as forbidden.
   *
   * The server no longer phrases it that way — spec 031 changed the detail to
   * "Re-read it before reapplying your change." — but the fixture still sends
   * the old wording deliberately. The dialog must supply its own words for a
   * refusal it recognises rather than passing the server's through, and a
   * fixture that agrees with the server cannot tell the two apart.
   */
  it('Tells the operator to reload on a stale version, and never to try again', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(412, 'CAMERA_VERSION_STALE', "Camera '1111' is at version 9, not 7. Re-read it and try again."),
      reset: vi.fn(),
    };

    renderDialog();

    const banner = screen.getByRole('alert');
    expect(banner).toHaveTextContent(/reload/i);
    expect(banner).not.toHaveTextContent(/try again/i);
  });

  /**
   * T030 — the mirror image. `CAMERA_RETIRED` is a **409**, so it matches
   * `isConflict` and inherits the lost-update wording unless distinguished:
   * the operator would be told someone else changed a camera nobody changed,
   * and to reload, which will not help.
   */
  it('Tells the operator a retired camera is retired, not that someone else changed it', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(409, 'CAMERA_RETIRED', "Camera '1111' is retired; its address cannot be changed."),
      reset: vi.fn(),
    };

    renderDialog();

    const banner = screen.getByRole('alert');
    expect(banner).toHaveTextContent(/retired/i);
    expect(banner).not.toHaveTextContent(/someone else/i);
    expect(banner).not.toHaveTextContent(/reload/i);
  });

  /**
   * T032. Caught before it is sent (FR-009), using the same rule registration
   * uses — so the client and the server cannot disagree about what a usable
   * address is.
   */
  it('Refuses an unusable address without sending anything', async () => {
    const user = userEvent.setup();
    renderDialog();

    const field = screen.getByLabelText(/rtsp url/i);
    await user.clear(field);
    await user.type(field, 'http://not-rtsp.example/stream');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(changeAddress).not.toHaveBeenCalled();
    expect(screen.getByText(/must start with rtsp/i)).toBeInTheDocument();
  });

  /**
   * A refused correction must not cost the operator their typing. The camera
   * shown is the stored one (FR-004); the field is theirs.
   */
  it('Keeps what the operator typed when the correction is refused', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(412, 'CAMERA_VERSION_STALE', 'stale'),
      reset: vi.fn(),
    };

    renderDialog();

    expect(screen.getByLabelText(/rtsp url/i)).toBeInTheDocument();
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  // ---- Issue #2624 / ADR-0151: focus must survive an in-flight submit ----

  it('Announces Save as unavailable with aria-disabled, not native disabled, while in flight', () => {
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };

    renderDialog();

    const save = screen.getByRole('button', { name: /^(save|saving…)$/i });
    expect(save).toHaveAttribute('aria-disabled', 'true');
    expect(save).not.toHaveAttribute('disabled');
  });

  it('Refuses a form-level submit while a save is in flight', async () => {
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };

    renderDialog();
    // Dialog renders through a Radix Portal into document.body, outside
    // render()'s own container, so the form is looked up from the document.
    // The submit event is dispatched at the form itself, bypassing whatever
    // the submit button's own disabled/aria-disabled state is — this is the
    // form-level guard, not a click or an implicit-submission proof (that is
    // e2e/in-flight-focus.spec.ts). One macrotask flush lets react-hook-form's
    // (async) zodResolver validation and the mocked mutation call settle
    // before asserting, since both resolve on the microtask queue with no
    // real timer involved.
    await act(async () => {
      fireEvent.submit(document.querySelector('form')!);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(changeAddress).not.toHaveBeenCalled();
  });

  it('Submits a form-level submit once when nothing is in flight', async () => {
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };

    renderDialog();
    await act(async () => {
      fireEvent.submit(document.querySelector('form')!);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(changeAddress).toHaveBeenCalledTimes(1);
  });
});

/**
 * Spec 438 (#3016, ADR-0179). The dialog pre-fills from `GET /cameras/{camera}/address` and quotes the
 * version *that read* returned, not whatever the page was holding. The fixtures below deliberately differ
 * from the props the page passes today (`rtsp://10.0.5.12/h264`, version 7), so a dialog still reading its
 * props cannot satisfy them.
 */
describe('EditCameraAddressDialog reads the full address when it opens (spec 438)', () => {
  const FULL_ADDRESS = 'rtsp://10.0.5.12/h264?token=X';

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function renderOpenable(open: boolean) {
    // A store of its own: the address must be fetched on this open, not found in a cache a sibling test warmed.
    const ownStore = createApiStore([camerasApi, streamsApi]);
    const ui = (isOpen: boolean) => (
      <Provider store={ownStore}>
        <EditCameraAddressDialog
          open={isOpen}
          onOpenChange={vi.fn()}
          cameraIdentifier={CAMERA}
          version={7}
          currentUrl="rtsp://10.0.5.12/h264"
        />
      </Provider>
    );
    const view = render(ui(open));
    return { setOpen: (isOpen: boolean) => view.rerender(ui(isOpen)) };
  }

  const field = () => document.querySelector('#edit-camera-url');

  it('Requests the address route on open and pre-fills the field with what it returns', async () => {
    const network = stubNetwork(() => Promise.resolve(jsonResponse({ rtspUrl: FULL_ADDRESS, version: 12 })));

    renderOpenable(true);

    await waitFor(() => expect(network.addressReads()).toHaveLength(1));
    await waitFor(() => expect(field()).toHaveValue(FULL_ADDRESS));
  });

  it("Quotes the address read's version on Save, not the version the page was holding", async () => {
    stubNetwork(() => Promise.resolve(jsonResponse({ rtspUrl: FULL_ADDRESS, version: 12 })));
    const user = userEvent.setup();
    renderOpenable(true);

    await waitFor(() => expect(field()).toHaveValue(FULL_ADDRESS));
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(changeAddress).toHaveBeenCalledWith({
      cameraIdentifier: CAMERA,
      rtspUrl: FULL_ADDRESS,
      version: 12,
    });
  });

  it.each([
    ['a 403', () => Promise.resolve(jsonResponse({ title: 'FORBIDDEN', status: 403 }, 403))],
    ['a 404', () => Promise.resolve(jsonResponse({ title: 'CAMERA_NOT_FOUND', status: 404 }, 404))],
    ['a network error', () => Promise.reject(new TypeError('Failed to fetch'))],
  ])('Shows the refusal and neither a field nor Save when the read fails with %s', async (_name, answer) => {
    const network = stubNetwork(answer);

    renderOpenable(true);

    await waitFor(() => expect(network.addressReads()).toHaveLength(1));
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(field()).toBeNull();
    expect(screen.queryByRole('button', { name: /^save$/i })).toBeNull();
  });

  it('Makes no request while closed, and one once it is opened', async () => {
    const network = stubNetwork(() => Promise.resolve(jsonResponse({ rtspUrl: FULL_ADDRESS, version: 12 })));

    const dialog = renderOpenable(false);
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(network.addressReads()).toHaveLength(0);

    dialog.setOpen(true);

    await waitFor(() => expect(network.addressReads()).toHaveLength(1));
  });

  it('Reads the address again each time it is reopened', async () => {
    const network = stubNetwork(() => Promise.resolve(jsonResponse({ rtspUrl: FULL_ADDRESS, version: 12 })));
    const dialog = renderOpenable(true);
    await waitFor(() => expect(network.addressReads()).toHaveLength(1));

    dialog.setOpen(false);
    dialog.setOpen(true);

    await waitFor(() => expect(network.addressReads()).toHaveLength(2));
  });
});
