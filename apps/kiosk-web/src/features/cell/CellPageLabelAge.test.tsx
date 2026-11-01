import {
  getOverlayMock,
  capturedCallbacks,
  getSnapshotMock,
  reportLag,
  reportLagByCamera,
  useSnapshotFromTheRealCache,
  tile,
  publishedRevision,
  mockLayout,
  renderPage,
  publishedOverlay,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, cleanup, screen } from '@testing-library/react';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { store } from '../../app/store.js';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

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
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1_000 });
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
   * Spec 204 US1 (issue #2303), SC-3/SC-4/SC-5. `CellPage.tsx:369` evaluates
   * `alignment.frameAgeFor(cell.key)` during *the page's* render and freezes
   * the result into a prop — so a tile's label hold is scheduled from
   * whatever age the parent last happened to render with, not the age most
   * recently measured. Label ageing (ADR-0129) rides on the wall's accidental
   * every-cycle re-render for its only refresh.
   */
  describe('A label is held for the age most recently reported (spec 204 SC-3/SC-4/SC-5)', () => {
    // Real timers, deliberately — not this file's usual `vi.useFakeTimers()`.
    // A direct RTK-cache dispatch's subscriber re-render (via `useSelector`)
    // does not flush synchronously under fake timers in this environment —
    // not even through an explicit `flushSync` — and needs a genuine, if
    // small, amount of *wall-clock* time before React commits it. These three
    // tests are the only ones in this file driving that path from outside a
    // React event, so they pay for their own real waits rather than fight the
    // fake-timer/scheduler interaction the rest of the file never exercises.
    let rafSpy: ReturnType<typeof vi.spyOn> | undefined;

    beforeEach(() => {
      // `measureOverlayDraw` (kioskLatency.ts) times the overlay-draw leg via
      // two *chained* `requestAnimationFrame` calls, scheduled from a Tile
      // effect outside any `act()` boundary. Left on jsdom's real (timer-based)
      // rAF polyfill, that chain is still in flight when this test's own
      // `act()` calls return, and it finishes — asynchronously — during
      // whichever test runs next, dragging a leftover state update in behind
      // it. Every other test in this file never drives a real text change
      // through the RTK cache outside a React event, so it never surfaces
      // this; run synchronously here instead of chasing it with more waits.
      rafSpy = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
        callback(0);
        return 0;
      });
    });

    afterEach(() => {
      rafSpy?.mockRestore();
      cleanup();
      store.dispatch(systemVariablesApi.util.resetApiState());
    });

    function sleep(milliseconds: number): Promise<void> {
      return new Promise((resolve) => setTimeout(resolve, milliseconds));
    }

    /**
     * SC-3. The text change goes through the RTK snapshot cache directly
     * (`useSnapshotFromTheRealCache` + `upsertQueryData`, the #2012 pattern
     * above) rather than through `onOverlayHighlightChanged`: that callback
     * sets page-level state and would re-render `CellPage` itself as a side
     * effect, refreshing `frameAgeMilliseconds` for free and making this pass
     * today regardless of the defect (plan.md §3, R1).
     * </p>
     */
    it('Holds a changed label for the age most recently reported, not the age its parent last rendered with', async () => {
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 2, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        ]),
      );
      // Restricted to 'ovl-x' — cam-b's tile binds no overlay and must stay
      // that way (`overlayDoubleDefault` matches this file's usual pattern for
      // a two-tile layout where only one tile binds an overlay).
      getOverlayMock.mockImplementation((overlayIdentifier: string) =>
        overlayIdentifier === 'ovl-x' ? publishedOverlay('OEE {{oeeline1}}') : { data: undefined },
      );
      const label = () => screen.getAllByTestId('camera-viewer')[0]!.getAttribute('data-overlay-text');

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            { overlayIdentifier: 'ovl-x', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-x',
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'first' }],
              version: 1,
            },
          ),
        );
      });
      renderPage();
      expect(label()).toBe('first');

      // Converge: both tiles settle at a 40 ms lag.
      act(() => {
        reportLagByCamera.get('cam-a')?.('cam-a', 40, 20);
        reportLagByCamera.get('cam-b')?.('cam-b', 40, 20);
      });
      await act(() => sleep(2_000));

      // A new lag arrives, but no settle cycle runs before the text changes —
      // the wall's own state is untouched, so any re-render has to come from
      // the tile's own subscription to the cache below.
      act(() => {
        reportLagByCamera.get('cam-a')?.('cam-a', 180, 20);
      });

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            { overlayIdentifier: 'ovl-x', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-x',
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'second' }],
              version: 2,
            },
          ),
        );
      });

      // Checked well short of, and well past, the 40 ms/180 ms boundary
      // (rather than exactly at either tick) so a small flush delay cannot be
      // mistaken for the assertion itself.
      await act(() => sleep(100));
      expect(label(), 'still withheld 100 ms after the change — the stale 40 ms age would already have fired').toBe(
        'first',
      );

      await act(() => sleep(120));
      expect(label(), 'shown by 220 ms after the change — the age actually measured (180 ms)').toBe('second');
    }, 10_000);

    /**
     * SC-4. The exact scenario spec.md's Correction 2 measured: a second lag
     * report that stays inside the ±33 ms deadband (`WALL_SKEW_BOUND_MS`), so
     * `setReleased`, `setHeld` and `setTarget` all have nothing to change.
     */
    it('Holds a changed label for the second reported age, inside the deadband, not the first', async () => {
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 2, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 }),
          tile({ cameraIdentifier: 'cam-b', row: 0, col: 1 }),
        ]),
      );
      getOverlayMock.mockImplementation((overlayIdentifier: string) =>
        overlayIdentifier === 'ovl-x' ? publishedOverlay('OEE {{oeeline1}}') : { data: undefined },
      );
      const label = () => screen.getAllByTestId('camera-viewer')[0]!.getAttribute('data-overlay-text');

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            { overlayIdentifier: 'ovl-x', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-x',
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'first' }],
              version: 1,
            },
          ),
        );
      });
      renderPage();
      expect(label()).toBe('first');

      // Converged: both tiles at 100 ms, spread 0.
      act(() => {
        reportLagByCamera.get('cam-a')?.('cam-a', 100, 60);
        reportLagByCamera.get('cam-b')?.('cam-b', 100, 60);
      });
      await act(() => sleep(2_000));

      // Second report, 30 ms on — inside WALL_SKEW_BOUND_MS (33 ms) — then a
      // settle cycle actually elapses this time (unlike SC-3 above).
      act(() => {
        reportLagByCamera.get('cam-a')?.('cam-a', 130, 60);
      });
      await act(() => sleep(2_000));

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            { overlayIdentifier: 'ovl-x', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-x',
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'second' }],
              version: 2,
            },
          ),
        );
      });

      // Checked between the two reported ages, then well past the later one.
      await act(() => sleep(115));
      expect(
        label(),
        'still withheld 115 ms after the change — the first report (100 ms) would already have fired',
      ).toBe('first');

      await act(() => sleep(85));
      expect(label(), 'shown by 200 ms after the change — the second, most recent report (130 ms)').toBe('second');
    }, 10_000);

    /**
     * SC-5. Below two tiles the settle interval is never created at all
     * (`useWallAlignment.ts:114,116-135`), so a 1×1 wall has never had even
     * the accidental crutch SC-3/SC-4 are about — a second, quieter instance
     * of the same design fault: the age is pushed from a parent that has no
     * reason to re-render.
     */
    it('Holds a changed label for its reported age on a 1x1 wall, which never settles', async () => {
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-x', row: 0, col: 0 })]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));
      const label = () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            { overlayIdentifier: 'ovl-x', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-x',
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'first' }],
              version: 1,
            },
          ),
        );
      });
      renderPage();
      expect(label()).toBe('first');

      act(() => {
        reportLag?.('cam-a', 150, 60);
      });

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            { overlayIdentifier: 'ovl-x', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-x',
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'second' }],
              version: 2,
            },
          ),
        );
      });

      await act(() => sleep(50));
      expect(label(), 'still withheld 50 ms after the change').toBe('first');

      await act(() => sleep(200));
      expect(label(), 'shown by 250 ms after the change — the 150 ms reported age').toBe('second');
    }, 5_000);
  });
});
