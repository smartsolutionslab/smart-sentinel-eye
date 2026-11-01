import {
  getOverlayMock,
  tile,
  chain,
  publishedRevision,
  mockLayout,
  renderPage,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

  it('Renders a single CameraViewer for an N=1 layout (identical to the pre-feature cell)', () => {
    mockLayout(chain());

    renderPage();
    expect(screen.getByTestId('camera-viewer')).toHaveTextContent('cam-99');
    expect(screen.getAllByTestId('layout-tile')).toHaveLength(1);
    expect(screen.queryByTestId('layout-empty-cell')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Line-1' })).toBeInTheDocument();
    expect(screen.getByTestId('camera-viewer').getAttribute('data-overlay-text')).toBe('');
  });

  it('Renders one CameraViewer per populated cell of a 2x2 grid', () => {
    mockLayout(
      publishedRevision(2, 2, [
        tile({ cameraIdentifier: 'cam-a', row: 0, col: 0 }),
        tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        tile({ cameraIdentifier: 'cam-c', row: 1, col: 0 }),
        tile({ cameraIdentifier: 'cam-d', row: 1, col: 1 }),
      ]),
    );

    renderPage();
    const viewers = screen.getAllByTestId('camera-viewer');
    expect(viewers).toHaveLength(4);
    expect(viewers.map((v) => v.textContent)).toEqual(['cam-a', 'cam-b', 'cam-c', 'cam-d']);
    expect(screen.queryByTestId('layout-empty-cell')).not.toBeInTheDocument();
  });

  it('Renders a placeholder for an empty cell of a sparse grid', () => {
    mockLayout(
      publishedRevision(2, 2, [
        tile({ cameraIdentifier: 'cam-a', row: 0, col: 0 }),
        tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        tile({ cameraIdentifier: 'cam-c', row: 1, col: 0 }),
      ]),
    );

    renderPage();
    expect(screen.getAllByTestId('camera-viewer')).toHaveLength(3);
    expect(screen.getAllByTestId('layout-empty-cell')).toHaveLength(1);
  });

  /**
   * Spec 262 (#2607) §8 declared pin — CHARACTERISATION, must stay GREEN.
   * "A 1x1 wall renders exactly as today" (spec Gherkin, US2). A non-square
   * grid, so a row-major-only regression (columns and rows swapped, or a
   * transposed order) would show here even though the 2x2 case above would
   * not. This is the literal green proof `wallGrid.test.ts`'s own
   * characterisation case points back to, since that file cannot resolve
   * until `wallGrid.ts` exists.
   */
  it('Renders every tile of an all-1x1 wall in row-major order, unchanged by this feature', () => {
    mockLayout(
      publishedRevision(2, 3, [
        tile({ cameraIdentifier: 'cam-a', row: 0, col: 0 }),
        tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        tile({ cameraIdentifier: 'cam-c', row: 0, col: 2 }),
        tile({ cameraIdentifier: 'cam-d', row: 1, col: 0 }),
        tile({ cameraIdentifier: 'cam-e', row: 1, col: 1 }),
        tile({ cameraIdentifier: 'cam-f', row: 1, col: 2 }),
      ]),
    );

    renderPage();
    const viewers = screen.getAllByTestId('camera-viewer');
    expect(viewers.map((v) => v.textContent)).toEqual(['cam-a', 'cam-b', 'cam-c', 'cam-d', 'cam-e', 'cam-f']);
    expect(screen.queryByTestId('layout-empty-cell')).not.toBeInTheDocument();
  });

  /**
   * RED (spec 262 US2, FR-008). `CellPage` does no explicit grid placement
   * today — every tile flows into CSS grid auto-placement, one cell each
   * (spec.md §1 finding: "the kiosk places tiles one cell each ... by
   * auto-placement"). A hero tile therefore carries no `grid-row`/
   * `grid-column` style yet, so this fails until `wallGrid.ts` /
   * `buildGridItems` is wired into `CellPage` (plan.md §4.2).
   */
  it('Places a spanning hero tile at grid-row 1 / span 2 and grid-column 1 / span 2', () => {
    mockLayout(
      publishedRevision(3, 3, [
        tile({ cameraIdentifier: 'cam-hero', row: 0, col: 0, rowSpan: 2, colSpan: 2 }),
        tile({ cameraIdentifier: 'cam-a', row: 0, col: 2 }),
        tile({ cameraIdentifier: 'cam-b', row: 1, col: 2 }),
        tile({ cameraIdentifier: 'cam-c', row: 2, col: 0 }),
        tile({ cameraIdentifier: 'cam-d', row: 2, col: 1 }),
        tile({ cameraIdentifier: 'cam-e', row: 2, col: 2 }),
      ]),
    );

    renderPage();
    const heroTile = screen.getAllByTestId('layout-tile')[0];
    expect(heroTile?.style.gridRow).toBe('1 / span 2');
    expect(heroTile?.style.gridColumn).toBe('1 / span 2');
  });

  it('Renders the bound overlay label per tile', () => {
    mockLayout(
      publishedRevision(1, 2, [
        tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-1', row: 0, col: 0 }),
        tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
      ]),
    );
    // Resolve only the tile that actually binds `ovl-1`; the skipped query
    // on the unbound tile returns no data (matches RTK Query's `skip`).
    getOverlayMock.mockImplementation((overlayIdentifier: string) =>
      overlayIdentifier === 'ovl-1'
        ? {
            data: {
              overlayIdentifier: 'ovl-1',
              name: 'Line-1 Title',
              createdAt: '2026-05-27T10:00:00Z',
              createdBy: '00000000-0000-0000-0000-000000000001',
              revisions: [
                {
                  revisionIdentifier: 'or1',
                  revisionNumber: 1,
                  state: 'Published',
                  elements: [
                    {
                      kind: 'Text',
                      color: '#FFFFFFD9',
                      text: 'Production Line 1',
                      normalizedX: 0.5,
                      normalizedY: 0.05,
                      normalizedWidth: 0.3,
                      normalizedHeight: 0.08,
                      fontSizePx: 48,
                    },
                  ],
                  createdAt: '2026-05-27T10:00:00Z',
                  createdBy: '00000000-0000-0000-0000-000000000001',
                  publishedAt: '2026-05-27T10:00:00Z',
                  archivedAt: null,
                },
              ],
            },
          }
        : { data: undefined },
    );

    renderPage();
    const viewers = screen.getAllByTestId('camera-viewer');
    expect(viewers[0]!.getAttribute('data-overlay-text')).toBe('Production Line 1');
    expect(viewers[1]!.getAttribute('data-overlay-text')).toBe('');
  });

  it('Falls back to the picker prompt when no Published revision exists', () => {
    mockLayout(
      chain({
        revisions: [
          {
            revisionIdentifier: 'r1',
            revisionNumber: 1,
            state: 'Draft',
            gridRows: 1,
            gridCols: 1,
            tiles: [tile()],
            createdAt: '2026-05-26T10:00:00Z',
            createdBy: '00000000-0000-0000-0000-000000000001',
            publishedAt: null,
            archivedAt: null,
          },
        ],
      }),
    );

    renderPage();
    expect(screen.getByText(/layout is no longer available/i)).toBeInTheDocument();
  });
});
