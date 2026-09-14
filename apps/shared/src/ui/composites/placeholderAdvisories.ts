import type { ResolvedTextPreview } from '@smart-sentinel-eye/shared/api/systemVariables.api';

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
 * Maps a resolve response plus the raw authored text onto the rows
 * `PlaceholderPreviewPanel` renders. Pure: no DOM, no store, no fetch — unit
 * testable in isolation (spec 148 plan.md "Frontend wiring").
 *
 * <p><b>Phase 4a scaffold (spec 148).</b> Returns no rows at all. This is the
 * one piece of genuinely new decision logic on the frontend side — the
 * per-outcome wording (US1 decision 3: "no variable named X is defined in
 * your fab(s)", never "does not exist") and the loose-brace heuristic
 * (US4) — and it is deliberately left unimplemented so
 * `placeholderAdvisories.test.ts` is observed red for a real reason (an
 * empty array where rows are expected), not a missing export.</p>
 */
export function placeholderAdvisories(
  rawText: string,
  response: ResolvedTextPreview | undefined,
): PlaceholderAdvisory[] {
  void rawText;
  void response;
  void BRACE_SHAPED;
  return [];
}
