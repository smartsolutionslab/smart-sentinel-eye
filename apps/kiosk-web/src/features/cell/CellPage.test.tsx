import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider, useSelector } from 'react-redux';
import type { Layout, LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { LayoutHubCallbacks } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
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

const getSnapshotMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    // Default: no resolved snapshot. Specific tests re-mock to assert the
    // resolved-text rendering path — which the fixed arrow this replaced
    // promised in a comment but made impossible.
    useGetOverlaySnapshotQuery: (...args: unknown[]) => getSnapshotMock(...args),
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

/**
 * The viewer's props are captured so a test can drive the one thing jsdom
 * cannot produce: a measured frame age. Without a lag report every tile's age
 * is null, the label hold never engages, and a test of the hold passes with the
 * wiring cut — the vacuous-test trap spec 045's review found five of.
 */
let reportLag: ((camera: string, lag: number, buffer: number) => void) | undefined;

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: ({
    cameraIdentifier,
    overlay,
    onLagMeasured,
  }: {
    cameraIdentifier: string;
    overlay?: { text: string };
    onLagMeasured?: (camera: string, lag: number, buffer: number) => void;
  }) => {
    if (onLagMeasured) reportLag = onLagMeasured;
    return (
      <div data-testid="camera-viewer" data-overlay-text={overlay?.text ?? ''}>
        {cameraIdentifier}
      </div>
    );
  },
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
    version: 0,
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

/** An overlay whose published revision carries `text`. */
function publishedOverlay(text: string) {
  return {
    data: {
      overlayIdentifier: 'ovl-x',
      name: 'Bound label',
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '00000000-0000-0000-0000-000000000001',
      revisions: [
        {
          revisionIdentifier: 'or1',
          revisionNumber: 1,
          state: 'Published',
          text,
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
  };
}

describe('CellPage', () => {
  beforeEach(() => {
    getLayoutMock.mockReset();
    getOverlayMock.mockReset();
    getOverlayMock.mockReturnValue({ data: undefined });
    getSnapshotMock.mockReset();
    getSnapshotMock.mockReturnValue({ data: undefined, isLoading: false });
    navigateMock.mockReset();
    capturedCallbacks = undefined;
    reportLag = undefined;
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

  describe('live-update resilience (spec 011 US2)', () => {
    it('Shows the degraded badge while live updates are down and clears it on recovery', () => {
      mockLayout(chain());
      renderPage();
      expect(screen.queryByTestId('live-updates-degraded')).not.toBeInTheDocument();

      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      expect(screen.getByTestId('live-updates-degraded')).toBeInTheDocument();

      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });
      expect(screen.queryByTestId('live-updates-degraded')).not.toBeInTheDocument();
    });

    it('Flags a tile bound to an overlay archived before the kiosk loaded (FR-009)', () => {
      mockLayout(publishedRevision(1, 1, [tile({ overlayIdentifier: 'ovl-gone', row: 0, col: 0 })]));
      // The fetched overlay exists but has no Published revision — archived
      // pre-mount, so no OverlayArchived push was ever observed.
      getOverlayMock.mockReturnValue({
        data: {
          overlayIdentifier: 'ovl-gone',
          name: 'Retired label',
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '00000000-0000-0000-0000-000000000001',
          revisions: [
            {
              revisionIdentifier: 'or1',
              revisionNumber: 1,
              state: 'Archived',
              text: 'Old text',
              normalizedX: 0.5,
              normalizedY: 0.05,
              normalizedWidth: 0.3,
              normalizedHeight: 0.08,
              fontSizePx: 48,
              createdAt: '2026-05-27T10:00:00Z',
              createdBy: '00000000-0000-0000-0000-000000000001',
              publishedAt: '2026-05-27T10:00:00Z',
              archivedAt: '2026-05-28T10:00:00Z',
            },
          ],
        },
      });

      renderPage();
      expect(screen.getByText(/overlay unavailable/i)).toBeInTheDocument();
    });

    it('Does not flag a tile while its overlay is still loading', () => {
      mockLayout(publishedRevision(1, 1, [tile({ overlayIdentifier: 'ovl-pending', row: 0, col: 0 })]));
      getOverlayMock.mockReturnValue({ data: undefined });

      renderPage();
      expect(screen.queryByText(/overlay unavailable/i)).not.toBeInTheDocument();
    });
  });

  describe('per-tile overlay highlight (US3)', () => {
    beforeEach(() => {
      // Highlight expiry math runs on performance.now() (spec 011 T024 —
      // PTP clock steps must not affect it), which vitest's default
      // toFake set does not include.
      vi.useFakeTimers({
        toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date', 'performance'],
      });
    });
    afterEach(() => {
      vi.runOnlyPendingTimers();
      vi.useRealTimers();
    });

    function highlightedTiles() {
      return screen.queryAllByTestId('layout-tile').filter((el) => el.dataset.highlighted === 'true');
    }

    const cameraOf = (tileEl: ReturnType<typeof highlightedTiles>[number]): string =>
      within(tileEl).getByTestId('camera-viewer').textContent ?? '';

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
      expect(cameraOf(lit[0]!)).toBe('cam-a');

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

    it('A wall-clock step does not expire an active highlight early', () => {
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 })]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', durationMs: 1000 });
      });
      // A PTP step forward moves Date, not the monotonic clock; the timer
      // has not fired yet, so the highlight must still be lit.
      act(() => {
        vi.setSystemTime(Date.now() + 60_000);
      });
      expect(highlightedTiles()).toHaveLength(1);

      act(() => {
        vi.advanceTimersByTime(1000);
      });
      expect(highlightedTiles()).toHaveLength(0);
    });

    it('Unmount clears pending highlight timers', () => {
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 })]),
      );
      const view = renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', durationMs: 1000 });
      });
      view.unmount();

      expect(vi.getTimerCount()).toBe(0);
    });
  });

  /**
   * Spec 046 US2 — the wiring, which nothing else covers.
   *
   * <p>
   * `useLabelDelay` is tested on its own and `frameAgeFor` is tested on its
   * own. What neither proves is that this page joins them for the right tile:
   * a hook wired to the wrong key, or not wired at all, leaves every other
   * test green. The live walk covered it once with real video; this covers it
   * on every run.
   * </p>
   */
  describe('Holding a label back to the age of its own picture', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    /**
     * Forces the render on which the changed label is read.
     *
     * <p>
     * A highlight rather than a controller cycle: with no lag reported the
     * controller settles to no change and re-renders nothing, so a test built
     * on its cycle would show the same label whether or not the hold works.
     * </p>
     */
    function rerenderTiles() {
      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', durationMs: 1_000 });
      });
    }

    function renderBoundTile() {
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 })]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('first'));
      renderPage();
      return () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');
    }

    /**
     * **Induced, not observed.** With no lag report the tile has no age, no
     * hold engages, and an assertion that the label eventually appears passes
     * with the wiring cut — which is the whole failure mode this covers.
     */
    it("Withholds an updated label for as long as the tile's picture is old", () => {
      const label = renderBoundTile();
      expect(label()).toBe('first');

      // The tile reports a 120 ms-old picture.
      act(() => {
        reportLag?.('cam-a', 120, 40);
      });

      // The label changes on the next render the controller drives. The hold
      // is scheduled at that instant, so it is due beyond this window.
      getOverlayMock.mockReturnValue(publishedOverlay('second'));
      rerenderTiles();

      expect(label(), 'held while the picture it describes is still arriving').toBe('first');

      act(() => {
        vi.advanceTimersByTime(120);
      });
      expect(label()).toBe('second');
    });

    /**
     * FR-011. The same render path and the same label change — only the
     * measurement is missing, and that is the whole difference. A tile with
     * no age has nothing to match, so its label is shown at once.
     */
    it('Shows an updated label at once when the tile has reported no lag', () => {
      const label = renderBoundTile();
      expect(label()).toBe('first');

      getOverlayMock.mockReturnValue(publishedOverlay('second'));
      rerenderTiles();

      expect(label()).toBe('second');
    });
  });

  /**
   * Spec 063 T003 (#2012) — the frontend hop nothing had ever executed.
   *
   * <p>
   * The push travels `useLayoutLifecycle` → this page's
   * `onResolvedOverlayTextChanged` → `upsertQueryData` → the tile's snapshot
   * query → `useLabelDelay` → the label. Every hop of that was plausible on
   * reading and none of it had ever been run: this file's other tests never
   * mention resolved text, and `useLayoutLifecycle.test.tsx` proves only that
   * the hook forwards its callback.
   * </p>
   *
   * <p>
   * <b>Expected green, declared in advance.</b> This covers a path believed
   * correct, not new behaviour — it is not the red artifact #2012's server-side
   * defect owes, and must not be read as one. Its value is that it makes the
   * belief checkable: if it is red, there is a second, independent defect in
   * front of the one being fixed.
   * </p>
   *
   * <p>
   * <b>What it does not prove.</b> Anything about a real frame, a real hub,
   * real video or a real clock — all stubbed in jsdom. The snapshot hook here
   * reads the real RTK cache rather than a canned value, so the dispatch is
   * genuinely exercised, but RTK's own `useQuery` subscription is not.
   * </p>
   */
  describe('A pushed resolved text reaching the tile that binds the overlay (#2012)', () => {
    /**
     * The one substitution that would make this test vacuous is a snapshot
     * hook that returns a canned value: it would assert the mock, not the
     * dispatch. This reads the same store the page dispatches into.
     */
    function useSnapshotFromTheRealCache(overlayIdentifier: string) {
      return useSelector(systemVariablesApi.endpoints.getOverlaySnapshot.select(overlayIdentifier));
    }

    it('Renders the text a ResolvedOverlayTextChanged frame carries, without a re-fetch', async () => {
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 })]),
      );
      // The `{{…}}` is load-bearing: without a placeholder the page's
      // `hasPlaceholder` gate skips the snapshot query outright.
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData('getOverlaySnapshot', 'ovl-x', {
            overlayIdentifier: 'ovl-x',
            resolvedText: 'OEE 41.0',
            version: 1,
          }),
        );
      });

      renderPage();
      const label = () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');
      expect(label()).toBe('OEE 41.0');

      await act(async () => {
        capturedCallbacks?.onResolvedOverlayTextChanged?.({
          overlay: 'ovl-x',
          resolvedText: 'OEE 82.5',
          version: 2,
        });
      });

      expect(label(), 'the tile kept its old text after a higher-versioned push').toBe('OEE 82.5');
    });
  });
});
