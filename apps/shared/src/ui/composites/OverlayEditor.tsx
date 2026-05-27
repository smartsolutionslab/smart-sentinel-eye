import { useCallback } from 'react';
import { Rnd } from 'react-rnd';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

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
}

const MIN_NORMALIZED = 0;
const MAX_NORMALIZED = 1;

function clamp01(value: number): number {
  if (Number.isNaN(value)) return 0;
  if (value < MIN_NORMALIZED) return MIN_NORMALIZED;
  if (value > MAX_NORMALIZED) return MAX_NORMALIZED;
  return value;
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

  return (
    <div className={className}>
      <div
        data-testid="overlay-editor-canvas"
        style={{
          position: 'relative',
          width: canvasWidthPx,
          height: canvasHeightPx,
          background:
            'repeating-linear-gradient(45deg, #1f2937, #1f2937 12px, #111827 12px, #111827 24px)',
          overflow: 'hidden',
          borderRadius: 8,
        }}
      >
        <Rnd
          size={{ width: pixelWidth, height: pixelHeight }}
          position={{ x: pixelX, y: pixelY }}
          bounds="parent"
          onDragStop={(_e, data) =>
            emitGeometry(data.x, data.y, pixelWidth, pixelHeight)
          }
          onResizeStop={(_e, _dir, ref, _delta, position) =>
            emitGeometry(
              position.x,
              position.y,
              ref.offsetWidth,
              ref.offsetHeight,
            )
          }
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'rgba(255, 255, 255, 0.92)',
            color: '#111827',
            fontSize: value.fontSizePx,
            fontWeight: 600,
            border: '1px solid rgba(17, 24, 39, 0.4)',
            padding: '0 8px',
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
            onChange={(e) =>
              onChange({ ...value, fontSizePx: Number(e.target.value) })
            }
          />
        </label>
      </div>
    </div>
  );
}
