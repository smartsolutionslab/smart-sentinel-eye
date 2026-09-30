/**
 * Spec 038 FR-009. Names the LIVE revision, because that is the one on
 * kiosks, and says when a draft is open without hiding either. The row used
 * to report its newest revision, so a live wall under a discarded draft read
 * as "Archived" while it was playing on the floor.
 *
 * Plain text, not a status pill (issue #2692, ADR-0146 item 4) — colouring
 * "Published" would make it status vocabulary it isn't; the semantic triad
 * is reserved for the operator-facing go/fault/caution colours.
 *
 * Shared between `LayoutsPage` and `OverlaysPage`, whose revisions differ
 * only in which other fields ride along; `RevisionSummary` is the
 * structural shape both already satisfy.
 *
 * `Published` appears exactly when a live revision exists, which is what
 * keeps the two tests that read this text matching.
 */
export interface RevisionSummary {
  readonly revisionNumber: number;
  readonly state: 'Draft' | 'Published' | 'Archived';
}

export function revisionSummary(
  live: RevisionSummary | undefined,
  draft: RevisionSummary | undefined,
  summarised: RevisionSummary | undefined,
): string {
  if (live !== undefined) {
    return draft === undefined
      ? `v${live.revisionNumber} · Published`
      : `v${live.revisionNumber} · Published · draft v${draft.revisionNumber}`;
  }
  return summarised === undefined ? '' : `v${summarised.revisionNumber} · ${summarised.state}`;
}
