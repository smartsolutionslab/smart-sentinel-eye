import {
  getOverlayMock,
  capturedCallbacks,
  getSnapshotMock,
  measureOverlayDrawMock,
  reportLagByCamera,
  renderCountByCamera,
  tile,
  chain,
  publishedRevision,
  mockLayout,
  renderPage,
  publishedOverlay,
  publishedOverlayWithLabels,
  resilienceLines,
  spyOnConsoleInfo,
  pushText,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, screen, within } from '@testing-library/react';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { store } from '../../app/store.js';

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

    /**
     * Spec 297 (issue #2635) T009, US2, FR-005/FR-010. RED: today's
     * `LiveUpdatesBadge.tsx:15` className is `border border-accent-warning/40
     * bg-accent-warning/15` — a translucent, bordered call-site-alpha fill
     * over live video (ADR-0146 violation, spec §1). After the conversion it
     * must be the shared Badge's warning tone, with no alpha modifier and no
     * border.
     */
    it('Shows the degraded badge with the Badge warning tone, no alpha modifier and no border', () => {
      mockLayout(chain());
      renderPage();

      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });

      const badge = screen.getByTestId('live-updates-degraded');
      expect(badge.className).toContain('bg-accent-warning-subtle');
      const offending = badge.className.split(/\s+/).filter((token) => /\/\d+$|^border/.test(token));
      expect(offending).toEqual([]);
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
              elements: [
                {
                  kind: 'Text',
                  color: '#FFFFFFD9',
                  text: 'Old text',
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
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1000 });
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
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1000 });
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
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1000 });
      });
      // A second highlight lands 500ms in with a fresh 1000ms duration.
      act(() => {
        vi.advanceTimersByTime(500);
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1000 });
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
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-absent', fab: 'munich', durationMs: 1000 });
      });

      expect(highlightedTiles()).toHaveLength(0);
    });

    it('A wall-clock step does not expire an active highlight early', () => {
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 })]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1000 });
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
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1000 });
      });
      view.unmount();

      expect(vi.getTimerCount()).toBe(0);
    });
  });

  /**
   * Spec 204 US1 (issue #2303). `useWallAlignment` publishes `skewMilliseconds`
   * every settle cycle and nothing on the wall reads it — but `setSkew`'s write
   * is unguarded, so a live, essentially-never-repeating float re-renders every
   * tile on the wall, every two seconds, forever, on constitution §IV's
   * `Overlay composite + render ≤ 50 ms` leg (spec.md).
   */
  describe('A wall does no work when nothing changed (spec 204 SC-1)', () => {
    const CYCLE_MS = 2_000;

    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    function cycle(times = 1) {
      act(() => {
        vi.advanceTimersByTime(CYCLE_MS * times);
      });
    }

    /**
     * SC-1, the core defect proof.
     *
     * <p>
     * <b>The jitter is load-bearing (spec.md Correction 1).</b> A round-number
     * spread repeats exactly from cycle to cycle, so React's `Object.is`
     * bail-out on `setSkew`'s write hides the defect outright — the spec
     * measured zero tile renders from a constant 10 ms spread over 5 cycles,
     * even on this unpatched code. Only a genuinely-changing float reproduces
     * it, because that is what a real `jitterBufferDelay` sample looks like.
     * The wall is converged first at exactly the jitter loop's own cycle-0
     * values, so the loop's first cycle reproduces the same skew it converged
     * on and must not re-render on that repeat either.
     * </p>
     */
    it('Re-renders no tile across five settle cycles of a converged, jittering wall', () => {
      mockLayout(
        publishedRevision(1, 2, [
          tile({ cameraIdentifier: 'cam-a', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        ]),
      );
      renderPage();

      act(() => {
        reportLagByCamera.get('cam-a')?.('cam-a', 100, 60);
        reportLagByCamera.get('cam-b')?.('cam-b', 110, 65);
      });
      cycle();

      const before = {
        a: renderCountByCamera.get('cam-a') ?? 0,
        b: renderCountByCamera.get('cam-b') ?? 0,
      };

      for (let round = 0; round < 5; round += 1) {
        act(() => {
          reportLagByCamera.get('cam-a')?.('cam-a', 100 + round * 0.31, 60);
          reportLagByCamera.get('cam-b')?.('cam-b', 110 + round * 0.73, 65);
        });
        cycle();
      }

      const after = {
        a: renderCountByCamera.get('cam-a') ?? 0,
        b: renderCountByCamera.get('cam-b') ?? 0,
      };

      expect(after.a - before.a + (after.b - before.b), 'no tile re-rendered across five silent settle cycles').toBe(0);
    });
  });

  /**
   * Spec 229 US1 (#2321) — safety net for the two handlers this suite never
   * fired directly, ahead of moving them into `useOverlayHubHandlers`.
   *
   * <p>
   * "Flags a tile bound to an overlay archived before the kiosk loaded
   * (FR-009)" above covers a *fetched* overlay with no Published revision —
   * the archive already happened before this kiosk ever queried it. Nothing
   * in this file has ever fired a *pushed* `OverlayRevisionArchived` or
   * `OverlayRevisionPublished` frame through `capturedCallbacks`. These two
   * cases characterise what `onOverlayArchived`/`onOverlayPublished` do
   * today, so a later move of that code can be checked against it.
   * </p>
   */
  describe('Pushed overlay archive and publish frames (spec 229 US1, #2321)', () => {
    afterEach(() => {
      store.dispatch(systemVariablesApi.util.resetApiState());
      store.dispatch(overlaysApi.util.resetApiState());
    });

    /** A munich wall with two tiles bound to two different overlays. */
    function aTwoTileWall() {
      mockLayout(
        publishedRevision(1, 2, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ov-1', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', overlayIdentifier: 'ov-2', row: 0, col: 1 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('Line label'));
      renderPage();
    }

    function unavailableBadgeIn(tileEl: HTMLElement) {
      return within(tileEl).queryByText(/overlay unavailable/i);
    }

    it('Flags only the tile bound to the overlay a pushed OverlayRevisionArchived names', () => {
      aTwoTileWall();
      const [tileA, tileB] = screen.getAllByTestId('layout-tile');
      expect(unavailableBadgeIn(tileA!), 'not flagged before the push').not.toBeInTheDocument();

      act(() => {
        capturedCallbacks?.onOverlayArchived?.({
          overlay: 'ov-1',
          revisionNumber: 2,
          archivedAt: '2026-09-23T10:00:00Z',
        });
      });

      expect(unavailableBadgeIn(tileA!), 'the tile bound to the archived overlay').toBeInTheDocument();
      expect(unavailableBadgeIn(tileB!), 'a tile bound to a different overlay').not.toBeInTheDocument();
    });

    /**
     * Spec 297 (issue #2635) T009, US2, FR-005/FR-010. RED: today's
     * *Overlay unavailable* chip (`LayoutGrid.tsx:443-450`) is
     * `bg-accent-warning/30` — a translucent call-site alpha fill over live
     * video (ADR-0146 violation, spec §1). After the conversion it must be
     * the shared Badge's warning tone, with no alpha modifier.
     */
    it('Flags the archived-overlay tile with the Badge warning tone and no alpha modifier', () => {
      aTwoTileWall();
      const [tileA] = screen.getAllByTestId('layout-tile');

      act(() => {
        capturedCallbacks?.onOverlayArchived?.({
          overlay: 'ov-1',
          revisionNumber: 2,
          archivedAt: '2026-09-23T10:00:00Z',
        });
      });

      const badge = unavailableBadgeIn(tileA!)!;
      expect(badge.className).toContain('bg-accent-warning-subtle');
      const offending = badge.className.split(/\s+/).filter((token) => /\/\d+$/.test(token));
      expect(offending).toEqual([]);
    });

    it('Clears a pushed-archive flag and invalidates the Overlay and OverlaySnapshot caches when OverlayRevisionPublished follows', () => {
      const dispatchSpy = vi.spyOn(store, 'dispatch');
      aTwoTileWall();

      act(() => {
        capturedCallbacks?.onOverlayArchived?.({
          overlay: 'ov-1',
          revisionNumber: 2,
          archivedAt: '2026-09-23T10:00:00Z',
        });
      });
      const [tileA] = screen.getAllByTestId('layout-tile');
      expect(unavailableBadgeIn(tileA!), 'flagged by the archive push').toBeInTheDocument();

      act(() => {
        capturedCallbacks?.onOverlayPublished?.({
          overlay: 'ov-1',
          revisionNumber: 3,
          name: 'Line label',
          elements: [
            {
              kind: 'Text',
              color: '#FFFFFFD9',
              text: 'Line label',
              normalizedX: 0.5,
              normalizedY: 0.05,
              normalizedWidth: 0.3,
              normalizedHeight: 0.08,
              fontSizePx: 48,
            },
          ],
          publishedAt: '2026-09-23T10:05:00Z',
        });
      });

      expect(unavailableBadgeIn(tileA!), 'a later publish clears the flag').not.toBeInTheDocument();

      const dispatched: unknown[] = dispatchSpy.mock.calls.map(([action]) => action);
      const payloadsOf = (creator: { match: (action: unknown) => boolean }) =>
        dispatched.filter((action) => creator.match(action)).map((action) => (action as { payload: unknown }).payload);

      expect(
        payloadsOf(overlaysApi.util.invalidateTags),
        'invalidates the published overlay in the Overlay cache',
      ).toContainEqual([{ type: 'Overlay', id: 'ov-1' }]);
      expect(
        payloadsOf(systemVariablesApi.util.invalidateTags),
        'invalidates the published overlay in the OverlaySnapshot cache',
      ).toContainEqual([{ type: 'OverlaySnapshot', id: 'ov-1' }]);

      dispatchSpy.mockRestore();
    });
  });

  /**
   * Spec 150 (#2345) T020, RED. `Tile`'s `hasPlaceholder` and
   * `measureOverlayDraw` keying both widen from one label to the whole set —
   * get either wrong and it compiles and only misbehaves at runtime.
   */
  describe('The label set as a unit (spec 150)', () => {
    beforeEach(() => {
      measureOverlayDrawMock.mockClear();
    });

    /**
     * A placeholder-gated snapshot fetch keyed on only the first label (the
     * shape a `publishedOverlay?.text?.includes('{{')` carry-over would have)
     * never fetches for an overlay whose placeholder sits in a later label.
     */
    it('Still asks for the opening snapshot when the placeholder is in the third label, not the first', () => {
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-third-placeholder', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlayWithLabels(['Station A', 'Line 1', 'OEE {{oeeline1}}']));

      renderPage();

      expect(getSnapshotMock).toHaveBeenCalledWith(
        { overlayIdentifier: 'ovl-third-placeholder', fabId: 'munich' },
        { skip: false },
      );
    });

    /**
     * `measureOverlayDraw` must fire once for a real change to the label set
     * and must NOT fire again for a render that changed nothing about it — a
     * render triggered by, say, the hub's own connection-state transition.
     * Keying the effect on the label array itself (a fresh reference every
     * render) would fire on every one of these for free, flooding the
     * `overlay_draw` instrument with no-op samples (#1888/#1889, ADR-0123).
     */
    it('Fires the draw measurement once per real label-set change, not once per render', () => {
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-draw-key', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlayWithLabels(['Station A', 'Line 1']));

      renderPage();
      const callsAfterMount = measureOverlayDrawMock.mock.calls.length;
      expect(callsAfterMount, 'at least the initial paint is measured').toBeGreaterThan(0);

      // A render that touches nothing about this tile's label set: the hub's
      // own connection-state transition re-renders the whole page.
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });

      expect(
        measureOverlayDrawMock.mock.calls.length,
        'two state-change re-renders changed nothing about the label set',
      ).toBe(callsAfterMount);

      // A real change to the label set — the second label's text differs.
      // Re-uses the same hub-driven re-render trigger as above (this file's
      // `getOverlayMock` is a plain double with no Redux cache of its own, so
      // changing its return value needs an actual re-render to be read).
      getOverlayMock.mockReturnValue(publishedOverlayWithLabels(['Station A', 'Line 2']));
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });

      expect(measureOverlayDrawMock.mock.calls.length, 'exactly one more measurement for the one real change').toBe(
        callsAfterMount + 1,
      );
    });

    /**
     * Spec 300 (#2349) FR-018, new behaviour, RED (ADR-0139) — the silent-break
     * site tasks.md calls out for T020: `renderElementsKey` has to carry
     * `color`, not just `text`. A colour-only republish (the text is
     * byte-identical) must still re-measure `overlay_draw`, and an equal
     * re-render must still not — exactly the counterfactual pair the sibling
     * test above proves for text.
     */
    it('Fires the draw measurement once for a colour-only republish, and not for an equal re-render', () => {
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-draw-color', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlayWithLabels(['Station A']));

      renderPage();
      const callsAfterMount = measureOverlayDrawMock.mock.calls.length;
      expect(callsAfterMount, 'at least the initial paint is measured').toBeGreaterThan(0);

      // An equal re-render: nothing about kind/color/text changed.
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });
      expect(
        measureOverlayDrawMock.mock.calls.length,
        'two state-change re-renders changed nothing about the element set',
      ).toBe(callsAfterMount);

      // A colour-only republish: the text is unchanged, only the colour
      // differs.
      const recoloured = publishedOverlayWithLabels(['Station A']);
      recoloured.data.revisions[0]!.elements[0]!.color = '#D32F2FFF';
      getOverlayMock.mockReturnValue(recoloured);
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });

      expect(measureOverlayDrawMock.mock.calls.length, 'exactly one more measurement for the colour-only change').toBe(
        callsAfterMount + 1,
      );
    });

    /**
     * Issue #2714, new behaviour, RED (ADR-0139) — `renderElementsKey` is
     * built from `kind|color|text` alone, so a republish that only moves or
     * resizes an element (nothing but geometry changes) leaves the key
     * byte-identical and `measureOverlayDraw` never re-fires, even though
     * what's painted changed. Mirrors the colour-only sibling test above,
     * including its equal-re-render half, so a fix that overcorrects into
     * firing on every no-op re-render is caught too.
     */
    it('Fires the draw measurement once for a move-only republish, and not for an equal re-render', () => {
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-draw-move', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlayWithLabels(['Station A']));

      renderPage();
      const callsAfterMount = measureOverlayDrawMock.mock.calls.length;
      expect(callsAfterMount, 'at least the initial paint is measured').toBeGreaterThan(0);

      // An equal re-render: nothing about kind/color/text/geometry changed.
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });
      expect(
        measureOverlayDrawMock.mock.calls.length,
        'two state-change re-renders changed nothing about the element set',
      ).toBe(callsAfterMount);

      // A move-only republish: kind/color/text are unchanged, only the
      // normalized position differs.
      const moved = publishedOverlayWithLabels(['Station A']);
      moved.data.revisions[0]!.elements[0]!.normalizedX = 0.4;
      moved.data.revisions[0]!.elements[0]!.normalizedY = 0.4;
      getOverlayMock.mockReturnValue(moved);
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });

      expect(measureOverlayDrawMock.mock.calls.length, 'exactly one more measurement for the move-only change').toBe(
        callsAfterMount + 1,
      );
    });

    /**
     * Issue #2714, new behaviour, RED (ADR-0139) — same gap as the move-only
     * test above, but for `fontSizePx`: a republish that only changes a
     * label's font size changes what's painted (the text renders larger or
     * smaller) but `renderElementsKey` has no `fontSizePx` component at all,
     * so the key is byte-identical and the measurement never re-fires.
     */
    it('Fires the draw measurement once for a font-size-only republish, and not for an equal re-render', () => {
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-draw-font-size', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlayWithLabels(['Station A']));

      renderPage();
      const callsAfterMount = measureOverlayDrawMock.mock.calls.length;
      expect(callsAfterMount, 'at least the initial paint is measured').toBeGreaterThan(0);

      // An equal re-render: nothing about kind/color/text/fontSizePx changed.
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });
      expect(
        measureOverlayDrawMock.mock.calls.length,
        'two state-change re-renders changed nothing about the element set',
      ).toBe(callsAfterMount);

      // A font-size-only republish: kind/color/text are unchanged, only the
      // rendered font size differs.
      const resized = publishedOverlayWithLabels(['Station A']);
      resized.data.revisions[0]!.elements[0]!.fontSizePx = 72;
      getOverlayMock.mockReturnValue(resized);
      act(() => {
        capturedCallbacks?.onStateChange?.('degraded');
      });
      act(() => {
        capturedCallbacks?.onStateChange?.('connected');
      });

      expect(
        measureOverlayDrawMock.mock.calls.length,
        'exactly one more measurement for the font-size-only change',
      ).toBe(callsAfterMount + 1);
    });

    /**
     * Phase-6 regression (#2345 v2 revision). The multi-label rewrite of
     * `hasPlaceholder` dropped the `publishedOverlay?.text?.includes` guard's
     * second `?.` — the one defending against spec 141 site 3's wire-drift
     * case, where a label's `text` is absent or renamed. `label.text` is
     * typed `string` in `OverlayLabel`, but that type is exactly what a
     * wire-shape mismatch bypasses, so a non-string `text` on any label must
     * not throw out of `render()` — it must leave this tile's placeholder
     * verdict unknown, exactly as `labelTextKnown` (line 401) already claims
     * to handle, and exactly like a not-yet-resolved overlay query (see
     * "Reports the disagreement even when the push loses the version race"
     * above): the push is answered, but no verdict is ever recorded to
     * disagree with it.
     */
    it('Renders a label set with a non-string text without throwing, and records no verdict for it', async () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-text-drift', row: 0, col: 0 }),
        ]),
      );
      const overlay = publishedOverlayWithLabels(['Station A', 'Line 1']);
      const elements = overlay.data.revisions[0]!.elements;
      // Models the wire-drift case itself: the cast is the only way to
      // construct the shape an absent/renamed `text` field would actually
      // produce, exactly as the omitted-`overlayIdentifier` fixture does for
      // site 1 (`CellPage.test.tsx:1667`).
      elements[1] = { ...elements[1]!, text: undefined as unknown as string };
      getOverlayMock.mockReturnValue(overlay);

      renderPage();

      await pushText({
        overlay: 'ovl-text-drift',
        fab: 'munich',
        texts: [
          { template: 'Station A', resolved: 'Station A' },
          { template: 'Line 1', resolved: 'Line 1' },
        ],
        version: 2,
      });

      expect(
        resilienceLines(info.mock.calls, 'resolved-text-for-static-label'),
        'labelTextKnown stayed false for the drifted label, so no verdict was ever recorded to disagree with the push',
      ).toEqual([]);

      info.mockRestore();
    });
  });
});
