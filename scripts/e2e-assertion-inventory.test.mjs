// Spec 288 US1, plan.md §4.2-4.3, ADR-0162 §6 — the characterisation
// mechanism every wall-session extraction (and, later, US2/US3) proves
// itself against. CI's own JSON reporter cannot supply this: the pinned
// `playwright@1.63.0`'s `JSONReporter._serializeTestResult` keeps only
// `result.steps.filter(s => s.category === "test.step")`, so every `expect`
// step it records is thrown away before the report is even written (spec
// §4, Q3 resolution). `scripts/e2e-assertion-inventory.mjs` exists to
// capture those steps itself, from `onStepEnd`, and to compare one run
// against two baselines so that a test whose own assertion count varies for
// reasons that have nothing to do with the change (the baselines
// disagreeing with each other) is named as noisy instead of misreported as
// a real difference.
//
// `scripts/e2e-assertion-inventory.mjs` does not exist yet. This file pins
// the contract of its two pure, file-I/O-free named exports —
// `collapseRuns(entries)` and `diffInventories(baselineA, baselineB, after)`
// — against fixture objects only, mirroring the idiom
// `node-types-alignment.test.mjs` and `render-leg-baseline-agreement.test.mjs`
// already use for this family of scripts/ checkers. The reporter class
// (default export) and the CLI entry point are exercised separately, against
// the real stack, by tasks.md's T004 smoke check — not by this file.
//
// Assumed contract (the engineer implements the module against this, per
// ADR-0144's Phase 4 split — this file is not edited to make it pass):
//
//   - An "inventory" is a plain object keyed by the reporter's own
//     `project › file › title path` string (retry number folded in before
//     this shape is built; only `retry === 0` results are ever compared —
//     T003's job, not this file's). Each value is an ordered array of
//     `{ title, subtitle }` — `title` is the custom `expect(…, 'message')`
//     text when given, else the default `Expect "<matcher>"`; `subtitle`
//     carries the locator/receiver description. `location` is never part of
//     the shape (plan §4.2: an extracted helper legitimately moves the
//     `expect` call to another file, and that move must not count).
//
//   - `collapseRuns(entries)` takes one test's `{title, subtitle}` array and
//     merges *consecutive* identical entries into one, so a `toPass`-style
//     retry loop's variable repeat count is not a diff.
//
//   - `diffInventories(baselineA, baselineB, after)` compares `after` to
//     `baselineA` per test, after collapsing every test's entries in all
//     three inventories. It returns
//     `{ differences: string[], noisyTests: string[] }`:
//       - `noisyTests` names every test where baselineA and baselineB
//         (collapsed) already disagree with each other — real noise, not
//         attributable to the change.
//       - `differences` holds one human-readable string per real
//         disagreement between `baselineA` and `after`, for every test NOT
//         in `noisyTests` — including a test present in the baselines but
//         absent from `after`. A noisy test contributes to neither array
//         member beyond being named in `noisyTests`: it must never also
//         appear in `differences`, and it must never suppress a genuine
//         difference found on any other test.
//
// Red run 1 (this commit, alone): every case below fails with
// `ERR_MODULE_NOT_FOUND` — `./e2e-assertion-inventory.mjs` does not exist.
// Red run 2 (a stub `diffInventories` that always reports no differences and
// a pass-through `collapseRuns`): run transiently against this file and
// never committed on its own — its output was captured and is quoted
// verbatim in PR 1 instead (ADR-0139): `ℹ tests 8 / pass 1 / fail 7`, every
// case failing for its own reason except case 1. This file is not touched by
// either the stub or the real implementation.

import assert from 'node:assert/strict';
import test from 'node:test';
import { collapseRuns, diffInventories } from './e2e-assertion-inventory.mjs';

// ---- fixture builders ------------------------------------------------------

function entry(title, subtitle, params = '') {
  return { title, subtitle, params };
}

/**
 * A `params` string as the real reporter's `serializeParams` would produce
 * it — `JSON.stringify` with every object's keys sorted, recursively (so a
 * nested `expected: { timeout }` round-trips, unlike an array replacer,
 * which would filter it as an unlisted key). Mirrors `matchers/expect.js`'s
 * `callMatcherAsStep`: `params = { ...suffixes.params }`, then
 * `params.expected = args[0]` when the matcher was called with an argument.
 */
function paramsOf(fields) {
  const sortKeysReplacer = (_key, value) => {
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      return Object.keys(value)
        .sort()
        .reduce((sorted, key) => {
          sorted[key] = value[key];
          return sorted;
        }, {});
    }
    return value;
  };
  return JSON.stringify(fields, sortKeysReplacer);
}

function escapeForRegex(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function describeResult(result) {
  return `differences: ${JSON.stringify(result.differences)}\nnoisyTests: ${JSON.stringify(result.noisyTests)}`;
}

// Real wall-spec test keys (spec §1.5), not invented ones, so a title an
// engineer greps for actually appears somewhere in this file.
const TEST_HOLDS_NO_EXPIRY =
  'wall › wall-outlives-its-session.spec.ts › A wall outlives its session ceiling (spec 052 US2) › holds a grant that carries no expiry';
const TEST_SURVIVES_LOCKOUT =
  'wall › wall-survives-a-lockout.spec.ts › A wall survives a lockout (spec 214 US2, SC-6) › a running wall display keeps its wall while wall-munich is locked out';

// ==== identical before/after — no differences (plan §4.3 row 1) ============

test('baselineA, baselineB and after all record the same ordered expect steps — no differences, nothing noisy', () => {
  const steps = [
    entry('Expect "toBeVisible"', "heading 'Pick a layout'"),
    entry('the wall display should be holding a grant', "locator('#username')"),
  ];
  const baselineA = { [TEST_HOLDS_NO_EXPIRY]: steps };
  const baselineB = { [TEST_HOLDS_NO_EXPIRY]: structuredClone(steps) };
  const after = { [TEST_HOLDS_NO_EXPIRY]: structuredClone(steps) };

  const result = diffInventories(baselineA, baselineB, after);

  assert.deepEqual(result.differences, [], describeResult(result));
  assert.deepEqual(result.noisyTests, [], describeResult(result));
});

// ==== one expect removed from a test (plan §4.3 row 2) ======================

test('after drops one expect step that both baselines agree on — a difference naming the test and the index', () => {
  const before = [
    entry('Expect "toBeVisible"', "heading 'Pick a layout'"),
    entry('the wall display should be holding a grant', "locator('#username')"),
  ];
  const baselineA = { [TEST_HOLDS_NO_EXPIRY]: before };
  const baselineB = { [TEST_HOLDS_NO_EXPIRY]: structuredClone(before) };
  const after = { [TEST_HOLDS_NO_EXPIRY]: [before[0]] };

  const result = diffInventories(baselineA, baselineB, after);

  assert.equal(result.differences.length, 1, describeResult(result));
  assert.match(result.differences[0], new RegExp(escapeForRegex(TEST_HOLDS_NO_EXPIRY)), describeResult(result));
  assert.match(result.differences[0], /\b1\b/, describeResult(result));
  assert.deepEqual(result.noisyTests, [], describeResult(result));
});

// ==== matcher changed (plan §4.3 row 3) =====================================

test('a matcher changes from toBeVisible to toHaveCount at the same position — a difference naming both matchers', () => {
  const baselineA = { [TEST_HOLDS_NO_EXPIRY]: [entry('Expect "toBeVisible"', "heading 'Pick a layout'")] };
  const baselineB = { [TEST_HOLDS_NO_EXPIRY]: [entry('Expect "toBeVisible"', "heading 'Pick a layout'")] };
  const after = { [TEST_HOLDS_NO_EXPIRY]: [entry('Expect "toHaveCount"', "heading 'Pick a layout'")] };

  const result = diffInventories(baselineA, baselineB, after);

  assert.equal(result.differences.length, 1, describeResult(result));
  assert.match(result.differences[0], /toBeVisible/, describeResult(result));
  assert.match(result.differences[0], /toHaveCount/, describeResult(result));
});

// ==== custom message changed (plan §4.3 row 4 — messages are contract,
//      ADR-0162 §4) =========================================================

test("a custom expect(…, message) text changes while the matcher and locator stay the same — a difference (the message is part of the helper's contract)", () => {
  const baselineA = { [TEST_SURVIVES_LOCKOUT]: [entry('the wall display should be holding a grant', 'locator')] };
  const baselineB = { [TEST_SURVIVES_LOCKOUT]: [entry('the wall display should be holding a grant', 'locator')] };
  const after = {
    [TEST_SURVIVES_LOCKOUT]: [entry('the recovered wall display should be holding a grant', 'locator')],
  };

  const result = diffInventories(baselineA, baselineB, after);

  assert.equal(result.differences.length, 1, describeResult(result));
  assert.match(result.differences[0], /the wall display should be holding a grant/, describeResult(result));
  assert.match(result.differences[0], /the recovered wall display should be holding a grant/, describeResult(result));
});

// ==== the same expect repeated a variable number of times, consecutively
//      (plan §4.3 row 5) — collapsed, not a difference ======================

test('the same expect repeats three times in both baselines and five times in after, consecutively — collapsed by collapseRuns, no difference', () => {
  const repeated = entry('Expect "toBeVisible"', 'listitem.first()');

  // collapseRuns itself, directly: a variable-length run of identical
  // consecutive entries collapses to the one entry, whatever the count.
  assert.deepEqual(collapseRuns([repeated, repeated, repeated]), [repeated]);
  assert.deepEqual(collapseRuns([repeated, repeated, repeated, repeated, repeated]), [repeated]);

  const baselineA = { [TEST_HOLDS_NO_EXPIRY]: [repeated, repeated, repeated] };
  const baselineB = { [TEST_HOLDS_NO_EXPIRY]: [repeated, repeated, repeated] };
  const after = { [TEST_HOLDS_NO_EXPIRY]: [repeated, repeated, repeated, repeated, repeated] };

  const result = diffInventories(baselineA, baselineB, after);

  assert.deepEqual(result.differences, [], describeResult(result));
  assert.deepEqual(result.noisyTests, [], describeResult(result));
});

// ==== baselines disagree on test X (plan §4.3 row 6) — X is noisy, and a
//      genuine difference on an unrelated test Y is still caught ===========

test('baselineA and baselineB disagree on test X — X is reported noisy and excluded from differences, while a genuine difference on test Y is still reported', () => {
  // TEST_SURVIVES_LOCKOUT's entry below is shaped as the real reporter would
  // produce it for a non-Locator receiver (e.g. `expect(azp).toBe('kiosk-wall')`):
  // the default title (`Expect "<matcher>"`, no custom message), an empty
  // subtitle (subtitle is only populated for a `Locator` receiver — see
  // `computeMatcherTitleSuffix`), and the expected value carried in `params`
  // instead — exactly what should-fix 2 exists to catch, since neither title
  // nor subtitle changes when only the expected value does.
  const baselineA = {
    [TEST_HOLDS_NO_EXPIRY]: [entry('Expect "toBeVisible"', 'listitem.first()')],
    [TEST_SURVIVES_LOCKOUT]: [entry('Expect "toBe"', '', paramsOf({ expected: 'kiosk-wall' }))],
  };
  const baselineB = {
    // X: the baselines themselves disagree — an extra step baselineA never saw.
    [TEST_HOLDS_NO_EXPIRY]: [
      entry('Expect "toBeVisible"', 'listitem.first()'),
      entry('Expect "toBeVisible"', 'listitem.nth(1)'),
    ],
    [TEST_SURVIVES_LOCKOUT]: [entry('Expect "toBe"', '', paramsOf({ expected: 'kiosk-wall' }))],
  };
  const after = {
    // X: matches baselineA exactly — would be a false positive against
    // baselineB if noise were not masked.
    [TEST_HOLDS_NO_EXPIRY]: [entry('Expect "toBeVisible"', 'listitem.first()')],
    // Y: a real, unrelated difference — title and subtitle are unchanged;
    // only the expected value (`params`) differs.
    [TEST_SURVIVES_LOCKOUT]: [entry('Expect "toBe"', '', paramsOf({ expected: 'kiosk-wall-wide' }))],
  };

  const result = diffInventories(baselineA, baselineB, after);

  assert.deepEqual(result.noisyTests, [TEST_HOLDS_NO_EXPIRY], describeResult(result));
  assert.equal(result.differences.length, 1, describeResult(result));
  assert.match(result.differences[0], new RegExp(escapeForRegex(TEST_SURVIVES_LOCKOUT)), describeResult(result));
  assert.doesNotMatch(
    result.differences.join('\n'),
    new RegExp(escapeForRegex(TEST_HOLDS_NO_EXPIRY)),
    describeResult(result),
  );
});

// ==== a test missing in after (plan §4.3 row 7) =============================

test('a test both baselines agree on is missing from after entirely — a difference, never silently dropped', () => {
  const steps = [entry('Expect "toBeVisible"', "heading 'Pick a layout'")];
  const baselineA = { [TEST_HOLDS_NO_EXPIRY]: steps };
  const baselineB = { [TEST_HOLDS_NO_EXPIRY]: structuredClone(steps) };
  const after = {};

  const result = diffInventories(baselineA, baselineB, after);

  assert.equal(result.differences.length, 1, describeResult(result));
  assert.match(result.differences[0], new RegExp(escapeForRegex(TEST_HOLDS_NO_EXPIRY)), describeResult(result));
  assert.match(result.differences[0], /missing|absent/i, describeResult(result));
});

// ==== subtitle changed, title unchanged (plan §4.3 row 8) ===================

test("a locator's subtitle changes (exact: true dropped) while the title stays the same — a difference naming the dropped text", () => {
  const baselineA = {
    [TEST_SURVIVES_LOCKOUT]: [entry('Expect "toBeVisible"', "getByRole('heading', { name: 'Overlays', exact: true })")],
  };
  const baselineB = {
    [TEST_SURVIVES_LOCKOUT]: [entry('Expect "toBeVisible"', "getByRole('heading', { name: 'Overlays', exact: true })")],
  };
  const after = {
    [TEST_SURVIVES_LOCKOUT]: [entry('Expect "toBeVisible"', "getByRole('heading', { name: 'Overlays' })")],
  };

  const result = diffInventories(baselineA, baselineB, after);

  assert.equal(result.differences.length, 1, describeResult(result));
  assert.match(result.differences[0], /exact: true/, describeResult(result));
});

// ==== volatile-token normalisation (reviewer should-fix 1) ==================
//
// The real shape from `e2e/support/seed-published-layout.setup.ts`:
// `expect(page.getByRole('cell', { name: cameraName })).toBeVisible({ timeout:
// FIRST_WRITE_TIMEOUT_MS })` where `cameraName` is `Kiosk Seed Cam
// ${Date.now()}` — a Locator receiver, so both `subtitle` and `params.locator`
// embed the timestamp, and `params.expected` carries the (non-volatile)
// timeout.

const TEST_SEED_PUBLISHED_LAYOUT =
  'seed › seed-published-layout.setup.ts › a published layout exists for the kiosk to open';

function seedCamEntry(stamp) {
  const subtitle = `getByRole('cell', { name: 'Kiosk Seed Cam ${stamp}' })`;
  return entry('Expect "toBeVisible"', subtitle, paramsOf({ locator: subtitle, expected: { timeout: 45000 } }));
}

test('a seed-cam locator differs only in its Date.now() stamp across baselineA, baselineB and after — normalisation collapses it to a match, and the test is no longer noisy', () => {
  const baselineA = { [TEST_SEED_PUBLISHED_LAYOUT]: [seedCamEntry('1790662172252')] };
  const baselineB = { [TEST_SEED_PUBLISHED_LAYOUT]: [seedCamEntry('1790662299999')] };
  const after = { [TEST_SEED_PUBLISHED_LAYOUT]: [seedCamEntry('1790662355555')] };

  const result = diffInventories(baselineA, baselineB, after);

  assert.deepEqual(result.noisyTests, [], describeResult(result));
  assert.deepEqual(result.differences, [], describeResult(result));
});

test('after normalisation the baselines still disagree in shape (a teardown-style entry-count difference) — a genuinely different after value is still caught, not silently dropped because the test is noisy', () => {
  const baselineA = { [TEST_HOLDS_NO_EXPIRY]: [seedCamEntry('1790662172252')] };
  const baselineB = {
    [TEST_HOLDS_NO_EXPIRY]: [seedCamEntry('1790662299999'), entry('Expect "toBeVisible"', 'listitem.nth(1)')],
  };
  // A genuinely different value — not a timestamp/UUID variant of the
  // baselines' entry, and not merely a different entry count either.
  const after = { [TEST_HOLDS_NO_EXPIRY]: [entry('Expect "toBeHidden"', "heading 'Pick a layout'")] };

  const result = diffInventories(baselineA, baselineB, after);

  assert.deepEqual(result.noisyTests, [TEST_HOLDS_NO_EXPIRY], describeResult(result));
  assert.equal(result.differences.length, 1, describeResult(result));
  assert.match(result.differences[0], new RegExp(escapeForRegex(TEST_HOLDS_NO_EXPIRY)), describeResult(result));
});
