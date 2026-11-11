import { measureOverlayDraw, reportKioskLatency } from '@smart-sentinel-eye/shared/observability/kioskLatency';
import type { CameraViewerOverlay } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { useCallback, useEffect, useState } from 'react';
import { useLabelDelay } from '../useLabelDelay';

/**
 * Keeps handing back the same `value` reference across renders whose `key` —
 * a cheap scalar derived from it — is unchanged, even though `value` itself
 * is a fresh object/array every render (spec 150, #2345). The opposite of
 * `useMemo`'s dependency-list form, which would need `value` itself listed as
 * a dependency and would therefore recompute on every reference change
 * regardless of content — exactly the trap `measureOverlayDraw`'s own effect
 * below avoids by keying on a derived scalar instead of the object
 * (#1888/#1889, ADR-0123). `undefined` in, `undefined` out.
 *
 * <p>
 * State, not a ref: `useLabelDelay.ts`'s own `adjustedFor` already uses this
 * "adjust state during render" idiom (React's documented pattern), which a
 * ref cannot — reading or writing `ref.current` during render is refused
 * outright (`react-hooks/refs`).
 * </p>
 */
function useStableByKey<T>(value: T, key: string | undefined): T {
  const [held, setHeld] = useState<{ key: string | undefined; value: T }>({ key, value });
  if (held.key !== key) {
    setHeld({ key, value });
    return value;
  }
  return held.value;
}

/**
 * A stable scalar over every PAINTED field of every element — kind, colour,
 * all four geometry fields, and (Text only) fontSizePx and text. A field
 * missing here makes a change to that field invisible to whichever key
 * consumes it. Shared by both keys so there is exactly one definition of
 * "every painted field" rather than two copies that can drift apart again.
 * `\u0000` cannot appear in an operator-authored label or a hex colour, so
 * this stays a lossless join for the purpose of detecting "did anything in
 * the set actually change".
 */
function paintedElementsKey(elements: readonly CameraViewerOverlay[] | undefined): string | undefined {
  return elements
    ?.map(
      (element) =>
        `${element.kind}|${element.color}|${element.normalizedX}|${element.normalizedY}|${element.normalizedWidth}|${element.normalizedHeight}|${element.kind === 'Text' ? element.fontSizePx : ''}|${element.kind === 'Text' ? element.text : ''}`,
    )
    .join('\u0000');
}

interface HeldOverlayInput {
  liveElements: CameraViewerOverlay[] | undefined;
  frameAgeMilliseconds: number | null;
  overlayUnavailable: boolean;
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  highlighted: boolean;
}

export function useHeldOverlay({
  liveElements,
  frameAgeMilliseconds,
  overlayUnavailable,
  cameraIdentifier,
  getToken,
  highlighted,
}: HeldOverlayInput) {
  const liveElementsKey = paintedElementsKey(liveElements);
  // The set is resolved and versioned atomically (FR-011), so a shared
  // reference — stable across renders that rebuild an equal array — is the
  // consistent reading: `liveElements` above is a fresh array every render,
  // and handing that straight to `useLabelDelay` would make every render
  // look like a change. `useStableByKey` is this unit's version of the fix
  // #1888/#1889 already made for `measureOverlayDraw`'s effect below,
  // applied to the paired set instead of a text-only list.
  const stableLiveElements = useStableByKey(liveElements, liveElementsKey);

  // Held back so the label set describes the same moment as the picture
  // beneath it (ADR-0129). **Not frame accuracy** — it makes the set as old
  // as the picture and pairs nothing with a frame. A tile with no readable
  // age, or one past the cap, gets its labels immediately.
  // Reported per tile so one badly-buffered camera is visible, and reported
  // as what was achieved rather than what was asked for (FR-015).
  const reportHeld = useCallback(
    (achievedMilliseconds: number) =>
      reportKioskLatency('label_delay', cameraIdentifier, achievedMilliseconds, getToken),
    [cameraIdentifier, getToken],
  );

  // One `useLabelDelay` call per tile for the WHOLE PAIRED set (FR-014,
  // spec 301 US1) — hooks cannot be called in a loop, and the set shares one
  // age. Geometry now travels with its text, so a reorder or a Box/Text swap
  // during the hold cannot re-join a held text with a different element's
  // current geometry.
  const heldElements = useLabelDelay(stableLiveElements, frameAgeMilliseconds, reportHeld);

  // `overlayUnavailable` bypasses the hold, as it did before (ADR-0129,
  // "Removal is not held either") — it is a final selection, not a change to
  // what `useLabelDelay` itself holds.
  const renderElements: CameraViewerOverlay[] | undefined = overlayUnavailable ? undefined : heldElements;

  // Spec 040: the overlay-draw leg (ADR-0015, ≤ 50 ms — a whole leg). Timed
  // from the overlay's rendered state changing to the browser having painted
  // it. Observation only: nothing here alters what is drawn or when.
  //
  // Keyed on a stable scalar derived from the element set and the highlight,
  // never on the array itself (spec 150) — an array dependency is a new
  // reference on every render and floods the instrument with no-op samples,
  // the exact defect #1888/#1889 fixed and ADR-0123 forbids reintroducing.
  // Must re-measure on a change to any painted field, not text alone, and
  // not re-measure on an equal re-render (#1888/#1889, #2714) — FR-018
  // (spec 300) covers kind/colour/text; geometry and fontSizePx are this
  // key's own extension of that requirement, not something FR-018 states.
  // Shares `paintedElementsKey` with `liveElementsKey` above so the two keys
  // cannot drift apart again, which is exactly how #2714 happened. When
  // `renderElements` is the same reference as `stableLiveElements` (the
  // common case: not mid-hold, overlay available), its key is already
  // `liveElementsKey` by construction — reuse it instead of re-walking the
  // same elements a second time.
  const renderElementsKey =
    renderElements === stableLiveElements ? liveElementsKey : paintedElementsKey(renderElements);
  useEffect(() => {
    if (renderElementsKey === undefined) {
      return;
    }
    measureOverlayDraw(cameraIdentifier, getToken);
  }, [renderElementsKey, highlighted, cameraIdentifier, getToken]);

  return { renderElements };
}
