import { describe, expect, it, vi } from 'vitest';
import { createSubjectWatcher } from './subjectWatcher.js';

/**
 * Spec 303 (#2524) T001 — RED.
 *
 * `createSubjectWatcher` is the pure decision at the centre of the fix:
 * "does this OIDC `profile.sub` count as a change from the last one this page
 * load has seen?" It remembers the last *defined* subject and tells its
 * caller only when a newly observed, defined subject differs from it.
 *
 * `./subjectWatcher.ts` does not exist yet (plan.md §1, §4 test 1) — this
 * whole file is expected to fail to resolve its import until T002 creates it.
 * That is the acceptable compile-error red for a pure helper (ADR-0139).
 */
describe('createSubjectWatcher (spec 303, #2524)', () => {
  it('Records the first defined subject silently — no callback on the first sighting', () => {
    const onChange = vi.fn();
    const observe = createSubjectWatcher(onChange);

    observe('operator-a');

    expect(onChange).not.toHaveBeenCalled();
  });

  it('Does not fire when the same subject is observed again', () => {
    const onChange = vi.fn();
    const observe = createSubjectWatcher(onChange);

    observe('operator-a');
    observe('operator-a');

    expect(onChange).not.toHaveBeenCalled();
  });

  it('Fires exactly once when a different defined subject is observed', () => {
    const onChange = vi.fn();
    const observe = createSubjectWatcher(onChange);

    observe('operator-a');
    observe('operator-b');

    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('Ignores an undefined subject: no callback, and it does not erase the last known subject', () => {
    const onChange = vi.fn();
    const observe = createSubjectWatcher(onChange);

    observe('operator-a');
    observe(undefined);

    expect(onChange).not.toHaveBeenCalled();

    // The real assertion is here, not above: if `undefined` had cleared the
    // watcher's memory, the next defined subject would be treated as a FIRST
    // sighting (silent) rather than a change from the original A — so this
    // would stay at 0 calls instead of becoming 1. Comparing against the
    // ORIGINAL last-known subject, not against `undefined`, is the behaviour
    // this test exists to pin (FR-004).
    observe('operator-b');

    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('A -> B -> B fires once: the second sighting of B is a no-op', () => {
    const onChange = vi.fn();
    const observe = createSubjectWatcher(onChange);

    observe('operator-a');
    observe('operator-b');
    observe('operator-b');

    expect(onChange).toHaveBeenCalledTimes(1);
  });
});
