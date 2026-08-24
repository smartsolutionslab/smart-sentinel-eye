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
 * Spec 030 T008. The first tests this helper has had, written because spec 029
 * introduced a second spelling of "your version is stale" and the helper only
 * understood the first.
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
  it('recognises the six aggregates that answer 409 with a _STALE code', () => {
    for (const code of [
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

  // Spec 029's spelling: 412 is what RFC 9110 specifies for a failed If-Match,
  // so the camera is the more correct one and the six above are the deviation.
  it('recognises the camera, which answers 412', () => {
    expect(isStaleConflict(refusal(412, 'CAMERA_VERSION_MISMATCH'))).toBe(true);
  });

  // The reason this helper was always two-part: 409 is not exclusively a stale
  // version, and offering "reload to see their version" for a name collision
  // sends the operator somewhere useless.
  it('is not fooled by other 409s', () => {
    expect(isStaleConflict(refusal(409, 'LAYOUT_NAME_TAKEN'))).toBe(false);
    expect(isStaleConflict(refusal(409, 'CAMERA_NAME_TAKEN'))).toBe(false);
  });

  /**
   * The guard for the new half. 412 is *also* already overloaded: Identity
   * answers it for an upsert precondition that was wrong about existence, which
   * is not a lost update. Widening on status alone would have swept these in
   * and told the operator to reload something that does not exist.
   */
  it('is not fooled by 412s that are not about a version', () => {
    expect(isStaleConflict(refusal(412, 'WEBHOOK_CLIENT_ALREADY_EXISTS'))).toBe(false);
    expect(isStaleConflict(refusal(412, 'WEBHOOK_CLIENT_NOT_FOUND'))).toBe(false);
  });

  it('is false for a retired camera, which is a 409 but not a lost update', () => {
    expect(isStaleConflict(refusal(409, 'CAMERA_RETIRED'))).toBe(false);
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
    expect(isTerminalRefusal(refusal(412, 'CAMERA_VERSION_MISMATCH'))).toBe(false);
    expect(isTerminalRefusal(refusal(404, 'CAMERA_NOT_FOUND'))).toBe(false);
  });
});

describe('problemDetail and problemCode', () => {
  it('returns the server detail when there is one', () => {
    expect(problemDetail(refusal(404, 'CAMERA_NOT_FOUND', 'No such camera.'), 'fallback')).toBe(
      'No such camera.',
    );
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
