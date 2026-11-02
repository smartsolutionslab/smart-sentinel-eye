import type { Layout, LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';

/**
 * Fixture builders shared by the LayoutGrid gap tests (spec 480). Plain data
 * only: every `vi.mock` stays in the test file that owns it, because the
 * factories are hoisted per file.
 */

export interface ElementFixture {
  kind: 'Text' | 'Box' | 'Ellipse';
  color: string;
  text?: string;
  fontSizePx?: number;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
}

export function tileAt(row: number, col: number, overrides: Partial<LayoutTile> = {}): LayoutTile {
  return {
    cameraIdentifier: `cam-${row}-${col}`,
    overlayIdentifier: 'ovl-x',
    row,
    col,
    rowSpan: 1,
    colSpan: 1,
    ...overrides,
  };
}

export interface LayoutFixtureOptions {
  rows?: number;
  cols?: number;
  fab?: string;
  name?: string;
  state?: 'Published' | 'Archived' | 'Draft';
}

export function layoutOf(tiles: LayoutTile[], options: LayoutFixtureOptions = {}): Layout {
  const { rows = 1, cols = 1, fab = 'munich', name = 'Line-1', state = 'Published' } = options;
  return {
    layoutIdentifier: 'cam-1',
    version: 0,
    name,
    fab,
    createdAt: '2026-05-26T10:00:00Z',
    createdBy: '00000000-0000-0000-0000-000000000001',
    revisions: [
      {
        revisionIdentifier: 'r1',
        revisionNumber: 1,
        state,
        gridRows: rows,
        gridCols: cols,
        tiles,
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '00000000-0000-0000-0000-000000000001',
        publishedAt: '2026-05-26T10:00:00Z',
        archivedAt: null,
      },
    ],
  };
}

export function textEl(overrides: Partial<ElementFixture> = {}): ElementFixture {
  return {
    kind: 'Text',
    color: '#FFFFFFD9',
    text: 'Static label',
    fontSizePx: 14,
    normalizedX: 0.1,
    normalizedY: 0.05,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    ...overrides,
  };
}

export function boxEl(overrides: Partial<ElementFixture> = {}): ElementFixture {
  return {
    kind: 'Box',
    color: '#FF0000FF',
    normalizedX: 0.5,
    normalizedY: 0.5,
    normalizedWidth: 0.2,
    normalizedHeight: 0.1,
    ...overrides,
  };
}

/** An overlay query result: one revision in `state`, carrying `elements`. */
export function overlayOf(elements: unknown[], state: 'Published' | 'Archived' = 'Published', revisionNumber = 1) {
  return {
    data: {
      overlayIdentifier: 'ovl-x',
      name: 'Bound overlay',
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '00000000-0000-0000-0000-000000000001',
      revisions: [
        {
          revisionIdentifier: `or${revisionNumber}`,
          revisionNumber,
          state,
          elements,
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '00000000-0000-0000-0000-000000000001',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
      ],
    },
  };
}
