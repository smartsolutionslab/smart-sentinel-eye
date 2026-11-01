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
  resilienceLines,
  spyOnConsoleInfo,
  pushText,
  textFrameWithoutFab,
  layoutWithFab,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, screen } from '@testing-library/react';
import type { OverlayHighlightChangedMessage } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { store } from '../../app/store.js';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

  /**
   * Issue #2084 — a frame with no fab at all is a version skew, not a routine
   * drop.
   *
   * <p>
   * Spec 067's filter is correct and stays exactly as it is: a frame whose
   * `fab` is not this wall's is refused (#2069 verified that end to end in a
   * browser). What the filter cannot do is tell two very different frames
   * apart. A frame naming <b>another plant</b> is the feature working — it is
   * supposed to arrive and it is supposed to be dropped. A frame naming
   * <b>no plant at all</b> is a `kiosk-web` bundle running against a
   * LayoutComposition that predates the field: `message.fab` is `undefined`,
   * `undefined !== 'munich'` holds for every frame ever pushed, and every
   * resolved-text and highlight update is discarded for the whole session.
   * </p>
   *
   * <p>
   * <b>And nothing says so.</b> `LiveUpdatesBadge` stays clear, because the hub
   * connection is genuinely healthy — it is delivering frames, and they are all
   * being thrown away. A frozen wall is indistinguishable from a working one to
   * anyone not actively changing a value, which is the same failure mode
   * `Does not let another fab's frame advance the version mark` exists to
   * prevent, arriving by a different route.
   * </p>
   *
   * <h4>Why a log and not the badge</h4>
   *
   * <p>
   * `LiveUpdatesBadge` means one thing — the hub connection is down and
   * retrying — and it is defined to <i>clear on reconnection</i>
   * (`useLayoutLifecycle`'s `onStateChange`). A version skew is the opposite
   * condition on both counts: the connection is healthy, and reconnecting to
   * the same skewed server fixes nothing. Driving the badge from it would make
   * a genuine outage and a bad deployment look identical to the operator, while
   * making the badge lie on the next reconnect. The two stay separate, and the
   * first case below asserts the badge stays clear.
   * </p>
   *
   * <h4>Why first-and-then-decades, rather than once or every time</h4>
   *
   * <p>
   * A wall runs for weeks. One line per frame floods the console and evicts the
   * first — the diagnostically valuable — occurrence. One line per session
   * survives eviction but is emitted the moment the wall boots, weeks before
   * anyone opens a remote-debug console because a label looked stale. So: the
   * first occurrence, then every power of ten, each carrying the running count.
   * That is bounded at roughly one line per order of magnitude and still leaves
   * a recent line near the tail of the buffer.
   * </p>
   *
   * <p>
   * The two routes count separately and log under separate transitions: a
   * server that carries `fab` on one frame type and not the other is exactly
   * the skew this exists to name, and one shared counter would hide it.
   * </p>
   */
  describe('A frame carrying no fab at all is a version skew (#2084)', () => {
    afterEach(() => {
      store.dispatch(systemVariablesApi.util.resetApiState());
      vi.restoreAllMocks();
    });

    /**
     * A munich wall showing one placeholder label, reading the real RTK cache —
     * so an applied frame genuinely moves the label and a dropped one genuinely
     * does not. No seeded snapshot: the tile falls back to the overlay's raw
     * text, which is a value no frame below ever pushes.
     */
    function aMunichWall(overlay: string) {
      getSnapshotMock.mockImplementation(useSnapshotFromTheRealCache);
      mockLayout(
        publishedRevision(1, 1, [tile({ cameraIdentifier: 'cam-a', overlayIdentifier: overlay, row: 0, col: 0 })]),
      );
      // The `{{…}}` is load-bearing: without a placeholder the page's
      // `hasPlaceholder` gate skips the snapshot query and no push would show.
      getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));
      renderPage();
      return () => screen.getByTestId('camera-viewer').getAttribute('data-overlay-text');
    }

    /** The same omission on the highlight route, which travels its own path. */
    function highlightFrameWithoutFab(overlay: string, durationMs: number): OverlayHighlightChangedMessage {
      return { overlay, durationMs } as unknown as OverlayHighlightChangedMessage;
    }

    // pushText, resilienceLines and spyOnConsoleInfo are declared at module
    // scope above (spec 141) so the new site 1-3 describes below can share
    // them without a second copy free to drift.

    /**
     * RED. The gap itself: a fab-less resolved-text frame is still refused (the
     * filter does not change) but now says so.
     *
     * <p>
     * The badge assertion is a CONTROL, green today and required to stay green:
     * it pins the decision that a version skew is not a hub outage.
     * </p>
     */
    it('Reports a resolved-text frame that carries no fab, and leaves the badge alone', async () => {
      const info = spyOnConsoleInfo();
      const label = aMunichWall('ovl-skew-text');
      expect(label()).toBe('OEE {{oeeline1}}');

      await pushText(textFrameWithoutFab('ovl-skew-text', 'OEE 99.9', 2));

      expect(label(), 'the filter must not change — a fab-less frame is still refused').toBe('OEE {{oeeline1}}');
      expect(resilienceLines(info.mock.calls, 'resolved-text-without-fab')).toEqual([
        { subsystem: 'hub', transition: 'resolved-text-without-fab', overlay: 'ovl-skew-text', count: 1 },
      ]);
      expect(
        screen.queryByTestId('live-updates-degraded'),
        'a version skew is not a hub outage — the badge must not conflate them',
      ).not.toBeInTheDocument();
    });

    /**
     * RED. The console-volume decision, stated as an assertion: the first
     * occurrence and every power of ten, carrying the running count, and
     * nothing in between.
     */
    it('Repeats the report on decades of dropped frames, not on every one', async () => {
      const info = spyOnConsoleInfo();
      aMunichWall('ovl-skew-decade');

      for (let version = 2; version <= 11; version += 1) {
        await pushText(textFrameWithoutFab('ovl-skew-decade', `OEE ${version}`, version));
      }

      expect(resilienceLines(info.mock.calls, 'resolved-text-without-fab')).toEqual([
        { subsystem: 'hub', transition: 'resolved-text-without-fab', overlay: 'ovl-skew-decade', count: 1 },
        { subsystem: 'hub', transition: 'resolved-text-without-fab', overlay: 'ovl-skew-decade', count: 10 },
      ]);
    });

    /**
     * RED, and **the whole point of the issue**. Dropping another plant's frame
     * is the feature working; dropping a frame with no plant on it is a
     * deployment fault wearing the same clothes. A signal that fires on both is
     * no signal at all — it would report a skew on every healthy two-fab
     * principal, every day.
     */
    it("Says nothing about a frame that names another plant's fab", async () => {
      const info = spyOnConsoleInfo();
      const label = aMunichWall('ovl-foreign-not-skew');

      await pushText({
        overlay: 'ovl-foreign-not-skew',
        fab: 'dresden',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
        version: 2,
      });

      expect(label(), "a munich wall showed dresden's production figure").toBe('OEE {{oeeline1}}');
      expect(
        resilienceLines(info.mock.calls, 'resolved-text-without-fab'),
        'a well-formed foreign frame is the filter working, not a version skew',
      ).toEqual([]);
    });

    /**
     * RED. The highlight route has the same filter and — unlike the
     * resolved-text route — no version guard at all (#2069 review), so nothing
     * else on it would ever notice. Its symptom is harder to spot, too: a
     * highlight that never fires leaves no stale label behind, just a tile that
     * never lights.
     */
    it('Reports a highlight frame that carries no fab, under its own transition', () => {
      const info = spyOnConsoleInfo();
      mockLayout(
        publishedRevision(1, 1, [
          tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-skew-highlight', row: 0, col: 0 }),
        ]),
      );
      renderPage();

      act(() => {
        capturedCallbacks?.onOverlayHighlightChanged?.(highlightFrameWithoutFab('ovl-skew-highlight', 1000));
      });

      expect(
        screen.getAllByTestId('layout-tile').map((el) => el.dataset.highlighted),
        'the filter must not change — a fab-less highlight is still refused',
      ).toEqual(['false']);
      expect(resilienceLines(info.mock.calls, 'highlight-without-fab')).toEqual([
        { subsystem: 'hub', transition: 'highlight-without-fab', overlay: 'ovl-skew-highlight', count: 1 },
      ]);
      expect(
        resilienceLines(info.mock.calls, 'resolved-text-without-fab'),
        'the two routes count separately, so a skew on one is not hidden by the other',
      ).toEqual([]);
    });

    /**
     * CONTROL — green today and it must still be green afterwards. A signal
     * that fires on every frame satisfies the cases above and reports a
     * permanent skew on a perfectly healthy wall.
     */
    it("Says nothing about the wall's own frame", async () => {
      const info = spyOnConsoleInfo();
      const label = aMunichWall('ovl-own-not-skew');

      await pushText({
        overlay: 'ovl-own-not-skew',
        fab: 'munich',
        texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 82.5' }],
        version: 2,
      });

      expect(label(), "the wall stopped applying its own plant's frames").toBe('OEE 82.5');
      expect(resilienceLines(info.mock.calls, 'resolved-text-without-fab')).toEqual([]);
    });

    /**
     * Issue #2320 — two absent fabs compare equal. `wallFab` is legitimately
     * `undefined` on a fab-less layout (spec 141 site 2 reports it and skips
     * the snapshot query, but does not resolve it), and `message.fab` is
     * `undefined` on the very frame this block's fixtures model. Both guards
     * read `message.fab !== wallFab`, so `undefined !== undefined` is
     * `false` and the frame is wrongly applied — the #2069 leak reached
     * through a different door.
     *
     * <p>
     * Nested here (rather than a sibling describe) so these cases can reuse
     * `textFrameWithoutFab` / `highlightFrameWithoutFab` above without a
     * second copy free to drift, exactly as this block's own top-of-describe
     * comment already argues for `pushText` / `resilienceLines` /
     * `spyOnConsoleInfo`.
     * </p>
     *
     * <p>
     * <b>The highlight case is the strongest red.</b> The resolved-text
     * route has no visible symptom on a fab-less wall: spec 141 already
     * skips the snapshot query there (`CellPage.tsx:471`, via `namedFab`), so
     * the label never moves whether the guard is right or wrong. Asserting
     * the label would pass today for the wrong reason — nothing renders
     * because the query never ran, not because the guard rejected the frame
     * — so that case asserts the RTK cache entry the push writes directly.
     * The highlight route has no such second net, so the DOM itself proves
     * the defect.
     * </p>
     */
    describe('Two absent fabs must not compare equal (#2320)', () => {
      /** RED. SC-2 — the strongest red: nothing else stands between the guard and the DOM. */
      it('A fab-less highlight frame lights a tile on a fab-less wall', () => {
        mockLayout(
          layoutWithFab(undefined, [
            tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-2320-highlight', row: 0, col: 0 }),
          ]),
        );
        renderPage();

        act(() => {
          capturedCallbacks?.onOverlayHighlightChanged?.(highlightFrameWithoutFab('ovl-2320-highlight', 1000));
        });

        expect(
          screen.getAllByTestId('layout-tile').map((el) => el.dataset.highlighted),
          'two absent fabs compared equal, so a fab-less frame lit a fab-less wall',
        ).toEqual(['false']);
      });

      /**
       * RED. SC-1 — the cache entry and the version mark, not the label
       * (plan.md Risk 1): the label cannot move on a fab-less wall regardless
       * of the guard, because the snapshot query is skipped there already.
       * The second push (a higher version than the first) proves the mark was
       * never advanced by the first — if it had been, this later push would
       * still be "accepted" and the cache would carry its text instead of
       * staying absent.
       */
      it('A fab-less resolved-text push writes a snapshot cache entry a fab-less wall never asked for', async () => {
        mockLayout(
          layoutWithFab(undefined, [
            tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-2320-text', row: 0, col: 0 }),
          ]),
        );
        getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));
        renderPage();

        const cachedSnapshot = () =>
          systemVariablesApi.endpoints.getOverlaySnapshot.select({
            overlayIdentifier: 'ovl-2320-text',
            fabId: undefined as unknown as string,
          })(store.getState()).data;

        await pushText(textFrameWithoutFab('ovl-2320-text', 'OEE 99.9', 2));
        expect(
          cachedSnapshot(),
          'a fab-less push wrote a snapshot entry a fab-less wall never asked for',
        ).toBeUndefined();

        await pushText(textFrameWithoutFab('ovl-2320-text', 'OEE 100.0', 3));
        expect(cachedSnapshot(), 'a later, higher-versioned fab-less frame was also refused').toBeUndefined();
      });

      /**
       * RED. SC-3, highlight route — the empty-string twin.
       * `CellPage.tsx:69-72` argues `''` "never matches a `!==`", true only
       * against a *named* wall fab: a wall on `fab: ''` and a frame on
       * `fab: ''` compare equal exactly like the `undefined` twin above.
       */
      it('The empty-string twin lights a tile on an empty-string wall exactly like the undefined twin', () => {
        mockLayout(
          layoutWithFab('', [
            tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-2320-empty-highlight', row: 0, col: 0 }),
          ]),
        );
        renderPage();

        act(() => {
          capturedCallbacks?.onOverlayHighlightChanged?.({
            overlay: 'ovl-2320-empty-highlight',
            fab: '',
            durationMs: 1000,
          });
        });

        expect(
          screen.getAllByTestId('layout-tile').map((el) => el.dataset.highlighted),
          'an empty-string wall fab and an empty-string frame fab compared equal',
        ).toEqual(['false']);
      });

      /**
       * RED. SC-3, resolved-text route — and the reason the fix must read
       * `namedFab(wallFab) === null`, not `wallFab === undefined`: a fix
       * spelled the naive way leaves `wallFab` as `''`, `'' !== undefined`,
       * so the naive guard would not reject this frame either. Only a fix
       * that treats `''` the same as `undefined` (i.e. `namedFab`) closes
       * this case.
       */
      it('The empty-string twin writes a cache entry a naive undefined-only guard would not have caught', async () => {
        mockLayout(
          layoutWithFab('', [
            tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-2320-empty-text', row: 0, col: 0 }),
          ]),
        );
        getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));
        renderPage();

        await pushText({
          overlay: 'ovl-2320-empty-text',
          fab: '',
          texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
          version: 2,
        });

        expect(
          systemVariablesApi.endpoints.getOverlaySnapshot.select({
            overlayIdentifier: 'ovl-2320-empty-text',
            fabId: '',
          })(store.getState()).data,
          'an empty-string wall fab and an empty-string frame fab compared equal',
        ).toBeUndefined();
      });

      /**
       * RED (phase 6 follow-up). The whitespace twin: `namedFab`'s original
       * fix only rejected `''`, not a whitespace-only fab. A wall on
       * `fab: ' '` and a frame on `fab: ' '` compare equal under a bare
       * `!==` exactly like the empty-string twin above — closed by
       * `namedFab` calling `.trim()` before its emptiness check, not by the
       * `!==` guard itself.
       */
      it('A whitespace-only fab compares equal to itself and must not light a tile', () => {
        mockLayout(
          layoutWithFab(' ', [
            tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-2320-ws-highlight', row: 0, col: 0 }),
          ]),
        );
        renderPage();

        act(() => {
          capturedCallbacks?.onOverlayHighlightChanged?.({
            overlay: 'ovl-2320-ws-highlight',
            fab: ' ',
            durationMs: 1000,
          });
        });

        expect(
          screen.getAllByTestId('layout-tile').map((el) => el.dataset.highlighted),
          'a whitespace-only wall fab and a whitespace-only frame fab compared equal',
        ).toEqual(['false']);
      });

      /**
       * RED (phase 6 follow-up), resolved-text route. Also proves the fix
       * must trim before comparing emptiness: `namedFab(' ')` without
       * `.trim()` returns `' '` (truthy, non-empty), so the guard's first
       * term stays `false` and `' ' !== ' '` is also `false` — the frame is
       * applied. Independently significant because a whitespace fab does
       * NOT get skipped by the snapshot-query guard either without this
       * fix, which would send an unconditional, untrimmed `fabId` to the
       * server — the same cross-fab resolution #2069 closed, reached via a
       * whitespace value instead of an empty one.
       */
      it('A whitespace-only fab must not write a cache entry a whitespace-only wall never asked for', async () => {
        mockLayout(
          layoutWithFab(' ', [
            tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-2320-ws-text', row: 0, col: 0 }),
          ]),
        );
        getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));
        renderPage();

        await pushText({
          overlay: 'ovl-2320-ws-text',
          fab: ' ',
          texts: [{ template: 'OEE {{oeeline1}}', resolved: 'OEE 99.9' }],
          version: 2,
        });

        expect(
          systemVariablesApi.endpoints.getOverlaySnapshot.select({
            overlayIdentifier: 'ovl-2320-ws-text',
            fabId: ' ',
          })(store.getState()).data,
          'a whitespace-only wall fab and a whitespace-only frame fab compared equal',
        ).toBeUndefined();
      });

      /**
       * CONTROL — green today and required to stay green after the fix
       * (SC-6). This spec changes the filter, not the reporters:
       * `countReportableSkew`'s own `wallFab === undefined` early return
       * (`CellPage.tsx:610`) is untouched, so it never fires on a fab-less
       * wall regardless of what the frame carries, and the wall has already
       * said once, via `layout-without-fab`, that it has no fab.
       */
      it('The layout-without-fab report fires once and the per-frame skew reporters stay silent', async () => {
        const info = spyOnConsoleInfo();
        mockLayout(
          layoutWithFab(undefined, [
            tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ovl-2320-sc6', row: 0, col: 0 }),
          ]),
        );
        getOverlayMock.mockReturnValue(publishedOverlay('OEE {{oeeline1}}'));
        renderPage();

        await pushText(textFrameWithoutFab('ovl-2320-sc6', 'OEE 99.9', 2));
        act(() => {
          capturedCallbacks?.onOverlayHighlightChanged?.(highlightFrameWithoutFab('ovl-2320-sc6', 1000));
        });

        expect(resilienceLines(info.mock.calls, 'layout-without-fab')).toEqual([
          { subsystem: 'hub', transition: 'layout-without-fab', layout: 'cam-1' },
        ]);
        expect(
          resilienceLines(info.mock.calls, 'resolved-text-without-fab'),
          "countReportableSkew's wallFab === undefined early return is untouched by this spec",
        ).toEqual([]);
        expect(
          resilienceLines(info.mock.calls, 'highlight-without-fab'),
          'the highlight route has no reporter of its own for this spec to have disturbed either',
        ).toEqual([]);
      });
    });
  });
});
