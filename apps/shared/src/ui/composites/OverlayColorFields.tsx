import { useEffect, useId, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';
import { DEFAULT_OVERLAY_COLOR, boxSchema, textSchema } from '../../api/overlays.schema.js';
import type { OverlayEditorValue } from './useOverlayEditHistory.js';
import { Input } from '../primitives/Input.js';
import { ToggleChip } from '../primitives/ToggleChip.js';
import { OVERLAY_SWATCHES } from './overlayPalette.js';
import { minimumTextAlpha } from './textLegibility.js';

export interface OverlayColorFieldsProps {
  /** The committed element; only its kind and colour are read. */
  value: Pick<OverlayEditorValue, 'kind' | 'color'>;
  /** Fires once per accepted colour with the canonical upper-case `#RRGGBBAA`. */
  onCommit: (color: string) => void;
  /** Reports whether the field is free of a refused value; false from a refusal until it is corrected or committed. */
  onValidityChange?: (valid: boolean) => void;
  readOnly?: boolean;
}

const ALERT_CLASSNAME = 'text-xs text-accent-fault';
const FIELDSET_CLASSNAME = 'm-0 border-0 p-0';
const LEGEND_CLASSNAME = 'mb-1 p-0 text-sm font-medium text-fg-primary';

const PROBE_GEOMETRY = { normalizedX: 0, normalizedY: 0, normalizedWidth: 1, normalizedHeight: 1 };

/**
 * Runs `candidate` through the same schema the form and the API use, so the editor refuses
 * exactly what they would: the first colour issue is the message, the parsed colour the canonical form.
 */
function check(kind: OverlayEditorValue['kind'], candidate: string): { color: string } | { error: string } {
  const result =
    kind === 'Text'
      ? textSchema.safeParse({ kind, color: candidate, text: 'T', fontSizePx: 8, ...PROBE_GEOMETRY })
      : boxSchema.safeParse({ kind: 'Box', color: candidate, ...PROBE_GEOMETRY });
  if (result.success) return { color: result.data.color };
  const issue = result.error.issues.find((entry) => entry.path[0] === 'color') ?? result.error.issues[0]!;
  return { error: issue.message };
}

function alphaOf(color: string): number {
  return parseInt(color.slice(7, 9), 16);
}

/**
 * Colour for the selected element (spec 463, issue #3108): swatches, a hex field and an opacity
 * range. A Text label's opacity floor is the legibility minimum for its RGB; a shape has none.
 */
export function OverlayColorFields({ value, onCommit, onValidityChange, readOnly = false }: OverlayColorFieldsProps) {
  const instanceId = useId();
  const [draft, setDraft] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    onValidityChange?.(error === null);
  }, [error, onValidityChange]);
  useEffect(() => () => onValidityChange?.(true), [onValidityChange]);

  const current = (value.color ?? DEFAULT_OVERLAY_COLOR).toUpperCase();
  const minimumAlpha = value.kind === 'Text' ? parseInt(minimumTextAlpha(current.slice(1, 7)), 16) : 0;

  function commit(candidate: string): void {
    const outcome = check(value.kind, candidate);
    if ('error' in outcome) {
      setError(outcome.error);
      return;
    }
    setDraft(undefined);
    setError(null);
    onCommit(outcome.color);
  }

  function commitDraft(): void {
    if (draft === undefined) return;
    commit(draft.trim());
  }

  function handleKeyDown(event: ReactKeyboardEvent<HTMLInputElement>): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      commitDraft();
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      setDraft(undefined);
      setError(null);
    }
  }

  const hexId = `${instanceId}-hex`;
  const opacityId = `${instanceId}-opacity`;
  const errorId = `${instanceId}-error`;

  return (
    <fieldset className={`${FIELDSET_CLASSNAME} mt-3 grid gap-3`}>
      <legend className={LEGEND_CLASSNAME}>Colour</legend>
      <div className="flex flex-wrap gap-2" role="group" aria-label="Colour swatches">
        {OVERLAY_SWATCHES.map((swatch) => (
          <ToggleChip
            key={swatch.name}
            pressed={current === swatch.value}
            disabled={readOnly}
            aria-label={swatch.name}
            title={swatch.name}
            onClick={() => commit(swatch.value)}
          >
            <span
              aria-hidden="true"
              className="inline-block h-4 w-4 rounded-sm border border-border-subtle align-middle"
              style={{ backgroundColor: swatch.value }}
            />
          </ToggleChip>
        ))}
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={hexId} className="text-sm font-medium text-fg-primary">
          Colour
        </label>
        <Input
          id={hexId}
          type="text"
          value={draft ?? current}
          readOnly={readOnly}
          onChange={(event) => {
            setDraft(event.target.value);
            setError(null);
          }}
          onBlur={commitDraft}
          onKeyDown={handleKeyDown}
          aria-describedby={error !== null ? errorId : undefined}
        />
        {error !== null && (
          <span id={errorId} role="alert" className={ALERT_CLASSNAME}>
            {error}
          </span>
        )}
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={opacityId} className="text-sm font-medium text-fg-primary">
          Opacity
        </label>
        <input
          id={opacityId}
          type="range"
          min={minimumAlpha}
          max={255}
          value={alphaOf(current)}
          disabled={readOnly}
          onChange={(event) => {
            const alpha = Number(event.target.value).toString(16).toUpperCase().padStart(2, '0');
            commit(`${current.slice(0, 7)}${alpha}`);
          }}
          className="w-full accent-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring"
        />
      </div>
    </fieldset>
  );
}
