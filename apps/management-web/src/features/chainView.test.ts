import { describe, it, expect } from 'vitest';
import { chainView, type ChainRevision } from './chainView.js';

/**
 * Spec 038 T003–T005. **The eight reachable chain shapes, tested once.**
 *
 * <p>
 * Testing them here rather than twice in the two pages is the reason this helper
 * is extracted at all: the shape table gets one home instead of two that can
 * drift, and if they drifted one page would misidentify the live revision — the
 * exact defect issue 1879 filed.
 * </p>
 *
 * <p>
 * Enumerated by <b>construction</b> rather than by inspection. The spec found
 * five shapes; deriving them from the operations and their preconditions found
 * eight, because <c>Publish</c> archives only the prior <i>Published</i> revision
 * and drafts therefore accumulate. Repeating the inspection method would have
 * shipped three shapes unexamined, which is how the original defect happened.
 * </p>
 */
function revision(revisionNumber: number, state: ChainRevision['state']): ChainRevision {
  return { revisionNumber, state };
}

describe('chainView — the eight reachable chain shapes', () => {
  it('{D} — a new chain: a draft, nothing live', () => {
    const view = chainView([revision(1, 'Draft')]);

    expect(view.live).toBeUndefined();
    expect(view.draft?.revisionNumber).toBe(1);
    expect(view.newest?.revisionNumber).toBe(1);
    expect(view.summarised?.revisionNumber).toBe(1);
    expect(view.fullyArchived).toBe(false);
  });

  it('{P} — published and never branched', () => {
    const view = chainView([revision(1, 'Published')]);

    expect(view.live?.revisionNumber).toBe(1);
    expect(view.draft).toBeUndefined();
    expect(view.fullyArchived).toBe(false);
  });

  it('{A} — stranded, and recoverable since spec 037', () => {
    const view = chainView([revision(1, 'Archived')]);

    expect(view.live).toBeUndefined();
    expect(view.draft).toBeUndefined();
    expect(view.newest?.revisionNumber).toBe(1);
    expect(view.fullyArchived).toBe(true);
  });

  it('{P,D} — live with an open draft', () => {
    const view = chainView([revision(1, 'Published'), revision(2, 'Draft')]);

    expect(view.live?.revisionNumber).toBe(1);
    expect(view.draft?.revisionNumber).toBe(2);
    expect(view.fullyArchived).toBe(false);
  });

  /**
   * The shape issue 1879 filed: the row offered nothing at all while the layout
   * was live on kiosks, because its newest revision is the discarded draft.
   */
  it('{P,A} — live under a discarded draft', () => {
    const view = chainView([revision(1, 'Published'), revision(2, 'Archived')]);

    expect(view.live?.revisionNumber).toBe(1);
    expect(view.draft).toBeUndefined();
    expect(view.newest?.revisionNumber).toBe(2);
    expect(view.fullyArchived).toBe(false);
  });

  it('{A,D} — a draft over archived history, nothing live', () => {
    const view = chainView([revision(1, 'Archived'), revision(2, 'Draft')]);

    expect(view.live).toBeUndefined();
    expect(view.draft?.revisionNumber).toBe(2);
    expect(view.fullyArchived).toBe(false);
  });

  /**
   * T004. **Two open drafts, nothing published** — reachable in two clicks from
   * a published chain, both offered by the row: branch a draft, then revert the
   * published revision.
   *
   * <p>
   * This is the assertion a model that assumes one draft cannot survive, and it
   * passes every single-draft fixture without it. <c>draft</c> is documented as
   * <i>the newest</i> draft for exactly this reason.
   * </p>
   */
  it('{D,D} — two open drafts: the NEWEST is the one the row acts on', () => {
    const view = chainView([revision(1, 'Draft'), revision(2, 'Draft')]);

    expect(view.live).toBeUndefined();
    expect(view.draft?.revisionNumber).toBe(2);
    expect(view.newest?.revisionNumber).toBe(2);
    expect(view.fullyArchived).toBe(false);
  });

  it('{P,D,D} — live with two open drafts', () => {
    const view = chainView([revision(1, 'Draft'), revision(2, 'Draft'), revision(3, 'Published')]);

    expect(view.live?.revisionNumber).toBe(3);
    expect(view.draft?.revisionNumber).toBe(2);
    expect(view.fullyArchived).toBe(false);
  });
});

describe('chainView — what the row describes', () => {
  /**
   * T005. <c>summarised</c> prefers the live revision, then the draft, then the
   * newest — which is a different question from what any button targets.
   */
  it('Describes the live revision even when a newer one was discarded', () => {
    const view = chainView([revision(1, 'Published'), revision(2, 'Archived')]);

    expect(view.summarised?.revisionNumber).toBe(1);
  });

  it('Describes the draft when nothing is live', () => {
    const view = chainView([revision(1, 'Archived'), revision(2, 'Draft')]);

    expect(view.summarised?.revisionNumber).toBe(2);
  });

  it('Describes the newest archived revision when a chain is stranded', () => {
    const view = chainView([revision(1, 'Archived'), revision(2, 'Archived')]);

    expect(view.summarised?.revisionNumber).toBe(2);
    expect(view.fullyArchived).toBe(true);
  });
});

describe('chainView — a chain with no revisions', () => {
  /**
   * Cannot occur: a chain is created with its first revision. Typed for rather
   * than asserted past, so a listing that ever returned one degrades to a row
   * with nothing to offer instead of rendering `vundefined` — and in particular
   * is NOT reported as stranded, since offering to recover it would send a
   * request that cannot succeed.
   */
  it('Has nothing live, nothing to describe, and is not stranded', () => {
    const view = chainView([]);

    expect(view.live).toBeUndefined();
    expect(view.draft).toBeUndefined();
    expect(view.newest).toBeUndefined();
    expect(view.summarised).toBeUndefined();
    expect(view.fullyArchived).toBe(false);
  });
});
