import { useId, useState } from 'react';
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
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

const NOT_A_NUMBER_MESSAGE = 'Enter a number.';

/** The refusal message for `spec.kind`, whatever the actual bound violation (spec 256 plan §2.1). */
function rangeMessage(spec: FieldSpec): string {
  if (spec.kind === 'position') {
    return `${spec.label} must be between 0% and 100%.`;
  }
  return `${spec.label} must be greater than 0% and at most 100%.`;
}

/** Every message `commit`/`committedValueError` can show for this field (spec 256 plan §2.1). */
function messagesFor(spec: FieldSpec): string[] {
  return [NOT_A_NUMBER_MESSAGE, rangeMessage(spec)];
}

/** Validates a committed (parsed) normalized value; `null` means accepted. */
function validate(spec: FieldSpec, normalized: number): string | null {
  if (spec.kind === 'position') {
    if (normalized < 0 || normalized > 1) {
      return rangeMessage(spec);
    }
    return null;
  }
  // size — refused at 0 and below, and above 100% (#2361, from the guarded
  // side: a typed 0 is refused with a message, never floored).
  if (!(normalized > 0 && normalized <= 1)) {
    return rangeMessage(spec);
  }
  return null;
}

const FIELD_INPUT_STYLE = { padding: 8, fontSize: 14, width: '100%', boxSizing: 'border-box' as const };
const FIELD_ALERT_STYLE = { color: '#dc2626', fontSize: 12 };
const FIELD_STATUS_STYLE = { color: '#b45309', fontSize: 12 };
// Nit 9 (phase 6): matches `BackdropControls.tsx`'s own `<fieldset><legend>`
// pattern — the four fields otherwise have no accessible group name.
const FIELDSET_STYLE = { border: 'none', padding: 0, margin: 0 };
const LEGEND_STYLE = { fontSize: 14, padding: 0, marginBottom: 4 };

/**
 * Reserves the vertical space of the tallest message `candidates` can ever
 * contain, at whatever width the slot actually renders — a static min-height
 * can't do this because a size-field refusal wraps to a different number of
 * lines depending on width (spec 256 plan §2.2). Every candidate is stacked in
 * the same grid cell as `children` (`gridArea: '1 / 1'`), so the cell's height
 * is always the tallest candidate at the current width; this is what stops a
 * blur-triggered message from moving the dialog's Save button out from under
 * an in-flight `mousedown`+`mouseup` (issue #2366). The candidates are
 * `visibility: hidden` (laid out, unpainted) and `aria-hidden="true"` (kept out
 * of the accessibility tree) — they never carry a `role`, so they cannot be
 * picked up by `getByRole('alert')` / `getByRole('status')`.
 */
function ReservedMessageSlot({
  candidates,
  textStyle,
  testId,
  children,
}: {
  candidates: string[];
  textStyle: CSSProperties;
  testId: string;
  children: ReactNode;
}) {
  return (
    <div data-testid={testId} style={{ display: 'grid' }}>
      {candidates.map((text) => (
        <span key={text} aria-hidden="true" style={{ ...textStyle, gridArea: '1 / 1', visibility: 'hidden' }}>
          {text}
        </span>
      ))}
      <div style={{ gridArea: '1 / 1' }}>{children}</div>
    </div>
  );
}

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
      setErrors((prev) => ({ ...prev, [spec.field]: NOT_A_NUMBER_MESSAGE }));
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

  // Should-fix 3 (phase 6): `commit` only ever validates a *typed* draft, so
  // a drag-produced value never passes through `validate` at all — a
  // drag-produced zero-size label reaches `value` with no message anywhere
  // (#2361 stays open: this adds the message, not a clamp — the drag path's
  // own emitted payload is untouched). Only applies once the field has
  // settled onto `value` (no standing draft, no live gesture) — while either
  // is present the field shows *that* number, not `value`, and `commit`
  // already owns validation for a draft.
  function committedValueError(spec: FieldSpec): string | null {
    if (drafts[spec.field] !== undefined || preview !== null) return null;
    return validate(spec, value[spec.field]);
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
      <fieldset style={FIELDSET_STYLE}>
        <legend style={LEGEND_STYLE}>Position and size</legend>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12 }}>
          {FIELD_SPECS.map((spec) => {
            const inputId = `${instanceId}-${spec.field}`;
            const errorId = `${instanceId}-${spec.field}-error`;
            const error = errors[spec.field] ?? committedValueError(spec) ?? undefined;
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
                    onChange={(event) => {
                      setDrafts((prev) => ({ ...prev, [spec.field]: event.target.value }));
                      // Nit 8 (phase 6): a standing error otherwise keeps
                      // describing a value the operator has already changed
                      // — a screen reader re-announces a stale description.
                      setErrors((prev) => {
                        if (prev[spec.field] === undefined) return prev;
                        const next = { ...prev };
                        delete next[spec.field];
                        return next;
                      });
                    }}
                    onBlur={() => commit(spec)}
                    onKeyDown={(event) => handleKeyDown(spec, event)}
                    aria-describedby={error !== undefined ? errorId : undefined}
                    style={FIELD_INPUT_STYLE}
                  />
                </label>
                <ReservedMessageSlot
                  candidates={messagesFor(spec)}
                  textStyle={FIELD_ALERT_STYLE}
                  testId={`overlay-geometry-message-slot-${spec.field}`}
                >
                  {error !== undefined && (
                    <span id={errorId} role="alert" style={FIELD_ALERT_STYLE}>
                      {error}
                    </span>
                  )}
                </ReservedMessageSlot>
              </div>
            );
          })}
        </div>
      </fieldset>
      {/* Should-fix 4 (phase 6): rendered unconditionally, not mounted only
          while `advisory !== null` — a live region inserted into the DOM at
          the same instant as its content is not reliably announced.
          `OverlayEditor.tsx:534` already does it this way in the same tree:
          always rendered, content changes. */}
      <ReservedMessageSlot
        candidates={ADVISORY_WORDINGS}
        textStyle={FIELD_STATUS_STYLE}
        testId="overlay-geometry-advisory-slot"
      >
        <span role="status" data-testid="overlay-geometry-advisory" style={FIELD_STATUS_STYLE}>
          {advisory ?? ''}
        </span>
      </ReservedMessageSlot>
    </div>
  );
}

const ADVISORY_BOTH_EDGES_MESSAGE =
  'This label extends past the right edge and the bottom edge and will be clipped on the wall.';
const ADVISORY_RIGHT_EDGE_MESSAGE = 'This label extends past the right edge and will be clipped on the wall.';
const ADVISORY_BOTTOM_EDGE_MESSAGE = 'This label extends past the bottom edge and will be clipped on the wall.';

/** Every wording `buildAdvisory` can return (spec 256 plan §2.1). */
const ADVISORY_WORDINGS: string[] = [
  ADVISORY_BOTH_EDGES_MESSAGE,
  ADVISORY_RIGHT_EDGE_MESSAGE,
  ADVISORY_BOTTOM_EDGE_MESSAGE,
];

/** FR-012 — phrased as what the wall will do, not as an error. */
function buildAdvisory(clipsRight: boolean, clipsBottom: boolean): string | null {
  if (!clipsRight && !clipsBottom) return null;
  if (clipsRight && clipsBottom) {
    return ADVISORY_BOTH_EDGES_MESSAGE;
  }
  if (clipsRight) {
    return ADVISORY_RIGHT_EDGE_MESSAGE;
  }
  return ADVISORY_BOTTOM_EDGE_MESSAGE;
}
