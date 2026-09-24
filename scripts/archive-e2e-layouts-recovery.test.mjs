// Guard for #2385.
//
// `archive-e2e-layouts.teardown.ts` falls back, on a refused archive, to
// `if (await looksSignedOut(page)) await signInAsOperator(page);` with no
// deadline re-check and no bound on that call — the same defect #2382 fixed
// for the camera teardown, at the second call site the issue names.
//
// `e2e/support/archive-e2e-layouts.recovery.ts` extracts that fallback as
// `recoverLayout`, committed here in **today's unbounded shape**, unwired.
// These four cases are the property the eventual fix must hold: R1 and R2
// prove a spent deadline still starts work today (it must not); R3 proves an
// in-budget stalled sign-in is not bounded today (it hangs past the test's
// own 2s ceiling, reproducing the original hang at unit-test scale); G1
// proves the `looksSignedOut` guard itself already behaves correctly and
// must keep doing so once the bound is added.
//
// All four run against `recoverLayout`, not the already-correct
// `recoverAndRetry` it will delegate to once fixed — testing the primitive
// alone would be green today and prove nothing about this call site (the
// "guard disconnected from its call site" failure PR #2384 rejected).

import assert from 'node:assert/strict';
import test from 'node:test';
import { recoverLayout } from '../e2e/support/archive-e2e-layouts.recovery.ts';

test('R1: a spent deadline must not start a fresh sign-in on a signed-out page', { timeout: 2_000 }, async () => {
  const deadline = Date.now() - 1; // already spent
  let signInCalled = false;
  let retryCalled = false;

  const outcome = await recoverLayout(
    Date.now(),
    deadline,
    30_000,
    async () => true, // looks signed out
    async () => {
      signInCalled = true;
    },
    async () => {
      retryCalled = true;
      return 'archived';
    },
  );

  assert.equal(outcome.attempted, false, 'a spent budget must not start a fresh attempt');
  assert.equal(signInCalled, false, 'a spent budget must not sign in');
  assert.equal(retryCalled, false, 'a spent budget must not retry the archive either');
});

test('R2: a spent deadline must not start a fresh retry on a signed-in page', { timeout: 2_000 }, async () => {
  const deadline = Date.now() - 1; // already spent
  let retryCalled = false;

  const outcome = await recoverLayout(
    Date.now(),
    deadline,
    30_000,
    async () => false, // already signed in
    async () => {
      throw new Error('sign-in must not be called when the page is already signed in');
    },
    async () => {
      retryCalled = true;
      return 'archived';
    },
  );

  assert.equal(outcome.attempted, false, 'a spent budget must not start a fresh attempt');
  assert.equal(retryCalled, false, 'a spent budget must not retry the archive');
});

test("R3: a stalled sign-in must be bounded to its own timeout, not the caller's deadline", { timeout: 2_000 }, async () => {
  const deadline = Date.now() + 60_000; // plenty of budget left
  let retryCalled = false;

  const start = Date.now();
  const outcome = await recoverLayout(
    Date.now(),
    deadline,
    20, // ms — the recovery's own bound
    async () => true, // looks signed out
    () => new Promise(() => {}), // stands in for a `prompt=none` renewal that never settles
    async () => {
      retryCalled = true;
      return 'archived';
    },
  );
  const elapsed = Date.now() - start;

  assert.ok(elapsed < 500, `expected the stalled sign-in to be bounded to ~20ms, took ${elapsed}ms`);
  assert.equal(outcome.attempted, true, 'a stalled sign-in must not abort the recovery, only bound it');
  assert.equal(retryCalled, true, 'the caller must still get to retry once the bound is hit');
  assert.equal(outcome.result, 'archived', 'the retry result must be passed through');
});

test('G1: an already signed-in page is retried without a sign-in attempt', { timeout: 2_000 }, async () => {
  const deadline = Date.now() + 60_000; // plenty of budget left
  let signInCalls = 0;
  let retryCalls = 0;

  const outcome = await recoverLayout(
    Date.now(),
    deadline,
    30_000,
    async () => false, // already signed in
    async () => {
      signInCalls++;
    },
    async () => {
      retryCalls++;
      return 'archived';
    },
  );

  assert.equal(signInCalls, 0, 'a signed-in page must not trigger a sign-in');
  assert.equal(retryCalls, 1, 'the archive must be retried exactly once');
  assert.equal(outcome.attempted, true);
  assert.equal(outcome.result, 'archived', 'the retry result must be passed through');
});
