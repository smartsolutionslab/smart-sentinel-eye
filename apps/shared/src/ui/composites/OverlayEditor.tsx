import { useCallback, useEffect, useId, useRef, useState } from 'react';
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent } from 'react';
import { Rnd } from 'react-rnd';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { ResolvedTextPreview } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { overlayLabelSurfaceStyle } from './overlayLabelStyle.js';
import { BackdropControls } from './BackdropControls.js';
import type { Backdrop } from './BackdropControls.js';
import { FrameGrabber } from './FrameGrabber.js';
import { useFrameCapture } from './useFrameCapture.js';
import { PlaceholderPreviewPanel, PLACEHOLDER_PREVIEW_STATUS_ID } from './PlaceholderPreviewPanel.js';
import { formatPercent, QUANTUM } from './normalizedPercent.js';
import { OverlayGeometryFields } from './OverlayGeometryFields.js';
import type { OverlayGeometry, OverlayGeometryField } from './OverlayGeometryFields.js';
import { useOverlayEditHistory } from './useOverlayEditHistory.js';
import type { Boundary, RunKey } from './useOverlayEditHistory.js';

export interface OverlayEditorProps {
  value: OverlayLabel;
  onChange: (next: OverlayLabel) => void;
  /**
   * Pixel-space backdrop the label is positioned over. Defaults to a
   * 16:9 800x450 canvas — large enough to be usable, small enough to
   * fit inside a Dialog. The fixed aspect ratio keeps normalized
   * coordinates resolution-independent.
   */
  canvasWidthPx?: number;
  canvasHeightPx?: number;
  className?: string;
  /**
   * Resolves the operator's bearer token for a captured-frame WHEP session
   * (spec 147). Absent means the camera picker and "Capture frame" are not
   * offered at all — the checkerboard/white/black backdrops keep working
   * regardless (FR-017's degradation path expressed as a type). Same shape as
   * `CameraViewerProps.getToken` / `WhepSessionOptions.getToken`.
   */
  getToken?: () => Promise<string | null>;
  /**
   * Spec 148 US1 + US3 — the caller (`OverlayEditorDialog`) owns the debounce
   * and the `useResolveOverlayTextQuery` call and passes the settled result
   * down as props, so this component stays Redux-free: it lives in
   * `apps/shared`, is consumed by two apps, and a shared presentational
   * component should not assume a store exists. All three are optional so a
   * caller with no query wiring (every existing test) renders exactly as
   * before, falling back to the raw text.
   */
  resolvedPreview?: ResolvedTextPreview;
  isResolving?: boolean;
  resolveFailed?: boolean;
}

const MIN_NORMALIZED = 0;
const MAX_NORMALIZED = 1;

function clamp01(value: number): number {
  if (Number.isNaN(value)) return 0;
  if (value < MIN_NORMALIZED) return MIN_NORMALIZED;
  if (value > MAX_NORMALIZED) return MAX_NORMALIZED;
  return value;
}

// Spec 149 §The step grid / §The keyboard map: the two step sizes, the
// server's size floor, and the announcement debounce. `QUANTUM` — the grid
// that keeps repeated presses from drifting in IEEE-754 — is imported from
// `normalizedPercent.js` (phase 6 nit 6): spec 151 defined the same constant
// a second time there rather than importing it. Reasoning lives in spec.md,
// not restated here.
const FINE_STEP = 0.005;
const COARSE_STEP = 0.05;
const MIN_NORMALIZED_SIZE = 0.005;
const ANNOUNCE_DELAY_MS = 500;

/**
 * #2361: the size clamp for `width`/`height`, separate from `clamp01`'s
 * position clamp. `[0, 1]` is the correct domain for `x`/`y` — exactly `0`
 * is a valid position — but a size of `0` is not a valid size, so a
 * non-positive or `NaN` pixel-derived width/height (a drag past the canvas
 * edge, or `NaN` from a zero-size ref) must floor to `MIN_NORMALIZED_SIZE`
 * rather than pass through as `clamp01` would. Deliberately **not**
 * `Math.max(value, MIN_NORMALIZED_SIZE)` — that would also floor a
 * small-but-domain-valid size (e.g. `0.003`), which must pass through
 * unchanged; only a genuinely invalid value (`<= 0` or `NaN`) gets floored.
 */
function clampSize(value: number): number {
  // !(value > 0) — not value <= 0 — so this also catches NaN (NaN > 0 is false)
  if (!(value > 0)) return MIN_NORMALIZED_SIZE;
  if (value > MAX_NORMALIZED) return MAX_NORMALIZED;
  return value;
}

const ARROW_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);

function quantize(value: number): number {
  return Math.round(value * QUANTUM) / QUANTUM;
}

/**
 * The reachable-region bound (`1 - size` / `1 - origin`), floored to the
 * quantum grid rather than plain-quantized (finding 1). `1 - size` is
 * arithmetic on a quantized `size`, but IEEE-754 still lands it off-grid for
 * a large fraction of grid-valid sizes (`1 - 0.8 === 0.19999999999999996`),
 * and the bound is applied *last*, so an unquantized bound silently defeats
 * the quantum at exactly the boundary the quantum exists for. A plain floor
 * would then round a mathematically-exact bound like that one *inward* by a
 * whole quantum step (`0.1999`, not `0.2`) — the epsilon nudge absorbs the
 * float noise (~1e-13 here) without being anywhere near large enough to
 * round a genuinely off-grid bound (an origin/size from a drag, e.g.
 * `0.248714`) *outward* past the true edge.
 */
function quantizeBoundFloor(value: number): number {
  return Math.floor(value * QUANTUM + 1e-9) / QUANTUM;
}

/**
 * FR-007, per plan.md §2 (corrected post-review — finding 2): quantize, then
 * `clamp01` (the same clamp the drag path uses), then clamp the *motion*
 * against the reachable-region bound rather than clamping the resulting
 * *value* to it. `NormalizedPosition` bounds `x`/`y` to `[0, 1]` each but not
 * relative to size, so the domain permits a label already off the canvas's
 * high edge (`x + width > 1`); clamping the value would snap such a label
 * onto the bound on the very first keypress, including one that moved it
 * *away* from that edge. Clamping the motion instead lets an off-region
 * value move freely toward the region (`delta` pointing inward) and refuses
 * only the press that would carry it further out. `size` is the axis's
 * *other* dimension (width for an x move, height for a y move).
 */
function nudgePosition(current: number, delta: number, size: number): number {
  const stepped = clamp01(quantize(current + delta));
  if (delta <= 0) return stepped;
  const bound = quantizeBoundFloor(Math.max(0, 1 - size));
  return Math.min(stepped, Math.max(bound, current));
}

/**
 * FR-007 + FR-008, per plan.md §2 (corrected post-review — finding 2):
 * quantize, `clamp01`, the `NormalizedSize` floor, *then* the
 * reachable-region bound (anchored at the axis's origin) applied to the
 * motion, not the value. The floor sits **inside** the canvas bound —
 * reversed from before — because outermost it could win over the bound and
 * mint an off-canvas label (finding 2a): at a near-edge origin the floor and
 * the canvas bound can conflict, and canvas containment (FR-007) must win,
 * even if that means a size below the floor is emitted at that origin — it
 * is still a domain-valid, positive size. The bound clamps motion for the
 * same off-region reason as `nudgePosition` (finding 2b): the domain does
 * not relate size to origin, so an already off-canvas size may keep
 * shrinking toward the canvas but a press that would grow it further out is
 * refused instead of snapping it down to the bound.
 */
function resizeSize(current: number, delta: number, origin: number): number {
  const stepped = clamp01(quantize(current + delta));
  const flooredBySize = Math.max(stepped, MIN_NORMALIZED_SIZE);
  if (delta <= 0) return flooredBySize;
  const bound = quantizeBoundFloor(Math.max(0, 1 - origin));
  return Math.min(flooredBySize, Math.max(bound, current));
}

type AnnounceAxis = 'x' | 'y' | 'width' | 'height';

const AXIS_ANNOUNCE_LABEL: Record<AnnounceAxis, string> = {
  x: 'Left',
  y: 'Top',
  width: 'Width',
  height: 'Height',
};

// FR-016: which edge a refused press hit. Position axes are refused at the
// canvas edge on either side; size axes are refused at FR-008's floor on
// the low side and at the canvas edge (the reachable-region bound) on the
// high side.
function edgeName(axis: AnnounceAxis, delta: number): string {
  if (axis === 'x') return delta < 0 ? 'at the left edge' : 'at the right edge';
  if (axis === 'y') return delta < 0 ? 'at the top edge' : 'at the bottom edge';
  if (axis === 'width') return delta < 0 ? 'at the minimum' : 'at the right edge';
  return delta < 0 ? 'at the minimum' : 'at the bottom edge';
}

function buildAnnouncement(axis: AnnounceAxis, value: number, edge: string | null): string {
  const base = `${AXIS_ANNOUNCE_LABEL[axis]} ${formatPercent(value)}`;
  return edge === null ? base : `${base}, ${edge}`;
}

// Spec 154 Decision 2, site 6: the run key includes both the axis and the
// mode, so switching between moving and resizing mid-hold (Ctrl) starts a
// new run even though the axis label alone (`width` vs `x`) would already
// differ; holding Shift changes only the step size, never this key.
function arrowRunKey(resizing: boolean, axis: AnnounceAxis): RunKey {
  return `arrow:${resizing ? 'resize' : 'move'}:${axis}`;
}

// FR-002. Two rings drawn with `outline` (flush against the element, paints
// on top of `boxShadow` per CSS paint order — the white inner ring) and
// `boxShadow` (a wider solid extension — the black outer ring underneath
// it). Never `border` — `OverlayLabelParity.test.tsx` pins the label's
// border empty against the wall in every state.
//
// Both rings are drawn *inward* — `outlineOffset: -2` and an `inset`
// `boxShadow` — because the canvas is `overflow: hidden` (phase 6 should-fix
// 3): the keyboard path's own bounds let the label sit flush against every
// edge of the canvas, exactly where an outward ring would be clipped by the
// overflow it is meant to be visible against.
const FOCUS_RING_STYLE: CSSProperties = {
  outline: '2px solid #ffffff',
  outlineOffset: -2,
  boxShadow: 'inset 0 0 0 4px #000000',
};

// FR-002, byte-for-byte. Pinned by `OverlayEditorCharacterisation.test.tsx`
// (T001) — a mangled rebase against #2354 fails that test loudly.
const CHECKERBOARD_BACKGROUND = 'repeating-linear-gradient(45deg, #1f2937, #1f2937 12px, #111827 12px, #111827 24px)';

/**
 * The canvas backdrop as a function of the operator's choice (spec 147
 * FR-001–FR-004, FR-019). `captured` without a frame yet falls back to the
 * checkerboard — the radio for it is disabled in that state, so this is a
 * defensive default rather than a reachable path.
 */
function canvasBackgroundStyle(backdrop: Backdrop, capturedFrame: string | null): CSSProperties {
  if (backdrop === 'white') return { backgroundColor: '#ffffff' };
  if (backdrop === 'black') return { backgroundColor: '#000000' };
  if (backdrop === 'captured' && capturedFrame !== null) {
    // FR-019: the still is fitted *into* the fixed canvas, never the reverse —
    // `contain`/`center`/`no-repeat` over black is the same letterboxing
    // semantics as the wall's `object-contain` over `bg-black`
    // (`CameraViewer.tsx`), so the label's normalized coordinates address the
    // same box on both.
    return {
      backgroundColor: '#000000',
      backgroundImage: `url("${capturedFrame}")`,
      backgroundSize: 'contain',
      backgroundPosition: 'center center',
      backgroundRepeat: 'no-repeat',
    };
  }
  return { background: CHECKERBOARD_BACKGROUND };
}

/**
 * WYSIWYG label editor (spec 004 T059). A fixed-aspect canvas
 * surfaces a draggable + resizable label preview backed by
 * <c>react-rnd</c>; sliders below the canvas tune the font size and
 * the text input updates the label text. <c>x</c>/<c>y</c> are clamped
 * to [0, 1] and <c>width</c>/<c>height</c> to <c>(0, 1]</c>, with a
 * non-positive or <c>NaN</c> value floored to <c>MIN_NORMALIZED_SIZE</c>,
 * before <c>onChange</c> fires (#2361).
 */
export function OverlayEditor({
  value,
  onChange,
  canvasWidthPx = 800,
  canvasHeightPx = 450,
  className,
  getToken,
  resolvedPreview,
  isResolving = false,
  resolveFailed = false,
}: OverlayEditorProps) {
  // US3: the canvas box sizes around the string the wall will actually show.
  // Raw text is the fallback while the preview is in flight, has failed, or
  // has not been requested at all (no `{{` — `resolvedPreview` never arrives)
  // — never empty, never a placeholder of its own (spec 148 US3 scenario 3).
  const previewText = resolvedPreview?.resolvedText ?? value.text;
  const pixelX = value.normalizedX * canvasWidthPx;
  const pixelY = value.normalizedY * canvasHeightPx;
  const pixelWidth = Math.max(value.normalizedWidth * canvasWidthPx, 24);
  const pixelHeight = Math.max(value.normalizedHeight * canvasHeightPx, 16);

  // Spec 154 (issue #2347): step-wise undo/redo over this editing session,
  // kept entirely in this hook's own React state — no Redux, no new
  // required prop (Decision 4). `commit` replaces every direct `onChange`
  // call below; the payload each site builds is unchanged, byte for byte
  // (FR-015) — only the bookkeeping around the emission is new.
  const { commit, endRun, undo, redo, canUndo, canRedo } = useOverlayEditHistory(value, onChange);

  // T005: the single place that builds the `onChange` payload. `clamp01`
  // bounds `x`/`y` and `clampSize` bounds `width`/`height` (#2361) — the drag
  // path's pixel-derived values need these as their only bound (unchanged,
  // FR-011); the keyboard path's values are already bounded tighter by
  // `nudgePosition` / `resizeSize` before they arrive here, so both clamps
  // are a no-op on them.
  const emitNormalized = useCallback(
    (nextX: number, nextY: number, nextWidth: number, nextHeight: number, boundary: Boundary) => {
      commit(
        {
          ...value,
          normalizedX: clamp01(nextX),
          normalizedY: clamp01(nextY),
          normalizedWidth: clampSize(nextWidth),
          normalizedHeight: clampSize(nextHeight),
        },
        boundary,
      );
    },
    [commit, value],
  );

  const emitGeometry = useCallback(
    (xPx: number, yPx: number, widthPx: number, heightPx: number) => {
      emitNormalized(
        xPx / canvasWidthPx,
        yPx / canvasHeightPx,
        widthPx / canvasWidthPx,
        heightPx / canvasHeightPx,
        'atomic',
      );
    },
    [canvasWidthPx, canvasHeightPx, emitNormalized],
  );

  // Spec 151 FR-013/FR-014: the live readout. `onDrag`/`onResize` hold the
  // in-flight geometry in local state, through the same `clamp01`/`clampSize`
  // the stop handlers use (via `emitNormalized`), so the readout never shows
  // a number the release would not produce. `onChange` does not fire from
  // either — only the two existing `*Stop` handlers below call it,
  // unchanged, and then clear this back to `null` (FR-014: "discarded when
  // the gesture ends" — spec.md:217).
  // `OverlayGeometryFields` then falls back to the `value` prop, which in the
  // real, controlled `OverlayEditorDialog.tsx` (a `<Controller>`) is updated
  // by this same `onChange` on the very next render, so the field keeps
  // reading correctly across the handoff.
  const [preview, setPreview] = useState<OverlayGeometry | null>(null);

  const geometryFromPixels = useCallback(
    (xPx: number, yPx: number, widthPx: number, heightPx: number): OverlayGeometry => ({
      x: clamp01(xPx / canvasWidthPx),
      y: clamp01(yPx / canvasHeightPx),
      width: clampSize(widthPx / canvasWidthPx),
      height: clampSize(heightPx / canvasHeightPx),
    }),
    [canvasWidthPx, canvasHeightPx],
  );

  const handleDrag = useCallback(
    (_e: unknown, data: { x: number; y: number }) => {
      setPreview(geometryFromPixels(data.x, data.y, pixelWidth, pixelHeight));
    },
    [geometryFromPixels, pixelWidth, pixelHeight],
  );

  const handleDragStop = useCallback(
    (_e: unknown, data: { x: number; y: number }) => {
      emitGeometry(data.x, data.y, pixelWidth, pixelHeight);
      setPreview(null);
    },
    [pixelWidth, pixelHeight, emitGeometry],
  );

  const handleResize = useCallback(
    (
      _e: unknown,
      _dir: string,
      ref: { offsetWidth: number; offsetHeight: number },
      _delta: unknown,
      position: { x: number; y: number },
    ) => {
      setPreview(geometryFromPixels(position.x, position.y, ref.offsetWidth, ref.offsetHeight));
    },
    [geometryFromPixels],
  );

  const handleResizeStop = useCallback(
    (
      _e: unknown,
      _dir: string,
      ref: { offsetWidth: number; offsetHeight: number },
      _delta: unknown,
      position: { x: number; y: number },
    ) => {
      emitGeometry(position.x, position.y, ref.offsetWidth, ref.offsetHeight);
      setPreview(null);
    },
    [emitGeometry],
  );

  // Spec 151 FR-006, plan.md §3c: one field, spread onto `value`. Deliberately
  // **not** through `emitNormalized` — that applies `clamp01`/`clampSize` to
  // all four values, but `OverlayGeometryFields` has already validated the
  // committed field strictly tighter than either clamp (FR-008/FR-009), and
  // running the *other three* through them would silently rewrite a stored
  // off-grid or out-of-range value the operator never touched (FR-006 says
  // they travel forward untouched). Typed entry therefore reaches neither
  // clamp (#2346/spec 151); `OverlayGeometryFields`' own validation is its
  // only bound.
  const handleGeometryCommit = useCallback(
    (field: OverlayGeometryField, normalized: number) => {
      commit({ ...value, [field]: normalized }, 'atomic');
    },
    [commit, value],
  );

  // FR-002/FR-003: driven by onFocus/onBlur, not `:focus-visible` — the file
  // is inline-styled throughout and a pseudo-class cannot be expressed
  // inline. A mouse click also raises the ring; that is deliberate (spec.md
  // FR-003).
  const [isLabelFocused, setIsLabelFocused] = useState(false);

  // FR-015/016/017: one debounced live-region message. The timer is reset on
  // every handled keypress so a burst produces exactly one announcement.
  const [liveMessage, setLiveMessage] = useState('');
  const announceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Phase 6 should-fix 5: a held key that stays refused at the same edge
  // repeats the same `message` burst after burst. Writing an unchanged
  // string is a React no-op — the DOM text never changes, so the region
  // never fires again after the first burst. A trailing zero-width space,
  // toggled on each write, makes a repeat of the same announcement a
  // different string every other time, without changing what is read aloud.
  const lastAnnouncedRef = useRef('');
  const announceToggleRef = useRef(false);

  const queueAnnouncement = useCallback((message: string) => {
    if (announceTimerRef.current !== null) {
      clearTimeout(announceTimerRef.current);
    }
    announceTimerRef.current = setTimeout(() => {
      const repeated = message === lastAnnouncedRef.current;
      announceToggleRef.current = repeated ? !announceToggleRef.current : false;
      lastAnnouncedRef.current = message;
      setLiveMessage(announceToggleRef.current ? `${message}\u200B` : message);
      announceTimerRef.current = null;
    }, ANNOUNCE_DELAY_MS);
  }, []);

  useEffect(
    () => () => {
      if (announceTimerRef.current !== null) {
        clearTimeout(announceTimerRef.current);
      }
    },
    [],
  );

  // FR-004–FR-009: the one `onKeyDown` on the label. Non-arrow keys, and
  // Alt/Meta held with an arrow, are left entirely alone (spec.md §The
  // keyboard map — Alt+arrow stays browser Back/Forward).
  const handleLabelKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLDivElement>) => {
      if (!ARROW_KEYS.has(event.key)) return;
      if (event.altKey || event.metaKey) return;
      event.preventDefault();

      const step = event.shiftKey ? COARSE_STEP : FINE_STEP;
      const resizing = event.ctrlKey;
      const sign = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
      const delta = sign * step;

      let nextX = value.normalizedX;
      let nextY = value.normalizedY;
      let nextWidth = value.normalizedWidth;
      let nextHeight = value.normalizedHeight;
      let axis: AnnounceAxis;
      let announceValue: number;
      let refused: boolean;

      if (resizing) {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          axis = 'width';
          nextWidth = resizeSize(value.normalizedWidth, delta, value.normalizedX);
          announceValue = nextWidth;
          refused = nextWidth === value.normalizedWidth;
        } else {
          axis = 'height';
          nextHeight = resizeSize(value.normalizedHeight, delta, value.normalizedY);
          announceValue = nextHeight;
          refused = nextHeight === value.normalizedHeight;
        }
      } else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        axis = 'x';
        nextX = nudgePosition(value.normalizedX, delta, value.normalizedWidth);
        announceValue = nextX;
        refused = nextX === value.normalizedX;
      } else {
        axis = 'y';
        nextY = nudgePosition(value.normalizedY, delta, value.normalizedHeight);
        announceValue = nextY;
        refused = nextY === value.normalizedY;
      }

      emitNormalized(nextX, nextY, nextWidth, nextHeight, { run: arrowRunKey(resizing, axis) });
      queueAnnouncement(buildAnnouncement(axis, announceValue, refused ? edgeName(axis, delta) : null));
    },
    [value, emitNormalized, queueAnnouncement],
  );

  // Spec 154 FR-005: the held-arrow run closes on `keyup` of any arrow key —
  // not on an idle timer (OS key repeat fires `keydown` roughly every 30ms
  // with no intervening `keyup`, so a timer wide enough to hold a long run
  // together would also merge two deliberate, separate nudges). `endRun()`
  // with no key closes whichever run is open; a non-arrow keyup (Shift,
  // Ctrl alone) is not the end of a nudge and is left alone.
  const handleLabelKeyUp = useCallback(
    (event: ReactKeyboardEvent<HTMLDivElement>) => {
      if (ARROW_KEYS.has(event.key)) {
        endRun();
      }
    },
    [endRun],
  );

  // FR-012: named by its own text, falling back to a fixed, non-empty name.
  // Not `Overlay label: ${text}` (phase 6 should-fix 7) —
  // `aria-roledescription="Overlay label"` is already announced alongside
  // the accessible name, so that prefix made NVDA say "Overlay label" twice.
  const accessibleLabelName = value.text || 'No text set';

  // FR-014. `useId()`, not a module constant (phase 6 should-fix 8) — a
  // module constant collides the moment a document renders two editors.
  const instructionsId = useId();

  // Spec 154 (issue #2347) US2 — a discrete act, not a burst, so no debounce
  // (unlike `queueAnnouncement` above). Phase 6 review finding (#2344's
  // defect, twenty lines above its own fix): a same-string `setState` is a
  // React no-op — no DOM write — so a screen reader never re-announces a
  // repeated outcome, and `Ctrl+Z` pressed twice at the floor is exactly the
  // likeliest repeat ('Nothing to undo', twice). `queueAnnouncement`'s own
  // fix (`lastAnnouncedRef` + a toggled trailing zero-width space) does not
  // reuse cleanly here: its *visible* output alternates between "message"
  // and "message<ZWSP>" on repeats, harmless for a screen reader but not
  // byte-identical text on every call, and this announcer has no debounce
  // window to key a "was the last one the same" comparison against in the
  // first place. `token`, bumped on every call and used as the inner
  // `<span>`'s React `key`, forces a fresh DOM node on every announcement
  // instead — a real mutation even for back-to-back identical text, with no
  // leftover character.
  const [undoAnnouncement, setUndoAnnouncement] = useState({ text: '', token: 0 });

  const announceUndo = useCallback((message: string) => {
    setUndoAnnouncement((prev) => ({ text: message, token: prev.token + 1 }));
  }, []);

  const handleUndoClick = useCallback(() => {
    announceUndo(undo() ? 'Undone' : 'Nothing to undo');
  }, [undo, announceUndo]);

  const handleRedoClick = useCallback(() => {
    announceUndo(redo() ? 'Redone' : 'Nothing to redo');
  }, [redo, announceUndo]);

  // FR-006/FR-007: bound on the editor's root element, not the label, so
  // `Ctrl+Z`/`Cmd+Z`/`Ctrl+Shift+Z`/`Cmd+Shift+Z`/`Ctrl+Y` work from any
  // focus position inside the editor. `preventDefault` only on a handled
  // combination; everything else — `Ctrl+C`, `Ctrl+V`, the browser's own
  // shortcuts — falls through untouched.
  const handleRootKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLDivElement>) => {
      if (!event.ctrlKey && !event.metaKey) return;
      const key = event.key.toLowerCase();
      if (key === 'z' && event.shiftKey) {
        event.preventDefault();
        handleRedoClick();
        return;
      }
      if (key === 'z') {
        event.preventDefault();
        handleUndoClick();
        return;
      }
      if (key === 'y' && event.ctrlKey) {
        event.preventDefault();
        handleRedoClick();
      }
    },
    [handleUndoClick, handleRedoClick],
  );

  // Neither of these is lifted into `OverlayLabel` — `onChange` fires only for
  // text, font size and geometry, exactly as today (FR-005). The preview
  // camera is authoring-session state, never persisted (spec.md §Which camera).
  const [backdrop, setBackdrop] = useState<Backdrop>('checkerboard');
  const [capturedFrame, setCapturedFrame] = useState<string | null>(null);
  const [selectedCamera, setSelectedCamera] = useState('');
  const { state: captureState, activeCamera, capture, cancel, fail } = useFrameCapture();

  const handleCameraChange = useCallback(
    (next: string) => {
      setSelectedCamera(next);
      // FR-012: changing the camera mid-capture closes the old session; no
      // frame from it is ever applied. Called unconditionally — `cancel()` is
      // idempotent against `idle`, and this is also how picking a different
      // camera clears a stale `failed` alert instead of leaving the operator
      // staring at "try again, or pick a different camera" after doing
      // exactly that.
      cancel();
    },
    [cancel],
  );

  const handleCapture = useCallback(() => {
    if (selectedCamera === '') return;
    capture(selectedCamera);
  }, [selectedCamera, capture]);

  const handleCaptured = useCallback(
    (dataUrl: string) => {
      setCapturedFrame(dataUrl);
      setBackdrop('captured');
      cancel();
    },
    [cancel],
  );

  return (
    <div className={className} onKeyDown={handleRootKeyDown}>
      <div
        data-testid="overlay-editor-canvas"
        style={{
          position: 'relative',
          width: canvasWidthPx,
          height: canvasHeightPx,
          overflow: 'hidden',
          borderRadius: 8,
          ...canvasBackgroundStyle(backdrop, capturedFrame),
        }}
      >
        <Rnd
          size={{ width: pixelWidth, height: pixelHeight }}
          position={{ x: pixelX, y: pixelY }}
          bounds="parent"
          onDrag={handleDrag}
          onDragStop={handleDragStop}
          onResize={handleResize}
          onResizeStop={handleResizeStop}
          tabIndex={0}
          data-testid="overlay-editor-label"
          role="application"
          aria-roledescription="Overlay label"
          aria-label={accessibleLabelName}
          aria-describedby={instructionsId}
          onKeyDown={handleLabelKeyDown}
          onKeyUp={handleLabelKeyUp}
          onFocus={() => setIsLabelFocused(true)}
          onBlur={() => {
            setIsLabelFocused(false);
            endRun();
          }}
          style={{
            ...overlayLabelSurfaceStyle(value),
            cursor: 'move',
            userSelect: 'none',
            ...(isLabelFocused ? FOCUS_RING_STYLE : {}),
          }}
        >
          <span data-testid="overlay-editor-preview">{previewText || ' '}</span>
        </Rnd>
      </div>
      {/* FR-014/FR-015, plan.md §6 — siblings below the canvas `div`, not
          inside `<Rnd>`: `OverlayLabelParity.test.tsx` reaches the label
          through `overlay-editor-preview`'s `parentElement`, and PR #2360
          rewrites that same `<span>`. Keeping the `<Rnd>` subtree to its one
          existing child keeps both out of the way. */}
      <p id={instructionsId} className="sr-only">
        Arrow keys move this label; hold Shift for a larger step. Hold Ctrl with an arrow key to resize from the
        top-left corner; add Shift for a larger resize step.
      </p>
      {/* Phase 6 should-fix 6: `data-testid` so `OverlayEditorKeyboard.test.tsx`
          can find this region without relying on JSX order against
          `PlaceholderPreviewPanel`'s own `aria-live="polite"` region — two
          matches for the same attribute at the time this comment was
          written, and `querySelector` takes whichever happens to come first
          in the DOM. A third live region joined this tree in the phase 6
          fix round — `OverlayGeometryFields.tsx`'s `role="status"` advisory
          span — carrying its own `data-testid` for the same reason. */}
      <div aria-live="polite" data-testid="overlay-editor-geometry-live-region" className="sr-only">
        {liveMessage}
      </div>
      {/* Spec 154 (issue #2347), plan.md §6 — between the two sr-only regions
          above and the input grid below, so the label still precedes the
          text input in DOM order (`OverlayEditorKeyboard.test.tsx:103`).
          `type="button"` is mandatory: the editor renders inside
          `OverlayEditorDialog`'s `<form>`, and a bare `<button>` would
          submit it.

          `aria-disabled`, not the native `disabled` attribute (phase 6
          review finding): a browser blurs a focused element the instant it
          becomes natively disabled, so reaching the undo floor *by mouse*
          would drop focus to `<body>` — outside the editor root — and
          `handleRootKeyDown` would stop receiving `Ctrl+Z` until the
          operator clicked back in. `aria-disabled` keeps the control a
          normal, focusable, clickable element; `handleUndoClick`/
          `handleRedoClick` are what refuse to act, via `undo()`/`redo()`
          already returning `false` with nothing to do. */}
      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button
          type="button"
          data-testid="overlay-editor-undo"
          aria-keyshortcuts="Control+Z"
          aria-disabled={!canUndo}
          onClick={handleUndoClick}
        >
          Undo
        </button>
        <button
          type="button"
          data-testid="overlay-editor-redo"
          aria-keyshortcuts="Control+Shift+Z"
          aria-disabled={!canRedo}
          onClick={handleRedoClick}
        >
          Redo
        </button>
      </div>
      {/* US2 — its own live region, own `data-testid`, so it never collides
          with the geometry announcer above. A discrete act, not a burst; the
          inner `key` is `announceUndo`'s repeated-announcement fix — see its
          own comment. */}
      <div aria-live="polite" data-testid="overlay-editor-undo-live-region" className="sr-only">
        <span key={undoAnnouncement.token}>{undoAnnouncement.text}</span>
      </div>
      <div style={{ display: 'grid', gap: 12, marginTop: 12 }}>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <span>Label text</span>
          <input
            data-testid="overlay-editor-text"
            type="text"
            value={value.text}
            onChange={(e) => commit({ ...value, text: e.target.value }, { run: 'text' })}
            onBlur={() => endRun('text')}
            maxLength={256}
            style={{ padding: 8, fontSize: 14 }}
            aria-describedby={PLACEHOLDER_PREVIEW_STATUS_ID}
          />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <span>Font size: {value.fontSizePx}px</span>
          <input
            data-testid="overlay-editor-font-size"
            type="range"
            min={8}
            max={256}
            value={value.fontSizePx}
            onChange={(e) => commit({ ...value, fontSizePx: Number(e.target.value) }, { run: 'fontSize' })}
            onBlur={() => endRun('fontSize')}
            onPointerUp={() => endRun('fontSize')}
          />
        </label>
      </div>
      {/* Spec 151 (issue #2346), FR-001–FR-012/FR-017 — the four numeric
          geometry fields, below the existing controls and before the
          preview panel. */}
      <OverlayGeometryFields value={value} preview={preview} onCommit={handleGeometryCommit} />
      <PlaceholderPreviewPanel
        text={value.text}
        data={resolvedPreview}
        isFetching={isResolving}
        isError={resolveFailed}
      />
      <BackdropControls
        backdrop={backdrop}
        onBackdropChange={setBackdrop}
        hasCapturedFrame={capturedFrame !== null}
        getToken={getToken}
        selectedCamera={selectedCamera}
        onCameraChange={handleCameraChange}
        captureState={captureState}
        onCapture={handleCapture}
        onCancelCapture={cancel}
      />
      {/* Mounted only while a capture is in flight (spec 147 plan.md
          §The mechanism) — success, failure, timeout, cancel, camera change
          and unmount all release the WHEP session through this one unmount,
          never a hand-written release path (FR-010–FR-014). */}
      {activeCamera !== null && getToken !== undefined && (
        <FrameGrabber
          key={activeCamera}
          cameraIdentifier={activeCamera}
          getToken={getToken}
          onCaptured={handleCaptured}
          onFailed={fail}
        />
      )}
    </div>
  );
}
