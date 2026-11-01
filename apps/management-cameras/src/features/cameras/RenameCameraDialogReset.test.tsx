import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

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
const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { streamsApi } = await import('@smart-sentinel-eye/shared/api/streams.api');
const store = createApiStore([camerasApi, streamsApi]);

const CAMERA = '11111111-1111-1111-1111-111111111111';

function ui(open: boolean, currentName: string) {
  return (
    <Provider store={store}>
      <RenameCameraDialog
        open={open}
        onOpenChange={vi.fn()}
        cameraIdentifier={CAMERA}
        version={7}
        currentName={currentName}
      />
    </Provider>
  );
}

/**
 * Spec 475 T002. Characterises what `RenameCameraDialog` does today, before its reset effect moves into a shared hook:
 * it re-resets when `currentName` changes **while the dialog is open** (its effect depends on `[open, currentName]`,
 * unlike `EditCameraAddressDialog`'s `[open]`). The existing Rename tests never rerender, so this was unpinned.
 * Green on the unmigrated tree by design; it must stay green after.
 */
describe('RenameCameraDialog — what resets the form and the mutation state', () => {
  beforeEach(() => {
    renameCamera.mockReset();
    renameCamera.mockResolvedValue({ data: undefined });
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
  });

  it('Resets the field to the new name when currentName changes while the dialog is open', async () => {
    const user = userEvent.setup();
    const { rerender } = render(ui(true, 'Line-3-Inlet'));
    const field = screen.getByLabelText(/^name$/i);
    await user.clear(field);
    await user.type(field, 'half-typed');
    expect(field).toHaveValue('half-typed');

    rerender(ui(true, 'Line-4-Inlet'));

    expect(screen.getByLabelText(/^name$/i)).toHaveValue('Line-4-Inlet');
  });

  it('Resets the mutation state when it opens, and again when currentName changes while open', () => {
    const { rerender } = render(ui(true, 'Line-3-Inlet'));
    const afterOpening = mutationState.current.reset.mock.calls.length;
    expect(afterOpening).toBeGreaterThan(0);

    rerender(ui(true, 'Line-4-Inlet'));

    expect(mutationState.current.reset.mock.calls.length).toBeGreaterThan(afterOpening);
  });

  it('Does not reset again on a rerender that changes neither open nor currentName', () => {
    const { rerender } = render(ui(true, 'Line-3-Inlet'));
    const afterOpening = mutationState.current.reset.mock.calls.length;

    rerender(ui(true, 'Line-3-Inlet'));
    rerender(ui(true, 'Line-3-Inlet'));

    expect(mutationState.current.reset.mock.calls.length).toBe(afterOpening);
  });

  it('Starts from the stored name again when the dialog is closed and reopened', async () => {
    const user = userEvent.setup();
    const { rerender } = render(ui(true, 'Line-3-Inlet'));
    const field = screen.getByLabelText(/^name$/i);
    await user.clear(field);
    await user.type(field, 'abandoned');

    rerender(ui(false, 'Line-3-Inlet'));
    rerender(ui(true, 'Line-3-Inlet'));

    expect(screen.getByLabelText(/^name$/i)).toHaveValue('Line-3-Inlet');
  });
});
