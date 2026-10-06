/**
 * Spec 303 (#2524): tracks the last OIDC `profile.sub` this page load has
 * seen and decides whether a newly observed subject counts as a change.
 *
 * Pure and stateful only in the closure returned here — no React, no store.
 * `undefined` (sign-out, still loading) is ignored and never overwrites the
 * last *defined* subject, so a later defined subject is still compared
 * against the one before the gap, not against `undefined` (FR-004).
 */
export function createSubjectWatcher(onChange: () => void): (subject: string | undefined) => void {
  let lastSubject: string | undefined;
  let hasSeenASubject = false;

  return (subject: string | undefined) => {
    if (subject === undefined) {
      return;
    }

    if (!hasSeenASubject) {
      hasSeenASubject = true;
      lastSubject = subject;
      return;
    }

    if (subject !== lastSubject) {
      lastSubject = subject;
      onChange();
    }
  };
}
