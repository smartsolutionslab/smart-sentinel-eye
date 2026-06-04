import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { LayoutEditTarget } from './LayoutEditorDialog.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async () => ({ data: 2 }));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [
      createDraftMock,
      { isLoading: false, error: undefined, reset: vi.fn() },
    ],
    useEditDraftRevisionMutation: () => [
      editDraftMock,
      { isLoading: false, error: undefined, reset: vi.fn() },
    ],
  };
});

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '22222222-2222-2222-2222-222222222222';
const OVERLAY_X = '55555555-5555-5555-5555-555555555555';

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: () => ({
      data: {
        items: [
          {
            cameraIdentifier: CAMERA_A,
            name: 'Line-1-Entrance',
            rtspUrl: 'rtsp://10.0.5.12/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
          {
            cameraIdentifier: CAMERA_B,
            name: 'Line-2-Exit',
            rtspUrl: 'rtsp://10.0.5.13/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
        ],
        count: 2,
        offset: 0,
        limit: 50,
      },
      isLoading: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: () => ({
      data: {
        chains: [],
        published: [
          {
            overlayIdentifier: OVERLAY_X,
            name: 'Line-1 Title',
            revisionNumber: 1,
            text: 'Production Line 1',
            publishedAt: '2026-05-27T10:00:00Z',
          },
        ],
      },
      isLoading: false,
    }),
  };
});

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

function renderDialog(editTarget?: LayoutEditTarget) {
  return render(
    <Provider store={store}>
      <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={editTarget} />
    </Provider>,
  );
}

describe('LayoutEditorDialog — create', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
  });

  it('Starts on a 1×1 grid with a name input and one camera picker', () => {
    renderDialog();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getAllByRole('combobox', { name: /camera/i })).toHaveLength(1);
    expect(screen.getByRole('option', { name: 'Line-1-Entrance' })).toBeInTheDocument();
  });

  it('Submits a single-tile wall with the chosen overlay', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-1');
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.selectOptions(screen.getByRole('combobox', { name: /overlay/i }), OVERLAY_X);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Line-1',
      grid: { rows: 1, cols: 1 },
      tiles: [{ cameraIdentifier: CAMERA_A, overlayIdentifier: OVERLAY_X, row: 0, col: 0 }],
    });
  });

  it('Submits overlayIdentifier=null when "(none)" is selected', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-2');
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Line-2',
      grid: { rows: 1, cols: 1 },
      tiles: [{ cameraIdentifier: CAMERA_A, overlayIdentifier: null, row: 0, col: 0 }],
    });
  });

  it('Resizing to 1×2 and filling both cells submits two tiles', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Wall');
    await user.click(screen.getByRole('radio', { name: '1×2' }));

    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    expect(cameraSelects).toHaveLength(2);
    await user.selectOptions(cameraSelects[0], CAMERA_A);
    await user.selectOptions(cameraSelects[1], CAMERA_B);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Wall',
      grid: { rows: 1, cols: 2 },
      tiles: [
        { cameraIdentifier: CAMERA_A, overlayIdentifier: null, row: 0, col: 0 },
        { cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 1 },
      ],
    });
  });

  it('Drops an empty cell so a sparse 1×2 submits one tile', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Sparse');
    await user.click(screen.getByRole('radio', { name: '1×2' }));

    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    await user.selectOptions(cameraSelects[0], CAMERA_A);
    // Leave cell 2 empty.
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Sparse',
      grid: { rows: 1, cols: 2 },
      tiles: [{ cameraIdentifier: CAMERA_A, overlayIdentifier: null, row: 0, col: 0 }],
    });
  });

  it('Rejects a wall with no populated cell (at least one tile required)', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Empty');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(await screen.findByText(/at least one tile/i)).toBeInTheDocument();
    expect(createDraftMock).not.toHaveBeenCalled();
  });

  it('Surfaces a validation error when the name is blank', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(createDraftMock).not.toHaveBeenCalled();
  });
});

describe('LayoutEditorDialog — edit', () => {
  const editTarget: LayoutEditTarget = {
    layoutIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    revisionNumber: 2,
    name: 'Rolling Mill',
    grid: { rows: 1, cols: 2 },
    tiles: [
      { cameraIdentifier: CAMERA_A, overlayIdentifier: OVERLAY_X, row: 0, col: 0 },
      { cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 1 },
    ],
  };

  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
  });

  it('Pre-loads the branched draft grid + tiles and PATCHes on save', async () => {
    const user = userEvent.setup();
    renderDialog(editTarget);

    // No name input in edit mode (the chain name is kept).
    expect(screen.queryByLabelText(/name/i)).not.toBeInTheDocument();
    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    expect(cameraSelects).toHaveLength(2);
    expect((cameraSelects[0] as HTMLSelectElement).value).toBe(CAMERA_A);
    expect((cameraSelects[1] as HTMLSelectElement).value).toBe(CAMERA_B);

    await user.click(screen.getByRole('button', { name: /save draft/i }));

    expect(editDraftMock).toHaveBeenCalledWith({
      layoutIdentifier: editTarget.layoutIdentifier,
      revisionNumber: 2,
      grid: { rows: 1, cols: 2 },
      tiles: [
        { cameraIdentifier: CAMERA_A, overlayIdentifier: OVERLAY_X, row: 0, col: 0 },
        { cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 1 },
      ],
    });
    expect(createDraftMock).not.toHaveBeenCalled();
  });

  it('Lets the operator swap a tile camera before saving', async () => {
    const user = userEvent.setup();
    renderDialog(editTarget);

    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    await user.selectOptions(cameraSelects[1], CAMERA_A);
    await user.click(screen.getByRole('button', { name: /save draft/i }));

    const body = editDraftMock.mock.calls[0]?.[0] as { tiles: Array<{ cameraIdentifier: string }> };
    expect(body.tiles[1].cameraIdentifier).toBe(CAMERA_A);
  });
});
