import type { ResolvedTextPreview } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { placeholderAdvisories } from './placeholderAdvisories.js';

export interface PlaceholderPreviewPanelProps {
  /** The raw, as-typed label text — the fallback shown before a response
   * arrives, and always the input `placeholderAdvisories` reasons about. */
  text: string;
  data: ResolvedTextPreview | undefined;
  isFetching: boolean;
  isError: boolean;
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
    <div data-testid="placeholder-preview-panel">
      <p data-testid="placeholder-preview-text">{previewText}</p>
      <p aria-live="polite" className="text-sm text-fg-muted">
        {status}
      </p>
      {isError && <p data-testid="placeholder-preview-error">Could not check this text right now.</p>}
      {rows.length > 0 && (
        <ul>
          {rows.map((row) => (
            <li key={`${row.kind}-${row.reference}`} data-testid={`placeholder-advisory-${row.kind}`}>
              {row.message}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
