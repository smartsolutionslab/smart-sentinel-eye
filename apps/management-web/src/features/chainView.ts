/**
 * What a revisioned chain currently is, read off the whole chain rather than off
 * its newest revision (spec 038, issue 1879).
 *
 * <p>
 * Both the layouts and the overlays row used to decide everything from the
 * highest-numbered revision. A chain is not its newest revision, and the gap
 * between those two produced a live layout whose row offered nothing at all, and
 * an <b>Archive</b> button that archived a draft while telling the operator the
 * layout was going out of service.
 * </p>
 *
 * <p>
 * Shared by both rows on purpose. What they share is the <i>rule</i> — which
 * revision is live, which draft is current — not a shape, which is why this is
 * the spec-036 case rather than the spec-035 one. If two copies drifted, one page
 * would misidentify the live revision, which is the defect this exists to remove.
 * ADR-0104's twin rule governs the backend contexts and does not reach here.
 * </p>
 */

/** The two fields this reads. Callers pass their own richer revision type. */
export interface ChainRevision {
  revisionNumber: number;
  state: 'Draft' | 'Published' | 'Archived';
}

export interface ChainView<TRevision extends ChainRevision> {
  /** The Published revision — the one kiosks are showing. At most one exists. */
  live: TRevision | undefined;
  /**
   * The **newest** Draft, not *the* draft. A chain can hold several: branch a
   * draft off a published revision and then revert that revision, and there are
   * two open drafts and nothing published — two clicks, both offered by the row.
   * A model that assumes one draft passes every single-draft test and is wrong
   * in practice.
   */
  draft: TRevision | undefined;
  /**
   * Highest-numbered revision, and the branch source when the chain is stranded.
   *
   * <p>
   * Undefined only for a chain with no revisions, which cannot occur — a chain is
   * created with its first. Typed for it anyway rather than asserted past, so a
   * listing that ever returned one degrades to a row with nothing to offer
   * instead of rendering `vundefined`.
   * </p>
   */
  newest: TRevision | undefined;
  /**
   * The revision the row **describes** — badge, tile summary, label preview.
   *
   * <p>
   * Deliberately separate from every action target. What a row says <i>about</i>
   * a chain and what its buttons do <i>to</i> it are different questions, and
   * collapsing them is a smaller version of the defect being fixed.
   * </p>
   */
  summarised: TRevision | undefined;
  /**
   * No live revision and no draft — which, since every revision is one of the
   * three, is the same set as "every revision archived". Spec 037's stranded
   * chain (ADR-0121), expressed as what it actually is rather than as a special
   * case beside the model.
   *
   * <p>
   * False for a chain with no revisions at all: there is nothing to branch from,
   * so offering to recover it would send a request that cannot succeed.
   * </p>
   */
  fullyArchived: boolean;
}

export function chainView<TRevision extends ChainRevision>(revisions: readonly TRevision[]): ChainView<TRevision> {
  const live = revisions.find((revision) => revision.state === 'Published');
  const draft = newestOf(revisions.filter((revision) => revision.state === 'Draft'));
  const newest = newestOf(revisions);

  return {
    live,
    draft,
    newest,
    summarised: live ?? draft ?? newest,
    fullyArchived: newest !== undefined && live === undefined && draft === undefined,
  };
}

function newestOf<TRevision extends ChainRevision>(revisions: readonly TRevision[]): TRevision | undefined {
  return revisions.reduce<TRevision | undefined>(
    (highest, revision) =>
      highest === undefined || revision.revisionNumber > highest.revisionNumber ? revision : highest,
    undefined,
  );
}
