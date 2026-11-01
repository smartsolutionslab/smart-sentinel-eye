import {
  getOverlayMock,
  capturedCallbacks,
  reportLagByCamera,
  renderCountByCamera,
  tile,
  chain,
  publishedRevision,
  mockLayout,
  renderPage,
  publishedOverlay,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, screen, within } from '@testing-library/react';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { store } from '../../app/store.js';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

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
});
