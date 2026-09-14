import type {
  PlaceholderResolutionEntry,
  ResolvedTextPreview,
} from '@smart-sentinel-eye/shared/api/systemVariables.api';

/**
 * Every advisory kind the panel can show for one referenced name (spec 148
 * US1 + US4). `'malformed'` is the client's own heuristic — brace-shaped text
 * the server never saw a name for — and is disjoint from the server-reported
 * kinds: a name the server calls `Unknown` is well-formed, just undefined.
 */
export type PlaceholderAdvisoryKind = 'resolved' | 'unknown' | 'unset' | 'archived' | 'malformed';

export interface PlaceholderAdvisory {
  kind: PlaceholderAdvisoryKind;
  /** The variable name for every server-reported kind; the raw brace-shaped
   * text (e.g. `'{{ temperature }}'`) for `'malformed'`, which has no name. */
  reference: string;
  message: string;
}

/**
 * The loose, non-authoritative shape US4 flags as "brace-shaped but maybe
 * not a placeholder" — deliberately wider than the server's real grammar
 * (`PlaceholderParser`'s `[A-Za-z][A-Za-z0-9_]{0,63}`). The strict grammar
 * stays server-side only; this is a heuristic, not a second resolver
 * (spec 148 US4).
 */
const BRACE_SHAPED = /\{\{([^{}]*)\}\}/g;

/**
 * Wording scoped to what the *editor* can know (spec 148 decision 3,
 * ADR-0115): an overlay is a fab-neutral template, so a name absent from the
 * author's fabs may legitimately be present in the viewer's. Never "does not
 * exist" — only "no variable named X is defined in your fab(s)".
 */
function advisoryFor(entry: PlaceholderResolutionEntry): PlaceholderAdvisory {
  const { name, outcome, fab, renderedValue } = entry;
  switch (outcome) {
    case 'Resolved':
      return { kind: 'resolved', reference: name, message: `${name} — ${renderedValue} (${fab})` };
    case 'Unset':
      return {
        kind: 'unset',
        reference: name,
        message: `${name} is defined in ${fab} but has no value set yet. It will render as {{${name}}}.`,
      };
    case 'Archived':
      return {
        kind: 'archived',
        reference: name,
        message: `${name} was archived in your fab(s). It will render as {{${name}}}.`,
      };
    case 'Unknown':
    default:
      return {
        kind: 'unknown',
        reference: name,
        message: `${name} — no variable named ${name} is defined in your fab(s). It will render as {{${name}}}.`,
      };
  }
}

/**
 * Rows for brace-shaped text the server never saw a name for (spec 148 US4).
 * Matched against the **loose** shape above, then filtered to exclude any
 * match whose inner text is one of the names the server actually returned —
 * those already have a server-reported row (`Unknown` included) and must not
 * also be flagged malformed.
 */
function malformedRows(rawText: string, knownNames: ReadonlySet<string>): PlaceholderAdvisory[] {
  const rows: PlaceholderAdvisory[] = [];
  for (const match of rawText.matchAll(BRACE_SHAPED)) {
    const full = match[0];
    const inner = match[1] ?? '';
    if (knownNames.has(inner)) continue;
    rows.push({
      kind: 'malformed',
      reference: full,
      message: `${full} is not a placeholder and will render literally.`,
    });
  }
  return rows;
}

/**
 * Maps a resolve response plus the raw authored text onto the rows
 * `PlaceholderPreviewPanel` renders. Pure: no DOM, no store, no fetch — unit
 * testable in isolation (spec 148 plan.md "Frontend wiring").
 */
export function placeholderAdvisories(
  rawText: string,
  response: ResolvedTextPreview | undefined,
): PlaceholderAdvisory[] {
  if (response === undefined) return [];

  const rows = response.placeholders.map(advisoryFor);
  const knownNames = new Set(response.placeholders.map((entry) => entry.name));
  return [...rows, ...malformedRows(rawText, knownNames)];
}
