import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { Layout, LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { LayoutHubCallbacks } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { store } from '../../app/store.js';

const getLayoutMock = vi.fn();
const navigateMock = vi.fn();

const getOverlayMock = vi.fn();

// Capture the callbacks the lifecycle hook hands to the (long-lived) hub so
// a test can fire a synthetic OverlayHighlightChanged frame post-render.
let capturedCallbacks: LayoutHubCallbacks | undefined;

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useGetLayoutQuery: (...args: unknown[]) => getLayoutMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useGetOverlayQuery: (...args: unknown[]) => getOverlayMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    // Default: no resolved snapshot. Specific tests can re-mock to
    // assert the resolved-text rendering path.
    useGetOverlaySnapshotQuery: () => ({ data: undefined, isLoading: false }),
  };
});

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: { access_token: 'fake-token' },
  }),
}));

vi.mock('@smart-sentinel-eye/shared/realtime/layoutHub', () => ({
  createLayoutHubClient: (_config: unknown, callbacks: LayoutHubCallbacks) => {
    capturedCallbacks = callbacks;
    return {
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
      state: () => 'Connected',
    };
  },
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useParams: () => ({ layoutIdentifier: 'cam-1' }),
    useNavigate: () => navigateMock,
  };
});

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: ({
    cameraIdentifier,
    overlay,
  }: {
    cameraIdentifier: string;
    overlay?: { text: string };
  }) => (
    <div data-testid="camera-viewer" data-overlay-text={overlay?.text ?? ''}>
      {cameraIdentifier}
    </div>
  ),
}));

const { CellPage } = await import('./CellPage.js');

function tile(overrides: Partial<LayoutTile> = {}): LayoutTile {
  return {
    cameraIdentifier: 'cam-99',
    overlayIdentifier: null,
    row: 0,
    col: 0,
    ...overrides,
  };
}

function chain(overrides: Partial<Layout> = {}): Layout {
  return {
    layoutIdentifier: 'cam-1',
    name: 'Line-1',
    createdAt: '2026-05-26T10:00:00Z',
    createdBy: '00000000-0000-0000-0000-000000000001',
    revisions: [
      {
        revisionIdentifier: 'r1',
        revisionNumber: 1,
        state: 'Published',
        gridRows: 1,
        gridCols: 1,
        tiles: [tile()],
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '00000000-0000-0000-0000-000000000001',
        publishedAt: '2026-05-26T10:00:00Z',
        archivedAt: null,
      },
    ],
    ...overrides,
  };
}

function publishedRevision(gridRows: number, gridCols: number, tiles: LayoutTile[]): Layout {
  return chain({
    revisions: [
      {
        revisionIdentifier: 'r1',
        revisionNumber: 1,
        state: 'Published',
        gridRows,
        gridCols,
        tiles,
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '00000000-0000-0000-0000-000000000001',
        publishedAt: '2026-05-26T10:00:00Z',
        archivedAt: null,
      },
    ],
  });
}

function mockLayout(layout: Layout) {
  getLayoutMock.mockReturnValue({
    data: layout,
    isLoading: false,
    error: undefined,
    refetch: vi.fn(),
  });
}

function renderPage() {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <CellPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe('CellPage', () => {
  beforeEach(() => {
    getLayoutMock.mockReset();
    getOverlayMock.mockReset();
    getOverlayMock.mockReturnValue({ data: undefined });
    navigateMock.mockReset();
    capturedCallbacks = undefined;
  });

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
                  text: 'Production Line 1',
                  normalizedX: 0.5,
                  normalizedY: 0.05,
                  normalizedWidth: 0.3,
                  normalizedHeight: 0.08,
                  fontSizePx: 48,
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
    expect(viewers[0].getAttribute('data-overlay-text')).toBe('Production Line 1');
    expect(viewers[1].getAttribute('data-overlay-text')).toBe('');
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

  describe('per-tile overlay highlight (US3)', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });
    afterEach(() => {
      vi.runOnlyPendingTimers();
      vi.useRealTimers();
    });

    function highlightedTiles(): HTMLElement[] {
      return screen
        .queryAllByTestId('layout-tile')
        .filter((el) => el.dataset.highlighted === 'true');
    }

    function cameraOf(tileEl: HTMLElement): string {
      return within(tileEl).getByTestId('camera-viewer').textContent ?? '';
    }

    it('Scenario 1: lights only the tile bound to the highlighted overlay, then reverts', () => {
      mockLayout(
        publishedRevision(2, 2, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', overlayIdentifier: 'ovl-y', row: 0, col: 1 }),
          tile({ cameraIdentifier: 'cam-c', row: 1, col: 0 }),
          tile({ cameraIdentifier: 'cam-d', row: 1, col: 1 }),
        ]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', durationMs: 1000 });
      });

      const lit = highlightedTiles();
      expect(lit).toHaveLength(1);
      expect(cameraOf(lit[0])).toBe('cam-a');

      // Auto-reverts after the duration.
      act(() => {
        vi.advanceTimersByTime(1000);
      });
      expect(highlightedTiles()).toHaveLength(0);
    });

    it('Scenario 2: lights every tile bound to the reused overlay (highlight-all-matching)', () => {
      mockLayout(
        publishedRevision(2, 2, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', overlayIdentifier: 'ovl-y', row: 0, col: 1 }),
          tile({ cameraIdentifier: 'cam-c', overlayIdentifier: 'ovl-x', row: 1, col: 0 }),
          tile({ cameraIdentifier: 'cam-d', row: 1, col: 1 }),
        ]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', durationMs: 1000 });
      });

      const lit = highlightedTiles();
      expect(lit.map(cameraOf).sort()).toEqual(['cam-a', 'cam-c']);
    });

    it('Scenario 3: overlapping highlights on the same overlay survive until the later expiry', () => {
      mockLayout(
        publishedRevision(1, 2, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        ]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', durationMs: 1000 });
      });
      // A second highlight lands 500ms in with a fresh 1000ms duration.
      act(() => {
        vi.advanceTimersByTime(500);
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', durationMs: 1000 });
      });

      // The first timer (at t=1000) must NOT revert — the later expiry is t=1500.
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(highlightedTiles()).toHaveLength(1);

      // After the later expiry the tile reverts.
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(highlightedTiles()).toHaveLength(0);
    });

    it('Scenario 4: a highlight for an unbound overlay is a no-op', () => {
      mockLayout(
        publishedRevision(1, 2, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        ]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-absent', durationMs: 1000 });
      });

      expect(highlightedTiles()).toHaveLength(0);
    });
  });
});
