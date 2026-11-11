import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { Input } from '../../primitives/Input';
import { PLACEHOLDER_PREVIEW_STATUS_ID } from '../PlaceholderPreviewPanel';
import type { Boundary, RunKey } from '../useOverlayEditHistory';

interface LabelFieldsProps {
  label: OverlayLabel;
  onChange: (next: OverlayLabel, boundary: Boundary) => void;
  endRun: (key?: RunKey) => void;
}

export function LabelFields({ label, onChange, endRun }: LabelFieldsProps) {
  return (
    <div className="mt-3 grid gap-3">
      <label className="flex flex-col gap-1">
        <span className="text-sm font-medium text-fg-primary">Label text</span>
        <Input
          data-testid="overlay-editor-text"
          type="text"
          value={label.text}
          onChange={(e) => onChange({ ...label, text: e.target.value }, { run: 'text' })}
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
          onChange={(e) => onChange({ ...label, fontSizePx: Number(e.target.value) }, { run: 'fontSize' })}
          onBlur={() => endRun('fontSize')}
          onPointerUp={() => endRun('fontSize')}
          className="w-full accent-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring"
        />
      </label>
    </div>
  );
}
