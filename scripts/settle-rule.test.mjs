// Guard for #2392 / spec 161 / ADR-0150 §2 (amended).
//
// ADR-0150 bans a fixed-count settle used as a synchronisation primitive: a
// counted loop that yields to the TIMER phase (Selector B — the instrument
// itself), and a fixed-count settle that immediately precedes an assertion
// (Selector A — the reserved-name adjacency rule, ADR-0150 §2 literally).
// Both are wired as one `no-restricted-syntax` rule, registered at `error`,
// over `src/**/*.test.{ts,tsx}` in each app config.
//
// This guard lints the fixtures through the REAL, shipped app configs (`new
// ESLint({ cwd })` against `apps/shared`, unmodified) — not a hand-rolled
// copy of the rule. That is the one property that makes this test worth
// anything: a guard that reads its own idea of the rule proves the idea was
// written down, not that the shipped config enforces it (memory: guards that
// read the design artefact). `ESLint#lintText` is given a `filePath` that
// matches the config's `files` glob so flat-config resolution applies the
// right block, WITHOUT writing the fixture into `apps/shared/src` — nothing
// under version control is touched and there is nothing to clean up.
//
// FIRST-RUN NOTE, read before treating any of this as a status report:
//   * Test 1 (must-flag) is expected RED right now, reporting 0 problems
//     where it asserts 4 — the rule does not exist yet.
//   * Test 3 (config wiring) is expected RED right now, finding
//     `no-restricted-syntax` undefined rather than `error`.
//   * Test 2 (must-not-flag) will be GREEN on this very first run, and that
//     is expected and proves NOTHING on its own: a rule that does not exist
//     flags nothing, so a fixture full of sound idioms and a fixture full of
//     nonsense would both read as "zero problems". Test 2 only becomes
//     evidence once test 1 has been observed red-then-green with the same
//     rule in place. Do not read "1 of 3 red" here as "2 of 3 already
//     satisfied".

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';
import { ESLint } from 'eslint';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const fixturesRoot = path.join(repositoryRoot, 'scripts', 'fixtures', 'settle-rule');

const sharedApp = path.join(repositoryRoot, 'apps', 'shared');
const managementWebApp = path.join(repositoryRoot, 'apps', 'management-web');
const kioskWebApp = path.join(repositoryRoot, 'apps', 'kiosk-web');

// All three apps carry their own COPY of the ADR-0150 config block (no
// shared eslint config exists to lint through once) — see the contention-file
// note in the project guide. Lint every fixture through all three, not just
// `apps/shared`: a fat-fingered selector or dropped combinator in ONE app's
// copy (`NewExpression` -> `NewExpresion`, say) would otherwise go
// undetected as long as `apps/shared`'s copy stayed correct, because the
// per-app fixture population is zero in the real repo either way (#2392
// phase 6 finding 1).
const apps = [
  ['shared', sharedApp],
  ['management-web', managementWebApp],
  ['kiosk-web', kioskWebApp],
];

const severityOf = (entry) => (Array.isArray(entry) ? entry[0] : entry);

function readFixture(name) {
  return readFileSync(path.join(fixturesRoot, name), 'utf8');
}

// Lints `code` through the real ESLint config of `appRoot`, at a filePath
// matching `src/**/*.test.{ts,tsx}` — the rule's `files` scope — so flat
// config resolves the same block `eslint src` would apply to a real suite.
// The path need not exist on disk: flat config's `files` matcher and the
// parser both operate on the string, not the filesystem.
async function lintThroughApp(appRoot, code, fixtureName) {
  const eslint = new ESLint({ cwd: appRoot });
  const [result] = await eslint.lintText(code, {
    filePath: path.join(appRoot, 'src', 'ui', 'composites', `__${fixtureName}__.test.tsx`),
  });
  return result.messages.filter((message) => message.ruleId === 'no-restricted-syntax');
}

for (const [appName, appRoot] of apps) {
  test(`the rule flags a fixed-count settle before an assertion (${appName})`, async () => {
    const code = readFixture('must-flag.fixture.txt');
    const problems = await lintThroughApp(appRoot, code, 'must-flag');

    assert.equal(
      problems.length,
      5,
      `expected exactly 5 no-restricted-syntax problems in must-flag.fixture.txt ` +
        `via ${appName}, got ${problems.length}: ${JSON.stringify(problems, null, 2)}`,
    );

    assert.deepEqual(
      problems.map((problem) => problem.line),
      [28, 36, 46, 53, 65],
      `expected the 5 violations at their recorded lines via ${appName} — the ` +
        'counted timer-phase loop with a literal bound (28), the plain ' +
        'settle-then-assert (36), the comment-separated settle-then-assert (46), ' +
        'the settle-then-negated-assert (53), and the counted timer-phase loop ' +
        'with a NAMED bound (65, the widened-selector pin — #2392 phase 6 ' +
        'finding 2)',
    );

    for (const problem of problems) {
      assert.match(
        problem.message,
        /waitFor|findBy|waitUntil/,
        `expected the message to name a sanctioned idiom via ${appName}, got: ${problem.message}`,
      );
    }
  });

  test(`the rule does not flag the sanctioned or the sound idiom (${appName})`, async () => {
    const code = readFixture('must-not-flag.fixture.txt');
    const problems = await lintThroughApp(appRoot, code, 'must-not-flag');

    // This is the discrimination proof (plan.md §5): the previous test alone
    // is also satisfied by a rule registered as `selector: "*"`. Only a
    // fixture of idioms the rule must NOT touch — the waitUntil deadline
    // poll, the un-looped realWait, the flushMicrotasks microtask drain (both
    // before an assertion and before a driving call), an ordinary awaited
    // helper, a counted loop with no await, a bare un-looped timer await, and
    // an unbounded `for (;;)` poll with no `test` node at all — closes that
    // gap.
    assert.deepEqual(
      problems,
      [],
      `expected zero no-restricted-syntax problems in must-not-flag.fixture.txt ` +
        `via ${appName}, got: ${JSON.stringify(problems, null, 2)}`,
    );
  });
}

test('the rule is registered at error in every app that has tests', async () => {
  const cases = [
    [sharedApp, path.join(sharedApp, 'src', 'ui', 'composites', 'CameraViewer.test.tsx')],
    [
      managementWebApp,
      path.join(managementWebApp, 'src', 'features', 'overlays', 'OverlayEditorDialog.test.tsx'),
    ],
    [kioskWebApp, path.join(kioskWebApp, 'src', 'app', 'wallMode.test.ts')],
  ];

  for (const [appRoot, testFile] of cases) {
    const eslint = new ESLint({ cwd: appRoot });
    const configuration = await eslint.calculateConfigForFile(testFile);
    const entry = configuration.rules?.['no-restricted-syntax'];
    const severity = severityOf(entry);
    const relativeTestFile = path.relative(repositoryRoot, testFile);

    assert.equal(
      severity,
      2,
      `expected no-restricted-syntax at severity error (2) for ` +
        `${relativeTestFile}, got ${JSON.stringify(severity)}`,
    );

    // Severity alone is not enough (#2392 phase 6 finding 4): flat config
    // REPLACES a rule's options rather than merging them, so a later block
    // adding an unrelated no-restricted-syntax entry for test files (banning
    // `describe.only`, say) would silently drop both selectors below while
    // this test still sees severity 2. Assert the options array still
    // carries both.
    const options = Array.isArray(entry) ? entry.slice(1) : [];

    assert.equal(
      options.length,
      2,
      `expected exactly 2 no-restricted-syntax selectors for ` +
        `${relativeTestFile}, got ${options.length}: ${JSON.stringify(options, null, 2)}`,
    );

    const selectors = options.map((option) => option.selector);

    assert.ok(
      selectors.some((selector) => selector.includes("NewExpression[callee.name='Promise']")),
      `expected Selector B (the counted-loop instrument) among the options for ` +
        `${relativeTestFile}, got: ${JSON.stringify(selectors)}`,
    );

    assert.ok(
      selectors.some((selector) => selector.includes('flushConnect|settle|pump|spin')),
      `expected Selector A (the reserved-name adjacency check) among the options for ` +
        `${relativeTestFile}, got: ${JSON.stringify(selectors)}`,
    );
  }
});
