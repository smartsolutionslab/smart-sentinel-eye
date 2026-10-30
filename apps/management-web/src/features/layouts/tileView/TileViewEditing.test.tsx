import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Overlay, OverlayElement, OverlayRevisionState } from '@smart-sentinel-eye/shared/api/overlays.api';
import { CONFLICT_FALLBACK } from '@smart-sentinel-eye/shared/api/problemDetail';
import type { DesignerCell } from '../gridDesignerModel';
import { TileView } from './TileView';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const OVERLAY_X = '55555555-5555-5555-5555-555555555555';
const OPEN_LAYOUT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const OTHER_LAYOUT = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const BOX: OverlayElement = {
  kind: 'Box',
  color: '#FF0000FF',
  normalizedX: 0.6,
  normalizedY: 0.5,
  normalizedWidth: 0.3,
  normalizedHeight: 0.4,
};

function overlay(states: OverlayRevisionState[]): Overlay {
  return {
    overlayIdentifier: OVERLAY_X,
    version: 3,
    name: 'Line speed',
    createdAt: '2026-10-01T00:00:00Z',
    createdBy: 'operator',
    revisions: states.map((state, index) => ({
      revisionIdentifier: `rev-${index}`,
      revisionNumber: index + 1,
      state,
      elements: [BOX],
      createdAt: '2026-10-01T00:00:00Z',
      createdBy: 'operator',
      publishedAt: state === 'Published' ? '2026-10-02T00:00:00Z' : null,
      archivedAt: null,
    })),
  };
}

let current: Overlay = overlay(['Published']);
let branchOutcome: 'created' | 'stale' = 'created';
const branchCalls: Array<{ overlayIdentifier: string; version: number }> = [];
let usedOnTiles = 1;
let branchGate: Promise<void> | null = null;

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useGetOverlayQuery: () => ({ data: current, currentData: current, isLoading: false, isError: false }),
    useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false, isError: false }),
    useBranchDraftOverlayRevisionMutation: () => [
      async (input: { overlayIdentifier: string; version: number }) => {
        branchCalls.push(input);
        if (branchGate !== null) await branchGate;
        if (branchOutcome === 'stale') {
          return Promise.resolve({ error: { status: 409, data: { title: 'OVERLAY_STALE' } } });
        }
        current = overlay(['Published', 'Draft']);
        return Promise.resolve({ data: 2 });
      },
      { isLoading: false },
    ],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: () => {
      // Per call, not per import: the tests set `usedOnTiles` after this factory has run.
      const tiles = Array.from({ length: usedOnTiles }, (_, row) => ({
        cameraIdentifier: CAMERA_A,
        overlayIdentifier: OVERLAY_X,
        row,
        col: 0,
        rowSpan: 1,
        colSpan: 1,
      }));
      return {
        data: {
          chains: [
            {
              layoutIdentifier: OTHER_LAYOUT,
              version: 1,
              fab: 'fab-1',
              name: 'Other',
              createdAt: '2026-10-01T00:00:00Z',
              createdBy: 'operator',
              revisions: [
                {
                  revisionIdentifier: 'r1',
                  revisionNumber: 1,
                  state: 'Published',
                  gridRows: 4,
                  gridCols: 1,
                  tiles,
                  createdAt: '2026-10-01T00:00:00Z',
                  createdBy: 'operator',
                  publishedAt: null,
                  archivedAt: null,
                },
              ],
            },
          ],
          published: [],
        },
        isLoading: false,
        isError: false,
      };
    },
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useResolveOverlayTextQuery: ({ text }: { text: string }) => ({
      data: { resolvedText: text, placeholders: [] },
      isLoading: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return { ...actual, useGetStreamQuery: () => ({ currentData: undefined, data: undefined }) };
});

vi.mock('@smart-sentinel-eye/shared/ui/composites/FrameGrabber', () => ({ FrameGrabber: () => null }));

// The real form is covered by its own suites (the T001 baseline). Here it is a stand-in that
// shows what the view hands it and lets a test report a live edit the way the form will.
let formProps: Record<string, unknown> = {};
const editTargets: unknown[] = [];
vi.mock('../../overlays/OverlayDraftForm', () => ({
  OverlayDraftForm: (props: {
    editTarget: { revisionNumber: number; elements: OverlayElement[] };
    readOnly?: boolean;
    backdropCameraIdentifier?: string;
    onElementsChange?: (elements: OverlayElement[]) => void;
  }) => {
    formProps = props as unknown as Record<string, unknown>;
    editTargets.push(props.editTarget);
    return (
      <div data-testid="draft-form" data-revision={props.editTarget.revisionNumber}>
        <button
          type="button"
          onClick={() => props.onElementsChange?.([{ ...BOX, normalizedX: 0.1 } as OverlayElement])}
        >
          Move the box
        </button>
      </div>
    );
  },
}));

function cell(): DesignerCell {
  return { cameraIdentifier: CAMERA_A, overlayIdentifier: OVERLAY_X, row: 0, col: 0, rowSpan: 1, colSpan: 1 };
}

function renderView(canEditOverlays = true) {
  render(
    <TileView
      open
      onOpenChange={vi.fn()}
      grid={{ rows: 1, cols: 1 }}
      cells={[cell()]}
      cellIndex={0}
      layoutIdentifier={OPEN_LAYOUT}
      onAssignOverlay={vi.fn()}
      getToken={() => Promise.resolve('token')}
      canEditOverlays={canEditOverlays}
    />,
  );
}

beforeEach(() => {
  current = overlay(['Published']);
  branchOutcome = 'created';
  branchCalls.length = 0;
  usedOnTiles = 1;
  formProps = {};
  editTargets.length = 0;
  branchGate = null;
});

afterEach(cleanup);

describe('TileView — editing the overlay on its tile', () => {
  it('edits a published overlay through a new draft and leaves the published revision alone', async () => {
    const user = userEvent.setup();
    renderView();

    await user.click(screen.getByRole('button', { name: /edit this overlay/i }));

    expect(branchCalls).toEqual([{ overlayIdentifier: OVERLAY_X, version: 3 }]);
    const form = await screen.findByTestId('draft-form');
    expect(form).toHaveAttribute('data-revision', '2');
    expect(current.revisions.find((revision) => revision.state === 'Published')?.revisionNumber).toBe(1);
  });

  it('edits the draft that already exists instead of making another', async () => {
    current = overlay(['Published', 'Draft']);
    const user = userEvent.setup();
    renderView();

    await user.click(screen.getByRole('button', { name: /edit this overlay/i }));

    expect(branchCalls).toEqual([]);
    expect(await screen.findByTestId('draft-form')).toHaveAttribute('data-revision', '2');
  });

  it('asks for a draft once however often the button is pressed while it is being made', async () => {
    let release: () => void = () => {};
    branchGate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const user = userEvent.setup();
    renderView();

    const button = screen.getByRole('button', { name: /edit this overlay/i });
    await user.click(button);
    await user.click(button);
    release();

    expect(branchCalls).toHaveLength(1);
    expect(await screen.findByTestId('draft-form')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows the edit on the open tile in the layout too, not only on the large one', async () => {
    const user = userEvent.setup();
    render(
      <TileView
        open
        onOpenChange={vi.fn()}
        grid={{ rows: 1, cols: 2 }}
        cells={[cell(), { ...cell(), col: 1, cameraIdentifier: 'cam-b', overlayIdentifier: '' }]}
        cellIndex={0}
        layoutIdentifier={OPEN_LAYOUT}
        onAssignOverlay={vi.fn()}
        getToken={() => Promise.resolve('token')}
        canEditOverlays
      />,
    );
    await user.click(screen.getByRole('button', { name: /edit this overlay/i }));
    await screen.findByTestId('draft-form');

    await user.click(screen.getByRole('button', { name: /move the box/i }));

    const open = screen.getAllByTestId('context-tile').find((tile) => tile.getAttribute('data-open') === 'true')!;
    expect(within(open).getByTestId('camera-viewer-overlay-shape')).toHaveStyle({ left: '10%' });
  });

  it('hands the form the same edit target after an edit, or the form would reset and lose the edit', async () => {
    const user = userEvent.setup();
    renderView();
    await user.click(screen.getByRole('button', { name: /edit this overlay/i }));
    await screen.findByTestId('draft-form');

    await user.click(screen.getByRole('button', { name: /move the box/i }));

    expect(editTargets.length).toBeGreaterThan(1);
    expect(new Set(editTargets).size).toBe(1);
  });

  it('hands the form the tile’s camera and the layout mode, and is not read-only', async () => {
    const user = userEvent.setup();
    renderView();
    await user.click(screen.getByRole('button', { name: /edit this overlay/i }));
    await screen.findByTestId('draft-form');

    expect(formProps).toMatchObject({ backdropCameraIdentifier: CAMERA_A });
    expect(formProps.readOnly).not.toBe(true);
  });

  it('moves the element in the frame as the operator edits, before anything is saved', async () => {
    const user = userEvent.setup();
    renderView();
    await user.click(screen.getByRole('button', { name: /edit this overlay/i }));
    await screen.findByTestId('draft-form');
    expect(screen.getByRole('region', { name: /what this tile shows/i })).toHaveTextContent(/left 60 ?%/i);

    await user.click(screen.getByRole('button', { name: /move the box/i }));

    expect(screen.getByRole('region', { name: /what this tile shows/i })).toHaveTextContent(/left 10 ?%/i);
  });

  it('shows the existing conflict message when the draft cannot be made because the overlay moved on', async () => {
    branchOutcome = 'stale';
    const user = userEvent.setup();
    renderView();

    await user.click(screen.getByRole('button', { name: /edit this overlay/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(CONFLICT_FALLBACK);
    expect(screen.queryByTestId('draft-form')).not.toBeInTheDocument();
  });

  it('keeps the view open but offers no editor without the overlay write right', () => {
    renderView(false);

    expect(screen.getByTestId('tile-frame')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /edit this overlay/i })).not.toBeInTheDocument();
    expect(within(screen.getByRole('status')).getByText(/not allowed to change overlays/i)).toBeInTheDocument();
  });

  it('says an overlay used on several tiles is shared, counting the open layout’s unsaved cell', () => {
    usedOnTiles = 2;
    renderView();

    // Two saved tiles in the other layout plus the designer's own unsaved cell.
    expect(screen.getByText(/used on at least 3 tiles/i)).toBeInTheDocument();
    expect(screen.getByText(/changing it changes all of them/i)).toBeInTheDocument();
  });

  it('does not call an overlay on a single tile shared', () => {
    usedOnTiles = 0;
    renderView();

    // Only the designer's own cell uses it; the count is shown, the shared warning is not.
    expect(screen.getByText(/used on at least 1 tile\b/i)).toBeInTheDocument();
    expect(screen.queryByText(/changing it changes all of them/i)).not.toBeInTheDocument();
  });

  it('links to the overlay editor, keyboard-reachable, without leaving the designer', () => {
    current = overlay(['Published', 'Draft']);
    renderView();

    const link = screen.getByRole('link', { name: /open in the overlay editor/i });
    expect(link).toHaveAttribute('href', `/overlays/${OVERLAY_X}/revisions/2/edit`);
    expect(link).toHaveAttribute('target', '_blank');
  });
});
