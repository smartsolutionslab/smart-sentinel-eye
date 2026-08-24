import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';

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

const { EditCameraAddressDialog } = await import('./EditCameraAddressDialog.js');
const { store } = await import('../../app/store.js');

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

describe('EditCameraAddressDialog', () => {
  beforeEach(() => {
    changeAddress.mockReset();
    changeAddress.mockResolvedValue({ data: undefined });
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
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
   * It fails by default twice over: `isStaleConflict` was 409-only and this
   * refusal is a 412, and the server's own detail ends "Re-read it and try
   * again."
   */
  it('Tells the operator to reload on a stale version, and never to try again', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(
        412,
        'CAMERA_VERSION_MISMATCH',
        "Camera '1111' is at version 9, not 7. Re-read it and try again.",
      ),
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
      error: refusal(412, 'CAMERA_VERSION_MISMATCH', 'stale'),
      reset: vi.fn(),
    };

    renderDialog();

    expect(screen.getByLabelText(/rtsp url/i)).toBeInTheDocument();
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});
