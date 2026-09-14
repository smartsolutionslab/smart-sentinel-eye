import { useId, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { parsePercent, toPercentText } from './normalizedPercent.js';

/**
 * In-flight drag/resize geometry, normalized [0,1] (spec 151 FR-013). `null`
 * while no gesture is in progress, in which case a field falls back to
 * `value`.
 */
export interface OverlayGeometry {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** The one normalized field a single commit changes (spec 151 FR-006). */
export type OverlayGeometryField = 'normalizedX' | 'normalizedY' | 'normalizedWidth' | 'normalizedHeight';

export interface OverlayGeometryFieldsProps {
  /** The committed truth. */
  value: OverlayLabel;
  /** In-flight drag/resize geometry (FR-013), or `null` when nothing is dragging. */
  preview: OverlayGeometry | null;
  /** Fires once per successful commit, naming exactly the one field that changed (FR-006). */
  onCommit: (field: OverlayGeometryField, normalized: number) => void;
}

type FieldKind = 'position' | 'size';

interface FieldSpec {
  field: OverlayGeometryField;
  /** Matches spec 149's `AXIS_ANNOUNCE_LABEL` exactly (FR-001) — restated, not
   * imported, because importing from `OverlayEditor.tsx` would be a circular
   * import. A reviewer should check the four strings against each other. */
  label: string;
  kind: FieldKind;
  previewKey: keyof OverlayGeometry;
}

const FIELD_SPECS: FieldSpec[] = [
  { field: 'normalizedX', label: 'Left', kind: 'position', previewKey: 'x' },
  { field: 'normalizedY', label: 'Top', kind: 'position', previewKey: 'y' },
  { field: 'normalizedWidth', label: 'Width', kind: 'size', previewKey: 'width' },
  { field: 'normalizedHeight', label: 'Height', kind: 'size', previewKey: 'height' },
];

/** Validates a committed (parsed) normalized value; `null` means accepted. */
function validate(spec: FieldSpec, normalized: number): string | null {
  if (spec.kind === 'position') {
    if (normalized < 0 || normalized > 1) {
      return `${spec.label} must be between 0% and 100%.`;
    }
    return null;
  }
  // size — refused at 0 and below, and above 100% (#2361, from the guarded
  // side: a typed 0 is refused with a message, never floored).
  if (!(normalized > 0 && normalized <= 1)) {
    return `${spec.label} must be greater than 0% and at most 100%.`;
  }
  return null;
}

const FIELD_INPUT_STYLE = { padding: 8, fontSize: 14, width: '100%', boxSizing: 'border-box' as const };
const FIELD_ALERT_STYLE = { color: '#dc2626', fontSize: 12 };
const FIELD_STATUS_STYLE = { color: '#b45309', fontSize: 12 };

/**
 * Four percent-denominated fields — Left, Top, Width, Height — for an overlay
 * label's position and size (spec 151, issue #2346). Per plan.md §2:
 * `type="text"` + `inputMode="decimal"` (FR-003 — a `type="number"` input
 * blocks `OverlayEditorDialog`'s submit before `onSubmit` runs, which no
 * jsdom test can see); a draft-string commit/revert protocol on blur / Enter
 * / Escape (FR-004); validation against the normalized bound with a
 * percent-phrased message (FR-007–FR-011); a non-blocking `role="status"`
 * advisory when the committed rectangle runs off the canvas (FR-012); a
 * `<label htmlFor>` per field (FR-017).
 */
export function OverlayGeometryFields({ value, preview, onCommit }: OverlayGeometryFieldsProps) {
  const instanceId = useId();
  const [drafts, setDrafts] = useState<Partial<Record<OverlayGeometryField, string>>>({});
  const [errors, setErrors] = useState<Partial<Record<OverlayGeometryField, string>>>({});

  // plan.md §2/§3c: `draft ?? preview ?? value` (FR-004/FR-013). An in-flight
  // draft — what the operator is mid-typing — outranks a live drag/resize;
  // in practice the two never overlap (a mouse gesture and a keystroke
  // don't land in the same instant), so this only matters for that edge
  // case and otherwise resolves exactly as FR-013's "field tracks the drag"
  // requirement expects. Once a commit succeeds the draft is cleared (see
  // `commit`, below), so a real controlled parent's next render — a fresh
  // `value` — becomes visible again instead of being shadowed forever.
  function displayValue(spec: FieldSpec): string {
    const draft = drafts[spec.field];
    if (draft !== undefined) return draft;
    if (preview !== null) return toPercentText(preview[spec.previewKey]);
    return toPercentText(value[spec.field]);
  }

  function commit(spec: FieldSpec): void {
    const draft = drafts[spec.field];
    if (draft === undefined) return;

    const parsed = parsePercent(draft);
    if (parsed === null) {
      setErrors((prev) => ({ ...prev, [spec.field]: 'Enter a number.' }));
      return;
    }

    const message = validate(spec, parsed);
    if (message !== null) {
      setErrors((prev) => ({ ...prev, [spec.field]: message }));
      return;
    }

    // plan.md:173 — "clear draft + error, onCommit(field, parsed)". The
    // draft is cleared, not kept: a real controlled parent (`Controller`,
    // `OverlayEditorDialog.tsx:134-147`) feeds the committed value straight
    // back down as a new `value` prop, and `displayValue`'s `draft ??
    // preview ?? value` would otherwise let this stale draft shadow it
    // forever — including a *later, unrelated* change to `value` that has
    // nothing to do with this field (a reset, a reload).
    setDrafts((prev) => {
      const next = { ...prev };
      delete next[spec.field];
      return next;
    });
    setErrors((prev) => {
      const next = { ...prev };
      delete next[spec.field];
      return next;
    });
    onCommit(spec.field, parsed);
  }

  function handleKeyDown(spec: FieldSpec, event: ReactKeyboardEvent<HTMLInputElement>): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      commit(spec);
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      setDrafts((prev) => {
        const next = { ...prev };
        delete next[spec.field];
        return next;
      });
      setErrors((prev) => {
        const next = { ...prev };
        delete next[spec.field];
        return next;
      });
    }
  }

  // FR-012: driven by the committed value, and by the live preview while a
  // gesture is in progress — never by a draft, which may not parse at all.
  const advisoryX = preview?.x ?? value.normalizedX;
  const advisoryY = preview?.y ?? value.normalizedY;
  const advisoryWidth = preview?.width ?? value.normalizedWidth;
  const advisoryHeight = preview?.height ?? value.normalizedHeight;
  const clipsRight = advisoryX + advisoryWidth > 1;
  const clipsBottom = advisoryY + advisoryHeight > 1;
  const advisory = buildAdvisory(clipsRight, clipsBottom);

  return (
    <div style={{ display: 'grid', gap: 12, marginTop: 12 }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12 }}>
        {FIELD_SPECS.map((spec) => {
          const inputId = `${instanceId}-${spec.field}`;
          const errorId = `${instanceId}-${spec.field}-error`;
          const error = errors[spec.field];
          return (
            // The error span is a sibling of `<label>`, not a child of it:
            // `aria-describedby` only needs a matching id anywhere in the
            // document, and a `<label>` computes its accessible text from
            // *all* of its descendant text — nesting the error inside it
            // would fold "Enter a number." into the label RTL's
            // `getByLabelText('Width')` (and a screen reader's field name)
            // looks up.
            <div key={spec.field} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
              <label htmlFor={inputId} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                <span>{spec.label}</span>
                <input
                  id={inputId}
                  type="text"
                  inputMode="decimal"
                  value={displayValue(spec)}
                  onChange={(event) => setDrafts((prev) => ({ ...prev, [spec.field]: event.target.value }))}
                  onBlur={() => commit(spec)}
                  onKeyDown={(event) => handleKeyDown(spec, event)}
                  aria-describedby={error !== undefined ? errorId : undefined}
                  style={FIELD_INPUT_STYLE}
                />
              </label>
              {error !== undefined && (
                <span id={errorId} role="alert" style={FIELD_ALERT_STYLE}>
                  {error}
                </span>
              )}
            </div>
          );
        })}
      </div>
      {advisory !== null && (
        <span role="status" style={FIELD_STATUS_STYLE}>
          {advisory}
        </span>
      )}
    </div>
  );
}

/** FR-012 — phrased as what the wall will do, not as an error. */
function buildAdvisory(clipsRight: boolean, clipsBottom: boolean): string | null {
  if (!clipsRight && !clipsBottom) return null;
  if (clipsRight && clipsBottom) {
    return 'This label extends past the right edge and the bottom edge and will be clipped on the wall.';
  }
  if (clipsRight) {
    return 'This label extends past the right edge and will be clipped on the wall.';
  }
  return 'This label extends past the bottom edge and will be clipped on the wall.';
}
