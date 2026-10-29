import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import type { Overlay, OverlayElement, OverlayRevisionState } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { DesignerCell } from '../gridDesignerModel';
import { TileView } from './TileView';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '22222222-2222-2222-2222-222222222222';
const OVERLAY_X = '55555555-5555-5555-5555-555555555555';
const OVERLAY_Y = '66666666-6666-6666-6666-666666666666';

const PLACEHOLDER_TEXT: OverlayElement = {
  kind: 'Text',
  color: '#FFFFFFFF',
  text: 'Speed {{line.speed}}',
  normalizedX: 0.1,
  normalizedY: 0.2,
  normalizedWidth: 0.5,
  normalizedHeight: 0.1,
  fontSizePx: 32,
};
const BOX: OverlayElement = {
  kind: 'Box',
  color: '#FF0000FF',
  normalizedX: 0.6,
  normalizedY: 0.5,
  normalizedWidth: 0.3,
  normalizedHeight: 0.4,
};

function overlay(
  identifier: string,
  name: string,
  states: OverlayRevisionState[],
  elements = [PLACEHOLDER_TEXT, BOX],
): Overlay {
  return {
    overlayIdentifier: identifier,
    version: 3,
    name,
    createdAt: '2026-10-01T00:00:00Z',
    createdBy: 'operator',
    revisions: states.map((state, index) => ({
      revisionIdentifier: `rev-${identifier}-${index}`,
      revisionNumber: index + 1,
      state,
      elements,
      createdAt: '2026-10-01T00:00:00Z',
      createdBy: 'operator',
      publishedAt: state === 'Published' ? '2026-10-02T00:00:00Z' : null,
      archivedAt: state === 'Archived' ? '2026-10-03T00:00:00Z' : null,
    })),
  };
}

// Driven per test, as the neighbouring layout tests drive their pickers.
let overlaysById: Record<string, Overlay> = {};
let captureFails = false;
const grabbedCameras: string[] = [];

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useGetOverlayQuery: (identifier: string, options?: { skip?: boolean }) => ({
      data: options?.skip ? undefined : overlaysById[identifier],
      currentData: options?.skip ? undefined : overlaysById[identifier],
      isLoading: false,
      isError: false,
    }),
    useListOverlaysQuery: () => ({
      data: {
        chains: Object.values(overlaysById),
        published: Object.values(overlaysById)
          .filter((o) => o.revisions.some((r) => r.state === 'Published'))
          .map((o) => ({
            overlayIdentifier: o.overlayIdentifier,
            name: o.name,
            revisionNumber: 1,
            elements: o.revisions[0]?.elements ?? [],
            publishedAt: '2026-10-02T00:00:00Z',
          })),
      },
      isLoading: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    // The editor's own resolver (a text in, the resolved text out), not the wall's snapshot:
    // the console has no wall fab to name (research.md R4).
    useResolveOverlayTextQuery: ({ text }: { text: string }) => ({
      data: { resolvedText: text.replace('{{line.speed}}', '42 pcs/min'), placeholders: [] },
      isLoading: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return { ...actual, useGetStreamQuery: () => ({ currentData: undefined, data: undefined }) };
});

vi.mock('@smart-sentinel-eye/shared/ui/composites/FrameGrabber', () => ({
  FrameGrabber: ({
    cameraIdentifier,
    onCaptured,
    onFailed,
  }: {
    cameraIdentifier: string;
    onCaptured: (dataUrl: string) => void;
    onFailed: () => void;
  }) => {
    useEffect(() => {
      grabbedCameras.push(cameraIdentifier);
      if (captureFails) onFailed();
      else onCaptured('data:image/png;base64,AAAA');
    }, [cameraIdentifier, onCaptured, onFailed]);
    return null;
  },
}));

function cell(overrides: Partial<DesignerCell> = {}): DesignerCell {
  return {
    cameraIdentifier: CAMERA_A,
    overlayIdentifier: OVERLAY_X,
    row: 0,
    col: 0,
    rowSpan: 1,
    colSpan: 1,
    ...overrides,
  };
}

function renderView(cells: DesignerCell[], onAssignOverlay = vi.fn()) {
  render(
    <TileView
      open
      onOpenChange={vi.fn()}
      grid={{ rows: 1, cols: cells.length }}
      cells={cells}
      cellIndex={0}
      onAssignOverlay={onAssignOverlay}
      getToken={() => Promise.resolve('token')}
      canEditOverlays
    />,
  );
  return { onAssignOverlay };
}

beforeEach(() => {
  overlaysById = {
    [OVERLAY_X]: overlay(OVERLAY_X, 'Line speed', ['Published']),
    [OVERLAY_Y]: overlay(OVERLAY_Y, 'Alarm', ['Published']),
  };
  captureFails = false;
  grabbedCameras.length = 0;
});

afterEach(cleanup);

describe('TileView — seeing an overlay on its tile', () => {
  it('shows the tile at the default 16:9 and lets the operator choose another display shape', async () => {
    const user = userEvent.setup();
    renderView([cell()]);

    const display = screen.getByTestId('tile-view-display');
    expect(display).toHaveAttribute('data-shape', '16:9');

    await user.selectOptions(screen.getByRole('combobox', { name: /display shape/i }), '4:3');
    expect(screen.getByTestId('tile-view-display')).toHaveAttribute('data-shape', '4:3');
  });

  it('shows the resolved value of a placeholder, never the braces', async () => {
    renderView([cell()]);
    const frame = await screen.findByTestId('tile-frame');
    expect(within(frame).getByTestId('camera-viewer-overlay-label')).toHaveTextContent('Speed 42 pcs/min');
    expect(frame).not.toHaveTextContent('{{');
  });

  it('shows the picture of the tile’s camera behind the overlay', async () => {
    renderView([cell()]);
    expect(await screen.findByTestId('tile-frame-backdrop')).toHaveAttribute('src', 'data:image/png;base64,AAAA');
    expect(grabbedCameras).toEqual([CAMERA_A]);
  });

  it('shows the designer’s unsaved cell, not a saved layout', () => {
    // The camera in the form is B although any saved layout would say A.
    renderView([cell({ cameraIdentifier: CAMERA_B })]);
    expect(grabbedCameras).toEqual([CAMERA_B]);
  });

  it('with no overlay shows the picture alone and offers to assign one', async () => {
    const { onAssignOverlay } = renderView([cell({ overlayIdentifier: '' })]);
    const user = userEvent.setup();

    expect(screen.queryByTestId('camera-viewer-overlay-label')).not.toBeInTheDocument();
    expect(screen.queryByTestId('camera-viewer-overlay-shape')).not.toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox', { name: /assign an overlay/i }), OVERLAY_Y);
    expect(onAssignOverlay).toHaveBeenCalledWith(0, OVERLAY_Y);
  });

  it('when the picture cannot be captured shows a neutral backdrop and a notice, and keeps the overlay', async () => {
    captureFails = true;
    renderView([cell()]);

    expect(await screen.findByTestId('tile-frame-backdrop-neutral')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/picture.*could not be captured/i);
    expect(screen.getByTestId('camera-viewer-overlay-label')).toBeInTheDocument();
  });

  it('for an overlay with only a draft shows the draft and says the wall shows nothing', async () => {
    overlaysById[OVERLAY_X] = overlay(OVERLAY_X, 'Line speed', ['Draft']);
    renderView([cell()]);

    expect(await screen.findByTestId('camera-viewer-overlay-label')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/not published.*wall shows nothing/i);
  });

  it('for an overlay archived after it was assigned shows no overlay and says so', async () => {
    overlaysById[OVERLAY_X] = overlay(OVERLAY_X, 'Line speed', ['Archived']);
    renderView([cell()]);

    expect(screen.queryByTestId('camera-viewer-overlay-label')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/archived/i);
  });

  it('reads the frame out as text: every element with its kind, resolved text and position in percent', async () => {
    renderView([cell()]);
    const readout = await screen.findByRole('region', { name: /what this tile shows/i });

    const items = within(readout).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Text');
    expect(items[0]).toHaveTextContent('Speed 42 pcs/min');
    expect(items[0]).toHaveTextContent(/10\s?%.*20\s?%/);
    expect(items[1]).toHaveTextContent('Box');
    expect(items[1]).toHaveTextContent(/60\s?%.*50\s?%/);
  });

  it('can be read and operated with the keyboard alone', async () => {
    const user = userEvent.setup();
    renderView([cell({ overlayIdentifier: '' })]);

    await user.tab();
    expect(screen.getByRole('combobox', { name: /display shape/i })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('combobox', { name: /assign an overlay/i })).toHaveFocus();
  });
});
