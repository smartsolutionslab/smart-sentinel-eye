import { Fragment, useCallback, useId, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { Rnd } from 'react-rnd';
import type { OverlayElement, OverlayLabel, OverlayShape } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { ResolvedTextPreview } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { overlayLabelSurfaceStyle, overlayShapeStyle } from './overlayLabelStyle.js';
import { Button } from '../primitives/Button.js';
import { Input } from '../primitives/Input.js';
import { BackdropControls } from './BackdropControls.js';
import type { Backdrop, CameraChoicesResult } from './BackdropControls.js';
import { useFrameCapture } from './useFrameCapture.js';
import { PlaceholderPreviewPanel, PLACEHOLDER_PREVIEW_STATUS_ID } from './PlaceholderPreviewPanel.js';
import { arrowRunKey, buildAnnouncement, edgeName, type AnnounceAxis } from './overlayEditor/overlayAnnouncement.js';
import { COARSE_STEP, FINE_STEP, nudgePosition, resizeSize } from './overlayEditor/overlayGeometry.js';
import { OverlayColorFields } from './OverlayColorFields.js';
import { OverlayGeometryFields } from './OverlayGeometryFields.js';
import type { OverlayGeometryField } from './OverlayGeometryFields.js';
import { canvasBackgroundStyle, FOCUS_RING_STYLE } from './overlayEditor/canvasStyle.js';
import { useCanvasGestures } from './overlayEditor/useCanvasGestures.js';
import { useEmitNormalized } from './overlayEditor/useEmitNormalized.js';
import { useUndoRedoControls } from './overlayEditor/useUndoRedoControls.js';
import { useGeometryAnnouncer } from './overlayEditor/useGeometryAnnouncer.js';
import { InertElement } from './overlayEditor/InertElement.js';
import { useOverlayEditHistory } from './useOverlayEditHistory.js';
import type { OverlayEditorValue } from './useOverlayEditHistory.js';

export type { OverlayEditorValue };

function isOverlayShape(value: OverlayEditorValue): value is OverlayShape {
  return value.kind === 'Box' || value.kind === 'Ellipse';
}

export interface OverlayEditorProps<T extends OverlayEditorValue = OverlayEditorValue> {
  value: T;
  /**
   * Spec 441: the other elements of the set, painted inert on the canvas
   * (aria-hidden, pointer-events none) below and above the one interactive
   * label, in ordinal order. Absent renders exactly as before.
   */
  inertElements?: { below: readonly OverlayElement[]; above: readonly OverlayElement[] };
  onChange: (next: T) => void;
  /** Forwarded to the colour field: false while it holds a refused value. */
  onColourValidityChange?: (valid: boolean) => void;
  /** Forwarded to the colour field so the keyboard cannot change a read-only element. */
  readOnly?: boolean;
  /**
   * Pixel-space backdrop the label is positioned over. Defaults to a 16:9
   * 800x450 canvas — a caller with a measured size (spec 305, #2350,
   * `useCanvasFit`) passes its own `widthPx`/`heightPx` here instead; this
   * default is only what a caller with no measurement falls back to. The
   * fixed aspect ratio keeps normalized coordinates resolution-independent
   * either way.
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
   * Spec 148 US1 + US3 — the caller (`OverlayDraftForm`) owns the debounce
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
  /**
   * The caller's `useListAllCameraChoicesQuery()` result, forwarded to
   * `BackdropControls`. Same ADR-0169 reasoning as `resolvedPreview` above —
   * this composite does not call the hook itself. Meaningful only alongside
   * `getToken`; a caller that omits one but not the other gets no camera
   * picker (`BackdropControls`'s own gate), same as before this prop existed.
   * `data.complete` — the response's own truncation signal (spec 048) —
   * travels through this prop untouched; `CameraCaptureSection` inside
   * `BackdropControls.tsx` is what actually reads it and renders the
   * "Showing N of M cameras" notice, since it is the component that owns
   * the picker UI.
   */
  cameras?: CameraChoicesResult;
  /** The camera the picker starts on, for a host that already knows the tile (spec 466). Absent keeps today's empty start. */
  initialCamera?: string;
  /**
   * Renders the capture-in-flight `FrameGrabber` (spec 147), given the three
   * things only `OverlayEditor` knows (which camera, and where to deliver the
   * result). ADR-0169: `FrameGrabber` itself takes `stream` as a prop rather
   * than calling `useGetStreamQuery`, so the caller — who owns a `<Provider>`
   * — is the one who can call that hook; this is the seam that lets it. Absent
   * alongside `getToken` ⇒ no capture control is offered (same FR-017 shape).
   */
  renderFrameGrabber?: (props: {
    cameraIdentifier: string;
    onCaptured: (dataUrl: string) => void;
    onFailed: () => void;
  }) => ReactNode;
  /**
   * The layer list's name for a Box or Ellipse (e.g. "Box 1"), so the canvas
   * node is announced under the same name as the selected layer chip. Absent
   * falls back to the kind. A label is always named by its own text.
   */
  layerName?: string;
}

const ARROW_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);

/**
 * WYSIWYG label editor (spec 004 T059). A fixed-aspect canvas
 * surfaces a draggable + resizable label preview backed by
 * <c>react-rnd</c>; sliders below the canvas tune the font size and
 * the text input updates the label text. <c>x</c>/<c>y</c> are clamped
 * to [0, 1] and <c>width</c>/<c>height</c> to <c>(0, 1]</c>, with a
 * non-positive or <c>NaN</c> value floored to <c>MIN_NORMALIZED_SIZE</c>,
 * before <c>onChange</c> fires (#2361).
 */
export function OverlayEditor<T extends OverlayEditorValue = OverlayEditorValue>({
  value,
  inertElements,
  onChange,
  onColourValidityChange,
  readOnly = false,
  canvasWidthPx = 800,
  canvasHeightPx = 450,
  className,
  getToken,
  resolvedPreview,
  isResolving = false,
  resolveFailed = false,
  cameras,
  renderFrameGrabber,
  initialCamera = '',
  layerName,
}: OverlayEditorProps<T>) {
  // US3: the canvas box sizes around the string the wall will actually show.
  // Raw text is the fallback while the preview is in flight, has failed, or
  // has not been requested at all (no `{{` — `resolvedPreview` never arrives)
  // — never empty, never a placeholder of its own (spec 148 US3 scenario 3).
  const shape = isOverlayShape(value) ? value : undefined;
  const label = shape === undefined ? (value as OverlayLabel) : undefined;
  const isShape = shape !== undefined;
  const previewText = resolvedPreview?.resolvedText ?? label?.text ?? '';
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

  const emitNormalized = useEmitNormalized(value, commit);

  const { preview, handleDrag, handleDragStop, handleResize, handleResizeStop } = useCanvasGestures({
    emitNormalized,
    canvasWidthPx,
    canvasHeightPx,
    pixelWidth,
    pixelHeight,
  });

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

  // Spec 463 (issue #3108): one accepted colour is one undo step, like a geometry commit.
  const handleColorCommit = useCallback(
    (color: string) => {
      commit({ ...value, color }, 'atomic');
    },
    [commit, value],
  );

  // FR-002/FR-003: driven by onFocus/onBlur, not `:focus-visible` — the ring
  // sits on `Rnd`'s own inline `style` (state-driven, spec 293 §3) and a
  // pseudo-class cannot be expressed inline. A mouse click also raises the
  // ring; that is deliberate (spec.md FR-003).
  const [isLabelFocused, setIsLabelFocused] = useState(false);

  const { liveMessage, queueAnnouncement } = useGeometryAnnouncer();

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
  const accessibleLabelName = shape !== undefined ? (layerName ?? shape.kind) : label?.text || 'No text set';

  // FR-014. `useId()`, not a module constant (phase 6 should-fix 8) — a
  // module constant collides the moment a document renders two editors.
  const instructionsId = useId();

  const { undoAnnouncement, handleUndoClick, handleRedoClick, handleRootKeyDown } = useUndoRedoControls({ undo, redo });

  // Neither of these is lifted into `OverlayLabel` — `onChange` fires only for
  // text, font size and geometry, exactly as today (FR-005). The preview
  // camera is authoring-session state, never persisted (spec.md §Which camera).
  const [backdrop, setBackdrop] = useState<Backdrop>('checkerboard');
  const [capturedFrame, setCapturedFrame] = useState<string | null>(null);
  const [selectedCamera, setSelectedCamera] = useState(initialCamera);
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
        className="relative overflow-hidden rounded-lg @container"
        style={{
          width: canvasWidthPx,
          height: canvasHeightPx,
          ...canvasBackgroundStyle(backdrop, capturedFrame),
        }}
      >
        {inertElements?.below.map((element, index) => (
          <InertElement
            key={`below-${index}`}
            element={element}
            canvasWidthPx={canvasWidthPx}
            canvasHeightPx={canvasHeightPx}
          />
        ))}
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
          aria-roledescription={isShape ? 'Overlay shape' : 'Overlay label'}
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
            ...(shape !== undefined ? overlayShapeStyle(shape) : overlayLabelSurfaceStyle(value as OverlayLabel)),
            cursor: 'move',
            userSelect: 'none',
            ...(isLabelFocused ? FOCUS_RING_STYLE : {}),
          }}
        >
          {!isShape && <span data-testid="overlay-editor-preview">{previewText || ' '}</span>}
        </Rnd>
        {inertElements?.above.map((element, index) => (
          <InertElement
            key={`above-${index}`}
            element={element}
            canvasWidthPx={canvasWidthPx}
            canvasHeightPx={canvasHeightPx}
          />
        ))}
      </div>
      {/* FR-014/FR-015, plan.md §6 — siblings below the canvas `div`, not
          inside `<Rnd>`: `OverlayLabelParity.test.tsx` reaches the label
          through `overlay-editor-preview`'s `parentElement`, and PR #2360
          rewrites that same `<span>`. Keeping the `<Rnd>` subtree to its one
          existing child keeps both out of the way. */}
      <p id={instructionsId} className="sr-only">
        Arrow keys move this {isShape ? 'shape' : 'label'}; hold Shift for a larger step. Hold Ctrl with an arrow key to
        resize from the top-left corner; add Shift for a larger resize step.
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
          `OverlayDraftForm`'s `<form>`, and a bare `<button>` would
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
      <div className="mt-3 flex gap-2">
        <Button
          variant="secondary"
          data-testid="overlay-editor-undo"
          aria-keyshortcuts="Control+Z"
          unavailable={!canUndo}
          onClick={handleUndoClick}
        >
          Undo
        </Button>
        <Button
          variant="secondary"
          data-testid="overlay-editor-redo"
          aria-keyshortcuts="Control+Shift+Z"
          unavailable={!canRedo}
          onClick={handleRedoClick}
        >
          Redo
        </Button>
      </div>
      {/* US2 — its own live region, own `data-testid`, so it never collides
          with the geometry announcer above. A discrete act, not a burst; the
          inner `key` is `useUndoRedoControls`' repeated-announcement fix — see
          its own comment. */}
      <div aria-live="polite" data-testid="overlay-editor-undo-live-region" className="sr-only">
        <span key={undoAnnouncement.token}>{undoAnnouncement.text}</span>
      </div>
      {label !== undefined && (
        <div className="mt-3 grid gap-3">
          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium text-fg-primary">Label text</span>
            <Input
              data-testid="overlay-editor-text"
              type="text"
              value={label.text}
              onChange={(e) => commit({ ...label, text: e.target.value } as T, { run: 'text' })}
              onBlur={() => endRun('text')}
              maxLength={256}
              aria-describedby={PLACEHOLDER_PREVIEW_STATUS_ID}
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium text-fg-primary">Font size: {label.fontSizePx}px</span>
            <input
              data-testid="overlay-editor-font-size"
              type="range"
              min={8}
              max={256}
              value={label.fontSizePx}
              onChange={(e) => commit({ ...label, fontSizePx: Number(e.target.value) } as T, { run: 'fontSize' })}
              onBlur={() => endRun('fontSize')}
              onPointerUp={() => endRun('fontSize')}
              className="w-full accent-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring"
            />
          </label>
        </div>
      )}
      {/* Spec 151 (issue #2346), FR-001–FR-012/FR-017 — the four numeric
          geometry fields, below the existing controls and before the
          preview panel. */}
      <OverlayGeometryFields value={value} preview={preview} onCommit={handleGeometryCommit} />
      <OverlayColorFields
        value={value}
        onCommit={handleColorCommit}
        onValidityChange={onColourValidityChange}
        readOnly={readOnly}
      />
      {label !== undefined && (
        <PlaceholderPreviewPanel
          text={label.text}
          data={resolvedPreview}
          isFetching={isResolving}
          isError={resolveFailed}
        />
      )}
      <BackdropControls
        backdrop={backdrop}
        onBackdropChange={setBackdrop}
        hasCapturedFrame={capturedFrame !== null}
        // Phase-6/review should-fix: `getToken`, `cameras` and
        // `renderFrameGrabber` are three independently optional props, but
        // FR-017's degradation is all-or-nothing — "absent ⇒ not offered at
        // all". `BackdropControls` only gates on `getToken`/`cameras`, so a
        // caller that wires the first two but not `renderFrameGrabber` would
        // otherwise get a Capture button with nothing to mount when pressed
        // (silently failing the 10s timeout). Gating `getToken` here on
        // `renderFrameGrabber` too restores the single guarantee without
        // reshaping the props into one object (out of scope for this issue).
        getToken={renderFrameGrabber !== undefined ? getToken : undefined}
        cameras={cameras}
        selectedCamera={selectedCamera}
        onCameraChange={handleCameraChange}
        captureState={captureState}
        onCapture={handleCapture}
        onCancelCapture={cancel}
      />
      {/* Mounted only while a capture is in flight (spec 147 plan.md
          §The mechanism) — success, failure, timeout, cancel, camera change
          and unmount all release the WHEP session through this one unmount,
          never a hand-written release path (FR-010–FR-014). The caller
          renders the actual `FrameGrabber` (ADR-0169: it owns `useGetStreamQuery`,
          which needs a `<Provider>`); this only decides when. */}
      {activeCamera !== null && getToken !== undefined && renderFrameGrabber !== undefined && (
        <Fragment key={activeCamera}>
          {renderFrameGrabber({ cameraIdentifier: activeCamera, onCaptured: handleCaptured, onFailed: fail })}
        </Fragment>
      )}
    </div>
  );
}
