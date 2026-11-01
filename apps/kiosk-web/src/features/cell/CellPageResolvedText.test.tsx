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
  publishedOverlayWithBoxAndText,
  resilienceLines,
  spyOnConsoleInfo,
  pushText,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, screen } from '@testing-library/react';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { store } from '../../app/store.js';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

  /**
   * Spec 301 (#2720) US2, FR-005/FR-007. Was "...resolves index-aligned
   * (FR-009)" — spec 300's index alignment (a shape padding its own slot
   * with `""`) is retired with V2 (spec.md FR-005: "Shapes contribute
   * nothing"). `Tile` now looks the Text element's own resolved value up by
   * its raw template, never by position, so the Box needs no entry — not an
   * empty one — and cannot be mistaken for the Text element's slot.
   *
   * <p>
   * <b>Rewritten, not construction-only.</b> The old assertion
   * (`resolvedTexts: ['', 'resolved value']`, a V2-shape positional pairing)
   * encoded exactly the premise this spec retires; keeping it unmodified
   * would characterise a contract this feature deletes. Flagged in the PR
   * per the test-writer brief.
   * </p>
   */
  describe('A mixed [Box, Text] overlay resolves by template, not position (spec 301 FR-005/FR-007)', () => {
    afterEach(() => {
      store.dispatch(systemVariablesApi.util.resetApiState());
    });

    it('Shows the resolved text on the Text element by its own template and renders exactly one shape at its own geometry', async () => {
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-mixed', row: 0, col: 0 })]),
      );
      // The `{{…}}` is load-bearing: without a placeholder the page's
      // `hasPlaceholder` gate skips the snapshot query outright.
      getOverlayMock.mockReturnValue(publishedOverlayWithBoxAndText('OEE {{oeeline1}}'));

      await act(async () => {
        await store.dispatch(
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            { overlayIdentifier: 'ovl-mixed', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-mixed',
              // Template-keyed (spec 301 FR-005): one entry for the Text
              // element's own template, and NO entry for the Box — the Box
              // never had a template to resolve, and the old "" padding at
              // its index is exactly what FR-005 retires.
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'resolved value' }],
              version: 1,
            },
          ),
        );
      });

      renderPage();

      const elements = JSON.parse(
        screen.getByTestId('camera-viewer').getAttribute('data-overlay-elements') ?? '[]',
      ) as Array<{ kind: string; text?: string; normalizedX: number; normalizedY: number }>;

      expect(elements).toHaveLength(2);
      expect(elements[0], 'the Box must keep its own geometry, not borrow the Text element’s').toMatchObject({
        kind: 'Box',
        normalizedX: 0.6,
        normalizedY: 0.6,
      });
      expect(elements[1]).toMatchObject({
        kind: 'Text',
        text: 'resolved value',
        normalizedX: 0.1,
        normalizedY: 0.05,
      });
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
    afterEach(() => {
      // `store` is the imported app singleton, so an upserted cache entry
      // outlives this describe block — several later blocks in this file
      // (#2069, #2084, spec 141, spec 229) each repeat this same reset for
      // exactly that reason. Not "harmless while this is the last one": it
      // no longer is, and a describe block appended after this one without
      // its own reset is the trap, not this one.
      store.dispatch(systemVariablesApi.util.resetApiState());
    });

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
          systemVariablesApi.util.upsertQueryData(
            'getOverlaySnapshot',
            // Spec 067: the cache key is now the page's `{ overlayIdentifier,
            // fabId }`, so the seeded entry has to be keyed the same way or the
            // tile reads nothing. Arrangement only — the assertions below are
            // the declared characterisation control (plan.md declaration 3).
            { overlayIdentifier: 'ovl-x', fabId: 'munich' },
            {
              overlayIdentifier: 'ovl-x',
              texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 41.0' }],
              version: 1,
            },
          ),
        );
      });

      renderPage();
      const label = () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');
      expect(label()).toBe('OEE 41.0');

      await act(async () => {
        capturedCallbacks?.onResolvedOverlayTextChanged?.({
          overlay: 'ovl-x',
          // Spec 067: the wall's own fab, so this frame stays one the wall
          // accepts. Arrangement only — the two assertions here are the
          // declared characterisation control and must not move (plan.md
          // declaration 3).
          fab: 'munich',
          texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 82.5' }],
          version: 2,
        });
        // `upsertQueryData` is a thunk the page dispatches without awaiting,
        // and the re-render it causes is notified on a later task still — the
        // same real wall-clock settle the #2069 block's own `push` helper
        // documents and pays for below (`settleMilliseconds`). Without it this
        // test raced its own assertion: it reads as isolated and green alone,
        // where nothing else has spent any wall-clock time yet, and red under
        // the full file, where it has. Found bisecting #2345's reported flake.
        await new Promise((resolve) => setTimeout(resolve, 100));
      });

      expect(label(), 'the tile kept its old text after a higher-versioned push').toBe('OEE 82.5');
    });
  });

  /**
   * Spec 141 site 3 — the kiosk holds a third, unshared copy of the
   * placeholder delimiter (`{{`), re-stated in TypeScript with nothing tying
   * it to `PlaceholderParser.cs`. A `ResolvedOverlayTextChangedV1` is only
   * ever published for an overlay the server's own reverse index found a
   * placeholder in (`VariableValueChangedDomainEventHandler.cs:50-62`), so a
   * resolved-text push arriving for an overlay whose tile decided the label
   * was static is, by construction, the two parsers disagreeing about what a
   * placeholder looks like. Only the delimiter-drift sub-case is in scope —
   * an omitted or renamed `text` is visible, not silent, and stays out
   * (spec.md's delimiter table).
   */
  describe('A resolved-text push for a label the kiosk treats as static says so (spec 141 site 3)', () => {
    afterEach(() => {
      store.dispatch(systemVariablesApi.util.resetApiState());
      // Restores the console.info spy — see the note on the site 1 describe.
      vi.restoreAllMocks();
    });

    it('Reports a resolved-text push for a label the kiosk parsed as static', async () => {
      const info = spyOnConsoleInfo();
      // No real-cache double needed: hasPlaceholder is false for this text
      // regardless of fab, so the snapshot query stays skip:true throughout —
      // asserted below — and the default double (never populated) is enough
      // to prove the raw template is what's shown.
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-drift', row: 0, col: 0 })]),
      );
      // The delimiter has moved (`[[…]]`) while the text has not — the only
      // silent sub-case of site 3.
      getOverlayMock.mockReturnValue(publishedOverlay('OEE [[oeeline1]]'));

      renderPage();
      const label = () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');
      expect(
        getSnapshotMock.mock.calls[0]?.[1],
        'the kiosk-side check found no {{, so the snapshot is skipped',
      ).toEqual({ skip: true });

      await pushText({
        overlay: 'ovl-drift',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
        version: 2,
      });

      expect(label(), 'the report does not resolve the disagreement (FR-006)').toBe('OEE [[oeeline1]]');
      expect(resilienceLines(info.mock.calls, 'resolved-text-for-static-label')).toEqual([
        { subsystem: 'hub', transition: 'resolved-text-for-static-label', overlay: 'ovl-drift' },
      ]);
    });

    it('Latches the static-label report at one per overlay per session', async () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-drift-latch', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE [[oeeline1]]'));

      renderPage();

      await pushText({
        overlay: 'ovl-drift-latch',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
        version: 2,
      });
      await pushText({
        overlay: 'ovl-drift-latch',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 100.0' }],
        version: 3,
      });

      expect(resilienceLines(info.mock.calls, 'resolved-text-for-static-label')).toEqual([
        { subsystem: 'hub', transition: 'resolved-text-for-static-label', overlay: 'ovl-drift-latch' },
      ]);
    });

    /**
     * Load-bearing ordering proof for the claim at `CellPage.tsx:239-255`
     * ("sits before the version guard, so a push that loses the version race
     * is still evidence of the disagreement"): the three cases above all use
     * versions that win the race (2, 2/3, 2), so a transposition — moving the
     * static-label check below the version guard — would leave all of them
     * green. This one does not: the verdict is set only *after* a
     * higher-versioned push has already moved the mark, so the disagreeing
     * push that follows can only be reported if the check still runs before
     * the version guard drops it.
     */
    it('Reports the disagreement even when the push loses the version race', async () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-verdict-race', row: 0, col: 0 }),
        ]),
      );
      // Not yet resolved: labelTextKnown is false, so no verdict is recorded
      // yet (R5) — matching "Does not flag a tile while its overlay is still
      // loading" above.
      getOverlayMock.mockReturnValue({ data: undefined });

      renderPage();

      // Silent: no verdict exists yet, so nothing can disagree — but the
      // version mark still advances to 5.
      await pushText({
        overlay: 'ovl-verdict-race',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 5.0' }],
        version: 5,
      });
      expect(
        resilienceLines(info.mock.calls, 'resolved-text-for-static-label'),
        'no verdict exists yet, so a push cannot yet disagree with one',
      ).toEqual([]);

      // The overlay resolves as static (no `{{`), setting the verdict for
      // the first time. Forces the re-render the same way `rerenderTiles`
      // does above — an unrelated highlight on the same overlay — so no
      // fake timers are needed in this describe block.
      getOverlayMock.mockReturnValue(publishedOverlay('OEE [[oeeline1]]'));
      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.({
          overlay: 'ovl-verdict-race',
          fab: 'munich',
          durationMs: 1000,
        });
      });

      // Loses the version race (3 <= 5) — must still be reported.
      await pushText({
        overlay: 'ovl-verdict-race',
        fab: 'munich',
        texts: [{ template: 'OEE [[oeeline1]]', resolved: 'OEE 3.0' }],
        version: 3,
      });

      expect(resilienceLines(info.mock.calls, 'resolved-text-for-static-label')).toEqual([
        { subsystem: 'hub', transition: 'resolved-text-for-static-label', overlay: 'ovl-verdict-race' },
      ]);
    });

    it('QUIET — a label with the recognised delimiter still applies the push and reports nothing', async () => {
      const info = spyOnConsoleInfo();
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-placeholder', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));

      renderPage();
      const label = () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');

      await pushText({
        overlay: 'ovl-placeholder',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
        version: 2,
      });

      expect(label(), 'the push applied, so execution reached the new guard').toBe('OEE 99.9');
      expect(resilienceLines(info.mock.calls, 'resolved-text-for-static-label')).toEqual([]);
    });

    it('QUIET — a not-yet-resolved overlay query reports nothing', async () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-loading', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue({ data: undefined });

      renderPage();

      await pushText({
        overlay: 'ovl-loading',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
        version: 2,
      });

      expect(resilienceLines(info.mock.calls, 'resolved-text-for-static-label')).toEqual([]);
      expect(getOverlayMock, 'the tile rendered and the code ran').toHaveBeenCalledWith('ovl-loading', {
        skip: false,
      });
    });

    it("CONFLICT — another plant's frame for a static label reports nothing", async () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-foreign-static', row: 0, col: 0 }),
        ]),
      );
      getOverlayMock.mockReturnValue(publishedOverlay('OEE [[oeeline1]]'));

      renderPage();

      await pushText({
        overlay: 'ovl-foreign-static',
        fab: 'dresden',
        texts: [{ template: 'OEE [[oeeline1]]', resolved: 'OEE 99.9' }],
        version: 2,
      });

      expect(resilienceLines(info.mock.calls, 'resolved-text-for-static-label')).toEqual([]);
    });
  });
});
