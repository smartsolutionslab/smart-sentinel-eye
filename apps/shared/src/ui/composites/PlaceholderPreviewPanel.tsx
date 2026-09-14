import type { ResolvedTextPreview } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { placeholderAdvisories, type PlaceholderAdvisoryKind } from './placeholderAdvisories.js';

export interface PlaceholderPreviewPanelProps {
  /** The raw, as-typed label text — the fallback shown before a response
   * arrives, and always the input `placeholderAdvisories` reasons about. */
  text: string;
  data: ResolvedTextPreview | undefined;
  isFetching: boolean;
  isError: boolean;
}

/**
 * `overlay-editor-text`'s `aria-describedby` target (`OverlayEditor.tsx`),
 * mirroring `LayoutEditorDialog.tsx:305-314`'s static-id association between
 * a field and its live status line. Exported rather than duplicated so the
 * two files cannot drift apart.
 */
export const PLACEHOLDER_PREVIEW_STATUS_ID = 'overlay-editor-advisory-status';

/**
 * Minimal severity distinction using existing design tokens only (phase 6
 * should-fix 6) — a fuller treatment is #2342, gated on #2332. `resolved`
 * stays the default text color; `unknown` is the one outcome that means the
 * reference is flatly wrong, so it gets the fault token; everything else
 * (`unset`, `archived`, `malformed`) is "will not render as intended, but
 * not necessarily wrong" and gets the warning token.
 */
function severityClassName(kind: PlaceholderAdvisoryKind): string {
  if (kind === 'unknown') return 'text-accent-fault';
  if (kind === 'resolved') return '';
  return 'text-accent-warning';
}

/**
 * Spec 148 US1 + US4 — the advisory panel below the overlay label field. A
 * side panel rather than a popup: it must never intercept the click on
 * *Save as draft* the two wall e2e seed setups depend on (spec 148 plan.md
 * "Frontend wiring"; CLAUDE.md hazard 1).
 *
 * <p><b>Never blocking</b> (spec 148 decision 3, ADR-0115). This component
 * has no submit control and never disables one elsewhere — an unresolved
 * reference or a failed request is reported here and nowhere stops the
 * form.</p>
 */
export function PlaceholderPreviewPanel({ text, data, isFetching, isError }: PlaceholderPreviewPanelProps) {
  const rows = placeholderAdvisories(text, data);
  const previewText = data?.resolvedText ?? text;

  let status: string;
  if (isFetching) {
    status = 'Resolving…';
  } else if (isError) {
    status = 'Could not check this text right now.';
  } else if (rows.length === 0) {
    status = 'No references to check.';
  } else {
    status = `${rows.length} reference${rows.length === 1 ? '' : 's'}.`;
  }

  return (
    <div data-testid="placeholder-preview-panel" role="region" aria-label="Placeholder preview">
      <p data-testid="placeholder-preview-text">{previewText}</p>
      <p
        id={PLACEHOLDER_PREVIEW_STATUS_ID}
        aria-live="polite"
        className="text-sm text-fg-muted"
        data-testid={isError ? 'placeholder-preview-error' : undefined}
      >
        {status}
      </p>
      {rows.length > 0 && (
        <ul>
          {rows.map((row, index) => (
            <li
              key={`${row.kind}-${row.reference}-${index}`}
              data-testid={`placeholder-advisory-${row.kind}`}
              className={severityClassName(row.kind)}
            >
              {row.message}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
