import { describe, expect, it } from 'vitest';
import {
  CONFLICT_FALLBACK,
  isConflict,
  isStaleConflict,
  isTerminalRefusal,
  problemCode,
  problemDetail,
} from './problemDetail.js';

/**
 * Spec 030 T008, rewritten by spec 031 T013.
 *
 * Spec 030 taught this helper a second spelling of "your version is stale",
 * because CameraCatalog answered 412 with a code that did not end `_STALE` and
 * nothing recognised it. Spec 031 removed the second spelling instead: the code
 * was renamed, ADR-0119 made the suffix authoritative, and an architecture test
 * now enforces it. So these tests changed shape — they no longer assert a
 * status/code pair, they assert the code and nothing else.
 *
 * The cases that matter are the ones where the wrong answer still produces a
 * message: an operator told to "try again" on a stale version resubmits, and
 * replays their change over the other writer's — the lost update the whole
 * `If-Match` mechanism exists to prevent.
 */
const refusal = (status: number, title: string, detail?: string) => ({
  status,
  data: { title, ...(detail === undefined ? {} : { detail }) },
});

describe('isStaleConflict', () => {
  /**
   * All eight lost-update codes in the product, kept in step with
   * `StaleCodeConventionTests.Every_lost_update_refusal_in_the_product_is_accounted_for`.
   * If that test's list grows, this one grows with it.
   */
  it('recognises every lost-update code the product can answer with', () => {
    for (const code of [
      'AGGREGATE_VERSION_STALE',
      'CAMERA_VERSION_STALE',
      'LAYOUT_REVISION_STALE',
      'OVERLAY_REVISION_STALE',
      'RULE_STALE',
      'VARIABLE_STALE',
      'WEBHOOK_CLIENT_STALE',
      'WEBHOOK_INTEGRATION_STALE',
    ]) {
      expect(isStaleConflict(refusal(409, code)), code).toBe(true);
    }
  });

  /**
   * The point of ADR-0119, stated as an assertion rather than left implicit in
   * the list above. The camera answers 412 and the other six answer 409, and
   * this helper must not care: were it still consulting the status, one of these
   * two lines would fail.
   */
  it('does not consult the status — the same code is stale at either one', () => {
    expect(isStaleConflict(refusal(412, 'CAMERA_VERSION_STALE'))).toBe(true);
    expect(isStaleConflict(refusal(409, 'CAMERA_VERSION_STALE'))).toBe(true);

    expect(isStaleConflict(refusal(409, 'LAYOUT_REVISION_STALE'))).toBe(true);
    expect(isStaleConflict(refusal(412, 'LAYOUT_REVISION_STALE'))).toBe(true);
  });

  /**
   * `AGGREGATE_VERSION_STALE` is the shared Layer-2 handler in ServiceDefaults —
   * the true database race, registered once and reaching every mutating endpoint
   * in every context. It was `AGGREGATE_VERSION_CONFLICT` until spec 031, which
   * meant this helper said false for it and the operator was told to try again
   * *product-wide*, not merely on cameras.
   */
  it('recognises the shared database race that no context declares itself', () => {
    expect(isStaleConflict(refusal(409, 'AGGREGATE_VERSION_STALE'))).toBe(true);
  });

  // 409 is not exclusively a stale version, which is why the status was never
  // sufficient: offering "reload to see their version" for a name collision
  // sends the operator somewhere useless.
  it('is not fooled by other 409s', () => {
    expect(isStaleConflict(refusal(409, 'LAYOUT_NAME_TAKEN'))).toBe(false);
    expect(isStaleConflict(refusal(409, 'CAMERA_NAME_TAKEN'))).toBe(false);
  });

  /**
   * And 412 is overloaded in the same way, in the other direction: Identity
   * answers it for an upsert precondition that was wrong about existence, which
   * is not a lost update. Keying on 412 would have swept these in and told the
   * operator to reload something that does not exist.
   */
  it('is not fooled by 412s that are not about a version', () => {
    expect(isStaleConflict(refusal(412, 'WEBHOOK_CLIENT_ALREADY_EXISTS'))).toBe(false);
    expect(isStaleConflict(refusal(412, 'WEBHOOK_CLIENT_NOT_FOUND'))).toBe(false);
  });

  it('is false for a retired camera, which is a 409 but not a lost update', () => {
    expect(isStaleConflict(refusal(409, 'CAMERA_RETIRED'))).toBe(false);
  });

  it('is false when there is no error and when the error carries no code', () => {
    expect(isStaleConflict(null)).toBe(false);
    expect(isStaleConflict(undefined)).toBe(false);
    expect(isStaleConflict({ status: 409 })).toBe(false);
  });
});

describe('isTerminalRefusal', () => {
  it('recognises a retired camera', () => {
    expect(isTerminalRefusal(refusal(409, 'CAMERA_RETIRED'))).toBe(true);
  });

  /**
   * The pair that would otherwise share wording. `CAMERA_RETIRED` is a 409, so
   * `isConflict` is true for it and it would inherit CONFLICT_FALLBACK —
   * "someone else changed this, reload to see their version" — about a camera
   * nobody changed and that reloading will not help.
   *
   * Still asserted after spec 031 simplified `isStaleConflict` to the suffix
   * alone: the simplification must not have quietly widened the predicate into
   * the terminal refusal's territory.
   */
  it('is distinguishable from a lost update even though both are 409', () => {
    const retired = refusal(409, 'CAMERA_RETIRED');
    const stale = refusal(409, 'LAYOUT_REVISION_STALE');

    expect(isConflict(retired)).toBe(true);
    expect(isConflict(stale)).toBe(true);

    expect(isTerminalRefusal(retired)).toBe(true);
    expect(isTerminalRefusal(stale)).toBe(false);

    expect(isStaleConflict(retired)).toBe(false);
    expect(isStaleConflict(stale)).toBe(true);
  });

  it('is false for a stale version and for a plain not-found', () => {
    expect(isTerminalRefusal(refusal(412, 'CAMERA_VERSION_STALE'))).toBe(false);
    expect(isTerminalRefusal(refusal(404, 'CAMERA_NOT_FOUND'))).toBe(false);
  });
});

describe('problemDetail and problemCode', () => {
  it('returns the server detail when there is one', () => {
    expect(problemDetail(refusal(404, 'CAMERA_NOT_FOUND', 'No such camera.'), 'fallback')).toBe('No such camera.');
  });

  it('falls back when the refusal carries no detail', () => {
    expect(problemDetail(refusal(500, 'SERVER_ERROR'), 'fallback')).toBe('fallback');
  });

  it('returns null only when there is no error at all', () => {
    expect(problemDetail(null, 'fallback')).toBeNull();
    expect(problemDetail(undefined, 'fallback')).toBeNull();
  });

  it('reads the code from the title', () => {
    expect(problemCode(refusal(409, 'CAMERA_RETIRED'))).toBe('CAMERA_RETIRED');
    expect(problemCode(null)).toBeNull();
  });

  it('keeps the conflict fallback pointed at re-reading rather than retrying', () => {
    expect(CONFLICT_FALLBACK).toMatch(/reload/i);
    expect(CONFLICT_FALLBACK).not.toMatch(/try again/i);
  });
});
