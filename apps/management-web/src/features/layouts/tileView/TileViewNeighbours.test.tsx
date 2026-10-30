import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { useEffect } from 'react';
import type { Overlay, OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { DesignerCell } from '../gridDesignerModel';
import { TileView } from './TileView';

const CAMERA = { a: 'cam-a', b: 'cam-b', c: 'cam-c', d: 'cam-d' };
const OVERLAY = { a: 'ov-a', b: 'ov-b', c: 'ov-c', d: 'ov-d' };

function label(text: string): OverlayElement {
  return {
    kind: 'Text',
    color: '#FFFFFFFF',
    text,
    normalizedX: 0.1,
    normalizedY: 0.2,
    normalizedWidth: 0.5,
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function overlay(identifier: string, text: string): Overlay {
  return {
    overlayIdentifier: identifier,
    version: 1,
    name: text,
    createdAt: '2026-10-01T00:00:00Z',
    createdBy: 'operator',
    revisions: [
      {
        revisionIdentifier: `rev-${identifier}`,
        revisionNumber: 1,
        state: 'Published',
        elements: [label(text)],
        createdAt: '2026-10-01T00:00:00Z',
        createdBy: 'operator',
        publishedAt: '2026-10-02T00:00:00Z',
        archivedAt: null,
      },
    ],
  };
}

const overlays: Record<string, Overlay> = {
  [OVERLAY.a]: overlay(OVERLAY.a, 'Alpha'),
  [OVERLAY.b]: overlay(OVERLAY.b, 'Bravo'),
  [OVERLAY.c]: overlay(OVERLAY.c, 'Charlie'),
  [OVERLAY.d]: overlay(OVERLAY.d, 'Delta'),
};

let failingCameras = new Set<string>();
const grabbed: string[] = [];
let mounted = 0;
let mostAtOnce = 0;

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useBranchDraftOverlayRevisionMutation: () => [vi.fn(), { isLoading: false }],
    useGetOverlayQuery: (identifier: string, options?: { skip?: boolean }) => {
      const found = options?.skip || typeof identifier !== 'string' ? undefined : overlays[identifier];
      return { data: found, currentData: found, isLoading: false, isError: false };
    },
    useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false, isError: false }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: () => ({ data: { chains: [], published: [] }, isLoading: false, isError: false }),
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
      mounted += 1;
      mostAtOnce = Math.max(mostAtOnce, mounted);
      grabbed.push(cameraIdentifier);
      if (failingCameras.has(cameraIdentifier)) onFailed();
      else onCaptured(`data:image/png;base64,${cameraIdentifier}`);
      return () => {
        mounted -= 1;
      };
    }, [cameraIdentifier, onCaptured, onFailed]);
    return null;
  },
}));

function cell(row: number, col: number, key: keyof typeof CAMERA, rowSpan = 1, colSpan = 1): DesignerCell {
  return {
    cameraIdentifier: CAMERA[key],
    overlayIdentifier: OVERLAY[key],
    row,
    col,
    rowSpan,
    colSpan,
  };
}

// 2×2: a b / c d
const FOUR = [cell(0, 0, 'a'), cell(0, 1, 'b'), cell(1, 0, 'c'), cell(1, 1, 'd')];

function view(cells: DesignerCell[], cellIndex: number, open = true) {
  return (
    <TileView
      open={open}
      onOpenChange={vi.fn()}
      grid={{ rows: 2, cols: 2 }}
      cells={cells}
      cellIndex={cellIndex}
      onAssignOverlay={vi.fn()}
      getToken={() => Promise.resolve('token')}
      canEditOverlays
    />
  );
}

beforeEach(() => {
  failingCameras = new Set();
  grabbed.length = 0;
  mounted = 0;
  mostAtOnce = 0;
});

afterEach(cleanup);

describe('TileView — the tile among its neighbours', () => {
  it('shows every tile of the layout in its grid position, with the open one marked', async () => {
    render(view(FOUR, 3));

    const tiles = await screen.findAllByTestId('context-tile');
    expect(tiles).toHaveLength(4);
    const open = tiles.filter((tile) => tile.getAttribute('data-open') === 'true');
    expect(open).toHaveLength(1);
    expect(open[0]).toHaveAttribute('data-cell-index', '3');
    expect(tiles[1]).toHaveStyle({ gridRow: '1 / span 1', gridColumn: '2 / span 1' });
  });

  it('lets a tile that spans several cells occupy them, as on the wall', async () => {
    const spanning = [cell(0, 0, 'a', 2, 1), cell(0, 1, 'b'), cell(1, 1, 'd')];
    render(view(spanning, 1));

    const tiles = await screen.findAllByTestId('context-tile');
    expect(tiles[0]).toHaveStyle({ gridRow: '1 / span 2', gridColumn: '1 / span 1' });
  });

  it('shows each neighbour’s own overlay over its own picture', async () => {
    render(view(FOUR, 0));

    const bravo = (await screen.findAllByTestId('context-tile'))[1]!;
    expect(await within(bravo).findByText('Bravo')).toBeInTheDocument();
    expect(await within(bravo).findByTestId('tile-frame-backdrop')).toHaveAttribute(
      'src',
      `data:image/png;base64,${CAMERA.b}`,
    );
  });

  it('takes the pictures one camera at a time, the open tile first and the nearest neighbours next', async () => {
    render(view(FOUR, 2));
    await screen.findAllByTestId('context-tile');

    expect(grabbed).toEqual([CAMERA.c, CAMERA.a, CAMERA.d, CAMERA.b]);
    expect(mostAtOnce).toBe(1);
  });

  it('keeps a neighbour whose picture failed on a neutral backdrop, still with its overlay', async () => {
    failingCameras = new Set([CAMERA.b]);
    render(view(FOUR, 0));

    const bravo = (await screen.findAllByTestId('context-tile'))[1]!;
    expect(await within(bravo).findByTestId('tile-frame-backdrop-neutral')).toBeInTheDocument();
    expect(within(bravo).getByText('Bravo')).toBeInTheDocument();
    // The failure of one neighbour does not stop the rest.
    expect(grabbed).toEqual([CAMERA.a, CAMERA.b, CAMERA.c, CAMERA.d]);
  });

  it('offers no editing on a neighbour', async () => {
    render(view(FOUR, 0));

    const tiles = await screen.findAllByTestId('context-tile');
    for (const tile of tiles) {
      expect(within(tile).queryByRole('button')).not.toBeInTheDocument();
      expect(within(tile).queryByRole('link')).not.toBeInTheDocument();
    }
  });

  it('releases every capture when the view is closed', async () => {
    const { rerender } = render(view(FOUR, 0));
    await screen.findAllByTestId('context-tile');

    rerender(view(FOUR, 0, false));

    expect(mounted).toBe(0);
    expect(screen.queryByTestId('context-tile')).not.toBeInTheDocument();
  });

  it('shows no context for a layout of one tile', () => {
    render(view([cell(0, 0, 'a')], 0));

    expect(screen.queryByTestId('context-tile')).not.toBeInTheDocument();
  });
});
