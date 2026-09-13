import { useCallback, useState } from 'react';
import type { CSSProperties } from 'react';
import { Rnd } from 'react-rnd';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { overlayLabelSurfaceStyle } from './overlayLabelStyle.js';
import { BackdropControls } from './BackdropControls.js';
import type { Backdrop } from './BackdropControls.js';
import { FrameGrabber } from './FrameGrabber.js';
import { useFrameCapture } from './useFrameCapture.js';

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
}

const MIN_NORMALIZED = 0;
const MAX_NORMALIZED = 1;

function clamp01(value: number): number {
  if (Number.isNaN(value)) return 0;
  if (value < MIN_NORMALIZED) return MIN_NORMALIZED;
  if (value > MAX_NORMALIZED) return MAX_NORMALIZED;
  return value;
}

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
 * the text input updates the label text. All four normalized values
 * are clamped to [0, 1] before <c>onChange</c> fires.
 */
export function OverlayEditor({
  value,
  onChange,
  canvasWidthPx = 800,
  canvasHeightPx = 450,
  className,
  getToken,
}: OverlayEditorProps) {
  const pixelX = value.normalizedX * canvasWidthPx;
  const pixelY = value.normalizedY * canvasHeightPx;
  const pixelWidth = Math.max(value.normalizedWidth * canvasWidthPx, 24);
  const pixelHeight = Math.max(value.normalizedHeight * canvasHeightPx, 16);

  const emitGeometry = useCallback(
    (xPx: number, yPx: number, widthPx: number, heightPx: number) => {
      onChange({
        ...value,
        normalizedX: clamp01(xPx / canvasWidthPx),
        normalizedY: clamp01(yPx / canvasHeightPx),
        normalizedWidth: clamp01(widthPx / canvasWidthPx),
        normalizedHeight: clamp01(heightPx / canvasHeightPx),
      });
    },
    [canvasWidthPx, canvasHeightPx, onChange, value],
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
      // frame from it is ever applied.
      if (activeCamera !== null) cancel();
    },
    [activeCamera, cancel],
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

  const handleFailed = useCallback(() => {
    fail();
  }, [fail]);

  return (
    <div className={className}>
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
          onDragStop={(_e, data) => emitGeometry(data.x, data.y, pixelWidth, pixelHeight)}
          onResizeStop={(_e, _dir, ref, _delta, position) =>
            emitGeometry(position.x, position.y, ref.offsetWidth, ref.offsetHeight)
          }
          style={{
            ...overlayLabelSurfaceStyle(value),
            cursor: 'move',
            userSelect: 'none',
          }}
        >
          <span data-testid="overlay-editor-preview">{value.text || ' '}</span>
        </Rnd>
      </div>
      <div style={{ display: 'grid', gap: 12, marginTop: 12 }}>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <span>Label text</span>
          <input
            data-testid="overlay-editor-text"
            type="text"
            value={value.text}
            onChange={(e) => onChange({ ...value, text: e.target.value })}
            maxLength={256}
            style={{ padding: 8, fontSize: 14 }}
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
            onChange={(e) => onChange({ ...value, fontSizePx: Number(e.target.value) })}
          />
        </label>
      </div>
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
          cameraIdentifier={activeCamera}
          getToken={getToken}
          onCaptured={handleCaptured}
          onFailed={handleFailed}
        />
      )}
    </div>
  );
}
