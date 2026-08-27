import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';

const renameCamera = vi.hoisted(() => vi.fn());
const mutationState = vi.hoisted(() => ({
  current: { isLoading: false, error: undefined as unknown, reset: vi.fn() },
}));

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useRenameCameraMutation: () => [renameCamera, mutationState.current],
  };
});

const { RenameCameraDialog } = await import('./RenameCameraDialog.js');
const { store } = await import('../../app/store.js');

const CAMERA = '11111111-1111-1111-1111-111111111111';
const CURRENT_NAME = 'Line-3-Inlet';

function renderDialog(currentName = CURRENT_NAME) {
  render(
    <Provider store={store}>
      <RenameCameraDialog open onOpenChange={vi.fn()} cameraIdentifier={CAMERA} version={7} currentName={currentName} />
    </Provider>,
  );
}

/** A refusal shaped as RTK Query delivers it. */
const refusal = (status: number, title: string, detail: string) => ({ status, data: { title, detail } });

describe('RenameCameraDialog', () => {
  beforeEach(() => {
    renameCamera.mockReset();
    renameCamera.mockResolvedValue({ data: undefined });
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
  });

  /**
   * T006 / FR-003. A correction is an edit, not a retype — a blank field makes
   * the operator reconstruct the name they are fixing before they can fix it.
   */
  it('Opens pre-filled with the name the camera has now', () => {
    renderDialog();

    expect(screen.getByLabelText(/^name$/i)).toHaveValue(CURRENT_NAME);
  });

  /**
   * T007 / FR-010 — asserted on the **mutation's argument**, not on form state.
   *
   * What the UI shows proves nothing here: a client that normalised on the way
   * out would display the typed value and send something else, and the symptom
   * would be a rename that reports success and changes nothing.
   */
  it('Sends the name exactly as typed', async () => {
    const user = userEvent.setup();
    renderDialog();

    const field = screen.getByLabelText(/^name$/i);
    await user.clear(field);
    await user.type(field, 'Line-4-Inlet');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(renameCamera).toHaveBeenCalledWith({
      cameraIdentifier: CAMERA,
      name: 'Line-4-Inlet',
      version: 7,
    });
  });

  /**
   * T007, the half that catches the specific bug.
   *
   * A case-only correction is a **real** change to what an operator reads on a
   * wall of live video, and the two spellings normalise identically. Spec 033
   * found that trap in the repository predicate, the aggregate's idempotency
   * guard and EF's change tracker. A client that lower-cased before sending
   * would be the fourth, and this assertion is what catches it.
   */
  it('Sends a case-only correction without normalising it away', async () => {
    const user = userEvent.setup();
    renderDialog('Line-4-Inlet');

    const field = screen.getByLabelText(/^name$/i);
    await user.clear(field);
    await user.type(field, 'line-4-inlet');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(renameCamera.mock.calls[0]?.[0]?.name).toBe('line-4-inlet');
  });

  /**
   * T010(a) / FR-005. The server names the conflict; the dialog adds the action
   * the server never states.
   */
  it('Tells the operator which name is taken and to choose a different one', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(
        409,
        'CAMERA_NAME_TAKEN',
        "Another camera in fab 'munich' is already called 'line-4-inlet'. Names are unique per fab, ignoring case.",
      ),
      reset: vi.fn(),
    };

    renderDialog();

    const banner = screen.getByRole('alert');
    expect(banner).toHaveTextContent(/line-4-inlet/i);
    expect(banner).toHaveTextContent(/munich/i);
    expect(banner).toHaveTextContent(/choose a different one/i);
  });

  /**
   * T011 — the assertion that fails if the branches are ordered carelessly.
   *
   * A taken name is a **409**, like a retired camera, so a check shaped like
   * "is this a conflict?" hands it the lost-update wording. That is wrong in
   * both halves: nobody changed this camera, and reloading will not release the
   * name — the operator would reload forever against a name that is not theirs.
   */
  it('Never tells the operator to reload when the name is simply taken', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(409, 'CAMERA_NAME_TAKEN', "Another camera in fab 'munich' is already called 'line-4-inlet'."),
      reset: vi.fn(),
    };

    renderDialog();

    const banner = screen.getByRole('alert');
    expect(banner).not.toHaveTextContent(/reload/i);
    expect(banner).not.toHaveTextContent(/someone else changed/i);
  });

  /**
   * T010(b). The stale refusal keeps the shared lost-update wording, and must
   * not acquire the taken-name remedy: there is nothing wrong with the name.
   */
  it('Tells the operator to reload on a stale version, and not to rename', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(
        412,
        'CAMERA_VERSION_STALE',
        "Camera '1111' is at version 9, not 7. Re-read it before reapplying your change.",
      ),
      reset: vi.fn(),
    };

    renderDialog();

    const banner = screen.getByRole('alert');
    expect(banner).toHaveTextContent(/reload/i);
    expect(banner).not.toHaveTextContent(/choose a different/i);
    expect(banner).not.toHaveTextContent(/try again/i);
  });

  /**
   * T010(c). Terminal, and neither of the other two remedies applies — there is
   * no name to choose and no version to reload.
   */
  it('Tells the operator a retired camera cannot be changed', () => {
    mutationState.current = {
      isLoading: false,
      error: refusal(409, 'CAMERA_RETIRED', "Camera '1111' is retired; it cannot be renamed."),
      reset: vi.fn(),
    };

    renderDialog();

    const banner = screen.getByRole('alert');
    expect(banner).toHaveTextContent(/retired/i);
    expect(banner).not.toHaveTextContent(/choose a different/i);
    expect(banner).not.toHaveTextContent(/reload/i);
  });

  /**
   * T008 / FR-011. A refused rename must not cost the operator their typing —
   * the name they chose is theirs, and the refusal is about the world, not
   * about their input.
   */
  it('Keeps what the operator typed when the rename is refused', async () => {
    const user = userEvent.setup();
    renameCamera.mockResolvedValue({ error: { status: 409, data: { title: 'CAMERA_NAME_TAKEN' } } });
    renderDialog();

    const field = screen.getByLabelText(/^name$/i);
    await user.clear(field);
    await user.type(field, 'line-9-outlet');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(screen.getByLabelText(/^name$/i)).toHaveValue('line-9-outlet');
  });
});
