import {
  getOverlayMock,
  capturedCallbacks,
  getSnapshotMock,
  useSnapshotFromTheRealCache,
  tile,
  publishedRevision,
  mockLayout,
  renderPage,
  publishedOverlay,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { act, screen } from '@testing-library/react';
import type { ResolvedOverlayTextChangedMessage } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { store } from '../../app/store.js';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

  /**
   * Spec 067 (#2069) — a wall shows one fab.
   *
   * <p>
   * An overlay is a fab-neutral template (ADR-0115), so the same overlay
   * identifier is legitimately pushed by every fab, and a principal holding two
   * fabs joins two hub groups and receives both plants' frames. That is correct
   * on the server and stays correct: the integration tests assert the arrival,
   * not a silence. What was missing is the wall's half of it — nothing here
   * ever asked whose frame it was, so a munich wall applied dresden's
   * production figure.
   * </p>
   *
   * <p>
   * <b>This is the only level at which the refusal is asserted.</b> No
   * server-side test can show a tile declining to move. Four of the five cases
   * below are red today — the page applies every frame it is handed, and asks
   * for its opening snapshot without naming a fab. The fifth is the control:
   * without it the other four would pass equally well against a page that
   * applied nothing at all.
   * </p>
   */
  describe("Applying only the wall's own fab (#2069)", () => {
    afterEach(() => {
      store.dispatch(systemVariablesApi.util.resetApiState());
    });

    /**
     * A munich wall with one tile bound to `overlay`, a placeholder label, and
     * a resolved snapshot already in the cache — the state a wall sits in
     * between pushes.
     *
     * <p>
     * <b>Every case here passes its own overlay identifier, and that is
     * load-bearing.</b> `store` is the app singleton this whole file shares, so
     * a cache key is reused across tests; a second `upsertQueryData` against a
     * key an earlier test already drove is silently dropped, and the label then
     * fails to move for a reason that has nothing to do with the fab. Written
     * with one shared `'ovl-x'`, the first case here passed *vacuously* and the
     * control failed — which is exactly backwards, and would have been read as
     * a red.
     * </p>
     */
    async function aMunichWallShowing(overlay: string, text: string) {
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: overlay, row: 0, col: 0 })]),
      );
      // The `{{…}}` is load-bearing: without a placeholder the page's
      // `hasPlaceholder` gate skips the snapshot query and no push would show.
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            // The munich wall's own cache key, matching what the page queries
            // with (spec 067, plan.md Risk 1).
            { overlayIdentifier: overlay, fabId: 'munich' },
            {
              overlayIdentifier: overlay,
              texts: [{ template: 'OEE {{oeeline1}}', resolved: text }],
              version: 1,
            },
          ),
        );
      });

      renderPage();
      return () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');
    }

    /**
     * How long an accepted frame is given to reach the label.
     *
     * <p>
     * `upsertQueryData` is a thunk the page dispatches without awaiting, and the
     * re-render it causes is notified on a later task still — measured at
     * roughly a millisecond here, and reliably longer than a fixed count of
     * microtask turns. Two microtask flushes were enough for the first case in
     * this block and not for the last, which is the shape of a test that passes
     * for its position in the file rather than for its behaviour.
     * </p>
     *
     * <p>
     * <b>If this is ever too short, the control below fails</b> — loudly, and
     * before any red can pass quietly. That is the whole reason the control is
     * in the same block and uses the same helper.
     * </p>
     */
    const settleMilliseconds = 100;

    /** Fires one frame and lets the cache write and re-render it causes settle. */
    async function push(message: ResolvedOverlayTextChangedMessage) {
      await act(async () => {
        capturedCallbacks?.onResolvedOverlayTextChanged?.(message);
        await new Promise((resolve) => setTimeout(resolve, settleMilliseconds));
      });
    }

    /** RED. US2 scenario 1 — the defect, stated as one plant's figure on another's wall. */
    it("Ignores a resolved-text frame belonging to another plant's fab", async () => {
      const label = await aMunichWallShowing('ovl-foreign-text', 'OEE 41.0');
      expect(label()).toBe('OEE 41.0');

      await push({
        overlay: 'ovl-foreign-text',
        fab: 'dresden',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
        version: 2,
      });

      expect(label(), "a munich wall showed dresden's production figure").toBe('OEE 41.0');
    });

    /**
     * RED. FR-005, and **the only assertion in the suite that fails if the fab
     * test is placed after the version guard rather than before it.**
     *
     * <p>
     * That ordering bug does not show as a wrong label. It shows as silence: a
     * foreign frame at version 5 moves the mark, and the wall's own next
     * update — legitimately lower — is discarded for the rest of the session.
     * A frozen wall looks like a working wall to anyone not changing a value,
     * which is why this case fires the higher version first and asserts on the
     * lower one afterwards. It must not be weakened or merged into the case
     * above.
     * </p>
     */
    it("Does not let another fab's frame advance the version mark", async () => {
      const label = await aMunichWallShowing('ovl-version-mark', 'OEE 41.0');
      expect(label()).toBe('OEE 41.0');

      await push({
        overlay: 'ovl-version-mark',
        fab: 'dresden',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
        version: 5,
      });
      await push({
        overlay: 'ovl-version-mark',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 82.5' }],
        version: 2,
      });

      expect(label(), "a dresden frame moved munich's version mark, so munich's own update was dropped").toBe(
        'OEE 82.5',
      );
    });

    /** RED. US2 scenario 2 — the highlight half, which travels its own route. */
    it("Ignores a highlight frame belonging to another plant's fab", () => {
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-foreign-highlight', row: 0, col: 0 }),
        ]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({
          overlay: 'ovl-foreign-highlight',
          fab: 'dresden',
          durationMs: 1000,
        });
      });

      expect(
        screen.getAllByTestId('layout-tile').map((el) => el.dataset.highlighted),
        'a munich tile lit for a rule that fired in dresden',
      ).toEqual(['false']);
    });

    /**
     * RED. US1 — the opening label, which no push is involved in at all.
     *
     * <p>
     * `GET /system-variables/-/snapshot` has always been able to resolve in a
     * named fab; what it was never told is which one. This asserts the argument
     * the page hands the query, because that argument is also its RTK Query
     * cache key (plan.md, Risk 1) — and a cache key that disagrees with the
     * push's `upsertQueryData` leaves the tile quiet rather than wrong.
     * </p>
     */
    it("Asks for the opening snapshot in the wall's own fab", () => {
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-opening', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      renderPage();

      expect(getSnapshotMock.mock.calls[0]?.[0]).toEqual({ overlayIdentifier: 'ovl-opening', fabId: 'munich' });
    });

    /**
     * CONTROL — green today, and it must still be green afterwards. A filter
     * that drops everything satisfies all four cases above and freezes every
     * wall in the fab (plan.md, Risk 3). This is what tells the two apart.
     */
    it("Still applies a frame carrying the wall's own fab", async () => {
      const label = await aMunichWallShowing('ovl-own-fab', 'OEE 41.0');
      expect(label()).toBe('OEE 41.0');

      await push({
        overlay: 'ovl-own-fab',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 82.5' }],
        version: 2,
      });

      expect(label(), "the wall stopped applying its own plant's frames").toBe('OEE 82.5');
    });
  });
});
