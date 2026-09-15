// Guard for #2382.
//
// `e2e/support/retire-e2e-cameras.teardown.ts` gave itself an 8-minute budget
// for its sweep, then fell back — on any camera that looked missing — to an
// unbounded `await signInAsOperator(page)` with no deadline check of its own.
// That call raced the app's own `prompt=none` silent renewal, which can stall
// for minutes against a self-signed certificate in this environment, and the
// stall fell through to the test's own 900s ceiling: 15.5 minutes on roughly
// a third of green CI runs, reported as flaky rather than failed.
//
// The fix lives in `e2e/support/retire-e2e-cameras.recovery.ts` as
// `recoverAndRetry`, extracted specifically so its timing behaviour can be
// exercised without a browser. These two tests are the property the fix
// claims: a spent budget does not start a fresh recovery, and a recovery in
// flight cannot run longer than its own bound regardless of how long the
// sign-in it wraps takes to (not) settle.
//
// Both tests carry a `timeout` well under a second. That is deliberate: a
// naive reproduction of the original defect (no deadline check, `await
// signIn()` with no race) hangs test two rather than merely failing an
// assertion, and the test's own timeout is what turns that hang into a fast,
// legible red rather than a stalled test run — the same shape of bug this
// guard exists to catch, at a scale that fits a unit test.

import assert from 'node:assert/strict';
import test from 'node:test';
import { recoverAndRetry } from '../e2e/support/retire-e2e-cameras.recovery.ts';

test('does not start a recovery once the sweep is out of time', { timeout: 2_000 }, async () => {
  const deadline = Date.now() - 1; // already spent
  let signInCalled = false;
  let retryCalled = false;

  const outcome = await recoverAndRetry(
    Date.now(),
    deadline,
    30_000,
    async () => {
      signInCalled = true;
    },
    async () => {
      retryCalled = true;
      return true;
    },
  );

  assert.equal(outcome.attempted, false, 'a spent budget must not start a fresh attempt');
  assert.equal(signInCalled, false, 'a spent budget must not sign in again');
  assert.equal(retryCalled, false, 'a spent budget must not retry the action either');
});

test("bounds a stalled sign-in to its own timeout, not the caller's deadline", { timeout: 2_000 }, async () => {
  const deadline = Date.now() + 60_000; // plenty of budget left
  let retryCalled = false;

  const start = Date.now();
  const outcome = await recoverAndRetry(
    Date.now(),
    deadline,
    20, // ms — the recovery's own bound
    () => new Promise(() => {}), // stands in for a `prompt=none` renewal that never settles
    async () => {
      retryCalled = true;
      return 'retried';
    },
  );
  const elapsed = Date.now() - start;

  assert.ok(elapsed < 500, `expected the stalled sign-in to be bounded to ~20ms, took ${elapsed}ms`);
  assert.equal(outcome.attempted, true, 'a stalled sign-in must not abort the recovery, only bound it');
  assert.equal(retryCalled, true, 'the caller must still get to retry once the bound is hit');
  assert.equal(outcome.result, 'retried');
});
