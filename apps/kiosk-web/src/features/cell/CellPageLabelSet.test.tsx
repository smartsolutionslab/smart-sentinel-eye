import {
  getOverlayMock,
  capturedCallbacks,
  getSnapshotMock,
  measureOverlayDrawMock,
  tile,
  publishedRevision,
  mockLayout,
  renderPage,
  publishedOverlayWithLabels,
  resilienceLines,
  spyOnConsoleInfo,
  pushText,
  resetCellPageMocks,
} from '../../test/cellPageHarness.js';
import { describe, it, expect, beforeEach } from 'vitest';
import { act } from '@testing-library/react';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

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
      // site 1 (in `CellPageOmittedInputs.test.tsx`).
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
