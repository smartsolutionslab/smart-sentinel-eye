import {
  getOverlayMock,
  capturedCallbacks,
  getSnapshotMock,
  tile,
  publishedRevision,
  mockLayout,
  renderPage,
  publishedOverlay,
  resilienceLines,
  spyOnConsoleInfo,
  pushText,
  textFrameWithoutFab,
  layoutWithFab,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, screen } from '@testing-library/react';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { store } from '../../app/store.js';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

  /**
   * Spec 141 site 1 — an omitted `overlayIdentifier` is `undefined`, not
   * `null`, and `undefined !== null` lets `useGetOverlayQuery('')` fire. The
   * route table proves that request lands as 200 with the *collection*
   * payload (`OverlayEndpoints.cs:59`'s `List`; `GetOne`'s `:guid` constraint
   * cannot match an empty segment, and both kiosk realm clients carry
   * `sse.overlays.read`), so `overlay.revisions` is `undefined` and `.find`
   * throws out of `render()`. There is no `ErrorBoundary` in this test tree,
   * so the defect cases below are expected to fail on that thrown
   * `TypeError` itself, not on a clean assertion mismatch — that throw is
   * the settled answer to #2197's open question (spec.md §"Site 1").
   */
  describe('An omitted overlayIdentifier says so instead of asking for the overlay collection (spec 141 site 1)', () => {
    // Each case spies on `console.info` fresh; without restoring, `vi.spyOn`
    // on an already-spied method returns the same spy and its call history
    // leaks into the next case (found running the T005-T007 mutation proof,
    // where the defect cases' lines bled into the quiet ones).
    afterEach(() => {
      vi.restoreAllMocks();
    });

    it('Reports the omitted field and never asks for the overlay collection', () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 1, [
          // The cast models the drift itself: `LayoutTile` deliberately does
          // not admit `undefined` (spec FR-001) — this is the only way to
          // construct the shape a field made optional would actually
          // produce.
          { ...tile(), overlayIdentifier: undefined as unknown as string | null },
        ]),
      );

      renderPage();

      expect(
        getOverlayMock.mock.calls.every(([, options]) => options?.skip === true),
        'an omitted overlayIdentifier must not fire the overlay query',
      ).toBe(true);
      expect(resilienceLines(info.mock.calls, 'tile-without-overlay-identifier')).toEqual([
        { subsystem: 'hub', transition: 'tile-without-overlay-identifier', layout: 'cam-1', tiles: 1 },
      ]);
    });

    it('Keeps matching pushes for a real overlay when a sibling tile omits its identifier (mixed layout)', () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 2, [
          {
            ...tile({ cameraIdentifier: 'cam-a', row: 0, col: 0 }),
            overlayIdentifier: undefined as unknown as string | null,
          },
          tile({ cameraIdentifier: 'cam-b', overlayIdentifier: 'ovl-real', row: 0, col: 1 }),
        ]),
      );

      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-real', fab: 'munich', durationMs: 1000 });
      });

      expect(screen.getAllByTestId('layout-tile').map((el) => el.dataset.highlighted)).toEqual(['false', 'true']);
      expect(resilienceLines(info.mock.calls, 'tile-without-overlay-identifier')).toEqual([
        { subsystem: 'hub', transition: 'tile-without-overlay-identifier', layout: 'cam-1', tiles: 1 },
      ]);
    });

    it('Reports an overlayIdentifier of empty string the same way as an omitted one', () => {
      const info = spyOnConsoleInfo();
      mockLayout(publishedRevision(1, 1, [tile({ overlayIdentifier: '' })]));

      renderPage();

      expect(
        getOverlayMock.mock.calls.every(([, options]) => options?.skip === true),
        'an empty-string overlayIdentifier must not fire the overlay query either',
      ).toBe(true);
      expect(resilienceLines(info.mock.calls, 'tile-without-overlay-identifier')).toEqual([
        { subsystem: 'hub', transition: 'tile-without-overlay-identifier', layout: 'cam-1', tiles: 1 },
      ]);
    });

    it("QUIET — a null overlayIdentifier is the healthy shape today's server sends, and reports nothing", () => {
      const info = spyOnConsoleInfo();
      mockLayout(publishedRevision(1, 1, [tile({ overlayIdentifier: null })]));

      renderPage();

      expect(resilienceLines(info.mock.calls, 'tile-without-overlay-identifier')).toEqual([]);
      expect(getOverlayMock, 'the tile must have rendered and evaluated the guard').toHaveBeenCalledWith('', {
        skip: true,
      });
    });

    it('QUIET — a bound overlayIdentifier still queries, and reports nothing', () => {
      const info = spyOnConsoleInfo();
      mockLayout(publishedRevision(1, 1, [tile({ overlayIdentifier: 'ovl-real' })]));

      renderPage();

      expect(resilienceLines(info.mock.calls, 'tile-without-overlay-identifier')).toEqual([]);
      expect(getOverlayMock).toHaveBeenCalledWith('ovl-real', { skip: false });
    });
  });

  /**
   * Spec 141 site 2 — `fab === ''` catches an empty string but not an absent
   * field: `data?.fab` is `undefined` when the layout carries no fab at all,
   * so the snapshot query fires across every fab the server holds (the
   * cross-fab request #2069 exists to prevent) and `countReportableSkew`'s
   * own `wallFab === undefined` early return (`:518`) goes silent for the
   * whole session — the reporter #2084 built to notice a frozen wall is
   * switched off by the same absence. `countReportableSkew` is not touched;
   * this reports the blind spot it leaves, at the wall rather than the
   * frame.
   */
  describe('A layout with no fab says so before querying across fabs (spec 141 site 2)', () => {
    afterEach(() => {
      store.dispatch(systemVariablesApi.util.resetApiState());
      // Restores the console.info spy — see the note on the site 1 describe.
      vi.restoreAllMocks();
    });

    it('Skips the cross-fab snapshot query when the fab is absent, and says so', () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        layoutWithFab(undefined, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-1', row: 0, col: 0 })]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      renderPage();

      expect(getSnapshotMock.mock.calls[0]?.[1], 'no request without a fabId').toEqual({ skip: true });
      expect(resilienceLines(info.mock.calls, 'layout-without-fab')).toEqual([
        { subsystem: 'hub', transition: 'layout-without-fab', layout: 'cam-1' },
      ]);
    });

    it('Skips the cross-fab snapshot query for an empty-string fab too, and says so', () => {
      const info = spyOnConsoleInfo();
      mockLayout(layoutWithFab('', [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-1', row: 0, col: 0 })]));
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      renderPage();

      expect(getSnapshotMock.mock.calls[0]?.[1], 'already skipped today — only the report is new').toEqual({
        skip: true,
      });
      expect(resilienceLines(info.mock.calls, 'layout-without-fab')).toEqual([
        { subsystem: 'hub', transition: 'layout-without-fab', layout: 'cam-1' },
      ]);
    });

    /**
     * The spec's own Gherkin for this case also expected the frame to be
     * "dropped, exactly as today" — checked against the real cache and found
     * not to hold: `message.fab !== wallFab` was `undefined !== undefined`,
     * which is `false`, so the (then-unchanged, plan invariant 4) fab filter
     * let a fab-less frame through when the wall's own fab was *also*
     * absent. That was a pre-existing hole in the equality filter itself,
     * orthogonal to FR-004 and out of spec 141's scope — so this case
     * asserts only what FR-004 actually promised: the old reporter stays
     * silent and the new one fires. Spec 198 (#2320) has since closed that
     * hole at both guard sites; this test's assertions are unaffected
     * because they only ever covered the reporters, never the cache write.
     */
    it("Leaves countReportableSkew's blind spot untouched, and reports the layout fault instead", async () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        layoutWithFab(undefined, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-blind', row: 0, col: 0 })]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      renderPage();

      await pushText(textFrameWithoutFab('ovl-blind', 'OEE 99.9', 2));

      expect(
        resilienceLines(info.mock.calls, 'resolved-text-without-fab'),
        "countReportableSkew's wallFab === undefined early return is untouched",
      ).toEqual([]);
      expect(resilienceLines(info.mock.calls, 'layout-without-fab'), 'the wall is no longer wholly silent').toEqual([
        { subsystem: 'hub', transition: 'layout-without-fab', layout: 'cam-1' },
      ]);
    });

    it('QUIET — a wall with a named fab is not reported, and the guard let the query through', () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        layoutWithFab('munich', [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-1', row: 0, col: 0 })]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      renderPage();

      expect(resilienceLines(info.mock.calls, 'layout-without-fab')).toEqual([]);
      expect(getSnapshotMock).toHaveBeenCalledWith({ overlayIdentifier: 'ovl-1', fabId: 'munich' }, { skip: false });
    });
  });
});
