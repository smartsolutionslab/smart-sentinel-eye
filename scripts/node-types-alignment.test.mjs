// Spec 285 §5 / plan.md §4 — the @types/node ↔ CI Node major alignment guard
// (closes #2645). §1's measurement found the repository already aligned
// (CI Node 22 on all three setup-node steps, engines.node floor 22,
// @types/node ^22.20.4) and NOT bumping to 26 — this test file is what
// keeps that alignment from drifting apart unnoticed a second time (the
// rule was already written down twice, in specs 088 and 272, and still
// produced this issue).
//
// `checkNodeTypesAlignment({ workflows, manifest, runningVersion })` is a
// pure function over fixture strings/objects — no file I/O — so every rule
// in plan.md §4.1/§4.2 is pinned here without touching the real repository.
// `readRepositoryInputs(repositoryRoot)` is the one exception (plan.md §4.2):
// it reads the real workflow files and root package.json, and the one test
// that calls it is the case CI actually enforces on every run (plan.md §4.3).
//
// Doubly red (plan.md §5, mirroring render-leg-check.test.mjs's idiom):
//   1. Module absent — every case fails with ERR_MODULE_NOT_FOUND. Proves
//      nothing on its own; quoted in the PR only to show run 1 happened.
//   2. A stub `node-types-alignment.mjs` that always returns `[]` from
//      `checkNodeTypesAlignment` and the real repository's shape from
//      `readRepositoryInputs` (see the engineer brief in the test-writer's
//      report). Every case expecting one or more problems then fails FOR
//      ITS OWN REASON (empty array, not a missing-message crash) while the
//      aligned/`[]` cases pass. That second run is the meaningful red —
//      the module exists, compiles and runs, and still cannot satisfy the
//      four counterfactual-shaped assertions.
//
// The engineer implementing `node-types-alignment.mjs` may not edit this
// file to make it pass (ADR-0144's Phase 4 split). Required wording per
// problem kind (chosen so the assertions below are satisfiable by more than
// one exact phrasing, mirroring render-leg-check.test.mjs's own regexes):
//   - a value this check could not read (bad node-version, bad engines
//     alternative, bad/missing @types/node range) -> the problem string
//     contains "unreadable" (case-insensitive)
//   - zero setup-node steps found in any workflow -> contains "no setup-node
//     step" (case-insensitive)
//   - CI steps disagree with each other -> contains "disagree" and every
//     involved workflow path and every distinct major found
//   - @types/node major vs CI major, or engines floor vs CI major -> names
//     both numbers and "package.json" (the file to change, FR-006)
//   - running runtime major < types major -> names the running version and
//     the types major

import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';
import { checkNodeTypesAlignment, readRepositoryInputs } from './node-types-alignment.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// ---- fixture builders -----------------------------------------------------

// Mirrors the real .github/workflows/ci.yml shape read during spec 285 §1
// (three "Set up Node" steps, `uses: actions/setup-node@…`, `with:
// node-version: '22'` then `cache: pnpm`, followed by an unrelated step) —
// not invented, so the regex the engineer writes has to survive the actual
// file shape, not a simplified stand-in for it.
function setupNodeStepText({ nodeVersion, name = 'Set up Node', nameFirst = true } = {}) {
  const withLines = [];
  if (nodeVersion !== null) {
    withLines.push(`          node-version: ${nodeVersion}`);
  }
  withLines.push('          cache: pnpm');
  const withBlock = `        with:\n${withLines.join('\n')}`;

  const step = nameFirst
    ? `      - name: ${name}\n        uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020 # v4\n${withBlock}`
    : `      - uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020 # v4\n        name: ${name}\n${withBlock}`;

  // A following step that is NOT setup-node, on every fixture — proves the
  // reader stops at the next step rather than reading past it into an
  // unrelated `with:`/`node-version:`-shaped line further down the file.
  return `${step}\n\n      - name: Install\n        run: pnpm install --frozen-lockfile`;
}

function nonSetupNodeStepText() {
  return '      - name: Checkout\n        uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4';
}

function workflowFile(relativePath, stepsText) {
  return {
    path: relativePath,
    text: `name: CI\n\njobs:\n  example:\n    runs-on: ubuntu-latest\n    steps:\n${stepsText}\n`,
  };
}

function alignedWorkflow(relativePath = '.github/workflows/ci.yml') {
  return workflowFile(relativePath, setupNodeStepText({ nodeVersion: "'22'" }));
}

function manifest({ typesRange = '^22.20.4', enginesNode = '^22.22.2 || ^24.15.0 || >=26.0.0', includeTypes = true } = {}) {
  const devDependencies = includeTypes ? { '@types/node': typesRange } : {};
  return { devDependencies, engines: { node: enginesNode } };
}

function describeProblems(problems) {
  return `problems: ${JSON.stringify(problems)}`;
}

// ==== the aligned state passes (spec §2 scenario 1, SC-001) ================

test('every setup-node step at Node 22, @types/node ^22.20.4, engines floor 22, running 22.22.2 — passes with no problems', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

test('the same aligned state, but the running runtime is Node 24 — still passes, the types are a floor not a ceiling (edge case: a newer local runtime)', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest(),
    runningVersion: '24.21.0',
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

test('node-version written as 22, 22.x, \'22\' and 22.22.2 all reduce to major 22 — every numeric spelling passes', () => {
  const stepsText = [
    setupNodeStepText({ nodeVersion: '22', name: 'Set up Node (bare)' }),
    setupNodeStepText({ nodeVersion: '22.x', name: 'Set up Node (dot-x)' }),
    setupNodeStepText({ nodeVersion: "'22'", name: 'Set up Node (quoted)' }),
    setupNodeStepText({ nodeVersion: '22.22.2', name: 'Set up Node (full)' }),
  ].join('\n\n');

  const problems = checkNodeTypesAlignment({
    workflows: [workflowFile('.github/workflows/ci.yml', stepsText)],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

test('a setup-node step with `uses:` and `with:` before `name:` is still read correctly — key order does not defeat the scan', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [workflowFile('.github/workflows/ci.yml', setupNodeStepText({ nodeVersion: "'22'", nameFirst: false }))],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

test("engines.node's alternatives out of order (\">=24.0.0 || ^22.22.2\") — the floor is the lowest major, not the first written (edge case)", () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest({ enginesNode: '>=24.0.0 || ^22.22.2' }),
    runningVersion: '22.22.2',
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

// ==== types bumped past the CI runtime (spec §2 scenario 2, FR-002) ========

test('@types/node declared as ^26.6.3 against CI Node 22 — fails, names 26, 22 and package.json (the conflict #2645 is about)', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest({ typesRange: '^26.6.3' }),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /26/, describeProblems(problems));
  assert.match(problems[0], /22/, describeProblems(problems));
  assert.match(problems[0], /package\.json/i, describeProblems(problems));
});

// ==== CI steps disagree with each other (spec §2 scenario 3, FR-003) =======

test('two setup-node steps declare 22 and one declares 24, all in one workflow — fails, names the disagreement and both majors', () => {
  const stepsText = [
    setupNodeStepText({ nodeVersion: "'22'", name: 'Set up Node (a)' }),
    setupNodeStepText({ nodeVersion: "'22'", name: 'Set up Node (b)' }),
    setupNodeStepText({ nodeVersion: "'24'", name: 'Set up Node (c)' }),
  ].join('\n\n');

  const problems = checkNodeTypesAlignment({
    workflows: [workflowFile('.github/workflows/ci.yml', stepsText)],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /disagree/i, describeProblems(problems));
  assert.match(problems[0], /22/, describeProblems(problems));
  assert.match(problems[0], /24/, describeProblems(problems));
  assert.match(problems[0], /ci\.yml/, describeProblems(problems));
});

test('two workflow files disagree with each other — fails, names both file paths and both majors', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [
      workflowFile('.github/workflows/ci.yml', setupNodeStepText({ nodeVersion: "'22'" })),
      workflowFile('.github/workflows/nightly.yml', setupNodeStepText({ nodeVersion: "'24'" })),
    ],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /disagree/i, describeProblems(problems));
  assert.match(problems[0], /\.github\/workflows\/ci\.yml/, describeProblems(problems));
  assert.match(problems[0], /\.github\/workflows\/nightly\.yml/, describeProblems(problems));
  assert.match(problems[0], /22/, describeProblems(problems));
  assert.match(problems[0], /24/, describeProblems(problems));
});

// ==== engines.node's floor diverges from CI (spec §2 scenario 1 counterpart,
//      FR-004) ==============================================================

test('engines.node floor is 24 while CI declares 22 — fails, names the floor and the CI major', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest({ enginesNode: '^24.15.0' }),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /24/, describeProblems(problems));
  assert.match(problems[0], /22/, describeProblems(problems));
  assert.match(problems[0], /package\.json/i, describeProblems(problems));
});

// ==== the running runtime is older than the typings (spec §2 scenario 4,
//      FR-005) ==============================================================

test('running Node major (20) is below the declared @types/node major (22) — fails, names the running version', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest(),
    runningVersion: '20.19.0',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /20\.19\.0/, describeProblems(problems));
  assert.match(problems[0], /22/, describeProblems(problems));
});

// ==== unreadable declarations (spec §2 scenario 5, FR-006) — a value the
//      check cannot read is a failure, never a vacuous pass ================

test("node-version: lts/* cannot be compared — fails, unreadable, not silently skipped (an alias would also let CI's runtime move silently)", () => {
  const problems = checkNodeTypesAlignment({
    workflows: [workflowFile('.github/workflows/ci.yml', setupNodeStepText({ nodeVersion: 'lts/*' }))],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /unreadable/i, describeProblems(problems));
  assert.match(problems[0], /lts\/\*/, describeProblems(problems));
  assert.match(problems[0], /ci\.yml/, describeProblems(problems));
});

test('a setup-node step with no node-version key at all — fails, unreadable, not a vacuous pass', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [workflowFile('.github/workflows/ci.yml', setupNodeStepText({ nodeVersion: null }))],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /unreadable/i, describeProblems(problems));
  assert.match(problems[0], /ci\.yml/, describeProblems(problems));
});

test('package.json declares no @types/node at all — fails, unreadable, names package.json', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest({ includeTypes: false }),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /unreadable/i, describeProblems(problems));
  assert.match(problems[0], /package\.json/i, describeProblems(problems));
});

test("@types/node declared as '>=22' — fails, unreadable (a >= range could resolve to any future major)", () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest({ typesRange: '>=22' }),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /unreadable/i, describeProblems(problems));
  assert.match(problems[0], /package\.json/i, describeProblems(problems));
});

test("engines.node is '<23' — fails, unreadable (a < alternative names no floor)", () => {
  const problems = checkNodeTypesAlignment({
    workflows: [alignedWorkflow()],
    manifest: manifest({ enginesNode: '<23' }),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /unreadable/i, describeProblems(problems));
  assert.match(problems[0], /package\.json/i, describeProblems(problems));
});

// ==== zero setup-node steps found (spec Edge Cases, FR-006) — a refactor
//      that renames the action or moves the steps must fail, not pass with
//      nothing compared =====================================================

test('a workflow file with no setup-node step at all — fails, names the absence, never passes with nothing compared', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [workflowFile('.github/workflows/ci.yml', nonSetupNodeStepText())],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /no setup-node step/i, describeProblems(problems));
});

test('the workflows array itself is empty — fails, names the absence, never a vacuous pass', () => {
  const problems = checkNodeTypesAlignment({
    workflows: [],
    manifest: manifest(),
    runningVersion: '22.22.2',
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /no setup-node step/i, describeProblems(problems));
});

// ==== the real repository (plan.md §4.3's closing paragraph) — the one case
//      that asks the running system, and the only I/O in this suite =========

test('readRepositoryInputs(repositoryRoot) against the real .github/workflows and package.json, on the runtime actually running this test — passes with no problems', () => {
  const problems = checkNodeTypesAlignment({
    ...readRepositoryInputs(repositoryRoot),
    runningVersion: process.versions.node,
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});
