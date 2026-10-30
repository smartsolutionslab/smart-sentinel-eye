import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import type {
  Layout,
  LayoutRevision,
  LayoutRevisionState,
  LayoutTile,
} from '@smart-sentinel-eye/shared/api/layouts.api';
import type { DesignerCell } from '../gridDesignerModel';

const OVERLAY = '55555555-5555-5555-5555-555555555555';
const OTHER = '66666666-6666-6666-6666-666666666666';
const OPEN_LAYOUT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const SECOND_LAYOUT = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function tile(overlayIdentifier: string | null, row = 0): LayoutTile {
  return { cameraIdentifier: 'cam', overlayIdentifier, row, col: 0, rowSpan: 1, colSpan: 1 };
}

function revision(state: LayoutRevisionState, tiles: LayoutTile[], number = 1): LayoutRevision {
  return {
    revisionIdentifier: `rev-${number}-${state}`,
    revisionNumber: number,
    state,
    gridRows: 4,
    gridCols: 1,
    tiles,
    createdAt: '2026-10-01T00:00:00Z',
    createdBy: 'operator',
    publishedAt: null,
    archivedAt: null,
  };
}

function layout(layoutIdentifier: string, revisions: LayoutRevision[]): Layout {
  return {
    layoutIdentifier,
    version: 1,
    fab: 'fab-1',
    name: layoutIdentifier,
    createdAt: '2026-10-01T00:00:00Z',
    createdBy: 'operator',
    revisions,
  };
}

function cell(overlayIdentifier: string, row = 0): DesignerCell {
  return { cameraIdentifier: 'cam', overlayIdentifier, row, col: 0, rowSpan: 1, colSpan: 1 };
}

let chains: Layout[] = [];
let listState: { isLoading: boolean; isError: boolean } = { isLoading: false, isError: false };

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: () => ({
      data: listState.isLoading || listState.isError ? undefined : { chains, published: [] },
      ...listState,
    }),
  };
});

const { useOverlayUsage } = await import('./useOverlayUsage');

describe('useOverlayUsage — how many tiles use an overlay', () => {
  beforeEach(() => {
    chains = [];
    listState = { isLoading: false, isError: false };
  });

  it('counts a layout’s tiles once, by its fullest open revision, and leaves Archived ones out', () => {
    chains = [
      layout(SECOND_LAYOUT, [
        revision('Published', [tile(OVERLAY), tile(OTHER, 1)], 1),
        revision('Draft', [tile(OVERLAY), tile(OVERLAY, 1)], 2),
        revision('Archived', [tile(OVERLAY), tile(OVERLAY, 1), tile(OVERLAY, 2)], 0),
      ]),
    ];

    const { result } = renderHook(() =>
      useOverlayUsage({ overlayIdentifier: OVERLAY, layoutIdentifier: undefined, cells: [] }),
    );

    expect(result.current.count).toBe(2);
  });

  it('replaces the open layout’s saved tiles with the designer’s unsaved cells', () => {
    chains = [
      layout(OPEN_LAYOUT, [revision('Published', [tile(OVERLAY), tile(OVERLAY, 1), tile(OVERLAY, 2)])]),
      layout(SECOND_LAYOUT, [revision('Published', [tile(OVERLAY)])]),
    ];

    const { result } = renderHook(() =>
      useOverlayUsage({
        overlayIdentifier: OVERLAY,
        layoutIdentifier: OPEN_LAYOUT,
        cells: [cell(OVERLAY), cell(OTHER, 1)],
      }),
    );

    // One unsaved cell in the open layout, one saved tile in the other. Not the three saved ones.
    expect(result.current.count).toBe(2);
  });

  it('adds the cells of a layout that has not been saved yet', () => {
    chains = [layout(SECOND_LAYOUT, [revision('Published', [tile(OVERLAY)])])];

    const { result } = renderHook(() =>
      useOverlayUsage({
        overlayIdentifier: OVERLAY,
        layoutIdentifier: undefined,
        cells: [cell(OVERLAY), cell(OVERLAY, 1)],
      }),
    );

    expect(result.current.count).toBe(3);
  });

  it('does not know the count while the layouts are loading, and says so when they cannot be read', () => {
    listState = { isLoading: true, isError: false };
    const loading = renderHook(() =>
      useOverlayUsage({ overlayIdentifier: OVERLAY, layoutIdentifier: undefined, cells: [] }),
    );
    expect(loading.result.current).toEqual({ count: null, failed: false });

    listState = { isLoading: false, isError: true };
    const failed = renderHook(() =>
      useOverlayUsage({ overlayIdentifier: OVERLAY, layoutIdentifier: undefined, cells: [] }),
    );
    expect(failed.result.current).toEqual({ count: null, failed: true });
  });
});
