import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
    useGetLayoutQuery: () => ({ data: undefined, currentData: undefined, isLoading: false, isError: false }),
  };
});

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const OVERLAY_X = '55555555-5555-5555-5555-555555555555';

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: {
        items: [
          {
            cameraIdentifier: CAMERA_A,
            name: 'Line-1-Entrance',
            rtspUrl: 'rtsp://10.0.5.12/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
        ],
        count: 1,
        complete: true,
      },
      isLoading: false,
      isError: false,
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
            elements: [],
            publishedAt: '2026-05-27T10:00:00Z',
          },
        ],
      },
      isLoading: false,
    }),
  };
});

// The view itself is covered by TileView.test.tsx; here only what the dialog hands it and takes back.
vi.mock('./tileView/TileView.js', () => ({
  TileView: ({
    open,
    onOpenChange,
    cells,
    cellIndex,
    onAssignOverlay,
  }: {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    cells: Array<{ cameraIdentifier: string; overlayIdentifier: string }>;
    cellIndex: number;
    onAssignOverlay: (cellIndex: number, overlayIdentifier: string) => void;
  }) =>
    open ? (
      <div data-testid="tile-view-stub">
        <span data-testid="stub-camera">{cells[cellIndex]?.cameraIdentifier}</span>
        <span data-testid="stub-overlay">{cells[cellIndex]?.overlayIdentifier}</span>
        <button type="button" onClick={() => onAssignOverlay(cellIndex, OVERLAY_X)}>
          stub assign
        </button>
        <button type="button" onClick={() => onOpenChange(false)}>
          stub close
        </button>
      </div>
    ) : null,
}));

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

function renderDialog() {
  render(
    <Provider store={store}>
      <LayoutEditorDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

beforeEach(() => createDraftMock.mockClear());
afterEach(cleanup);

describe('LayoutEditorDialog — the tile view', () => {
  it('opens the view on the unsaved cell of the form, with no save', async () => {
    const user = userEvent.setup();
    renderDialog();

    expect(screen.queryByRole('button', { name: /open tile view/i })).not.toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.click(screen.getByRole('button', { name: /open tile view/i }));

    expect(screen.getByTestId('stub-camera')).toHaveTextContent(CAMERA_A);
    expect(createDraftMock).not.toHaveBeenCalled();
  });

  it('writes an overlay assigned in the view into the form, and saves nothing', async () => {
    const user = userEvent.setup();
    renderDialog();
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.click(screen.getByRole('button', { name: /open tile view/i }));

    await user.click(screen.getByRole('button', { name: 'stub assign' }));
    expect(screen.getByTestId('stub-overlay')).toHaveTextContent(OVERLAY_X);

    await user.click(screen.getByRole('button', { name: 'stub close' }));
    expect(screen.getByRole('combobox', { name: /overlay/i })).toHaveValue(OVERLAY_X);
    expect(createDraftMock).not.toHaveBeenCalled();
  });

  it('closing the view without assigning changes nothing', async () => {
    const user = userEvent.setup();
    renderDialog();
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.click(screen.getByRole('button', { name: /open tile view/i }));
    await user.click(screen.getByRole('button', { name: 'stub close' }));

    expect(screen.queryByTestId('tile-view-stub')).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /overlay/i })).toHaveValue('');
    expect(createDraftMock).not.toHaveBeenCalled();
  });
});
