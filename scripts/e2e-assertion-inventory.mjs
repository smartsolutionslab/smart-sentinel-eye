#!/usr/bin/env node
// scripts/e2e-assertion-inventory.mjs
//
// Spec 288 US1, plan.md §4.2/§4.4, ADR-0162 §6 — the characterisation
// mechanism every wall-session extraction (and, later, US2/US3) proves itself
// against. Playwright's own JSON reporter cannot supply this: the pinned
// `playwright@1.63.0`'s `JSONReporter._serializeTestResult` keeps only
// `result.steps.filter(s => s.category === "test.step")`, dropping every
// `expect` step before the report is even written. This module captures
// those steps itself, from `onStepEnd`, and compares one run against two
// baselines so a test whose own step shape varies for reasons that have
// nothing to do with the change (the baselines disagreeing with each other)
// is named as noisy instead of misreported as a real difference.
//
// Two roles, one file:
//
//   - **Reporter** (default export): a Playwright reporter recording, per
//     test, the ordered `expect`-category steps of the FIRST attempt only
//     (`result.retry === 0` — a retry loop's step count is not behaviour).
//     Writes the inventory to `E2E_ASSERTION_INVENTORY_FILE`
//     (default `test-results/assertion-inventory.json`) in `onEnd`.
//
//   - **Diff** (named exports `collapseRuns`, `diffInventories`, plus a CLI
//     entry point when this file is run directly): pure, file-I/O-free
//     comparison of inventory objects, pinned by
//     `scripts/e2e-assertion-inventory.test.mjs` (this module is implemented
//     to match that file — ADR-0144 Phase 4 split — not the other way round).
//
// CLI usage:
//   node scripts/e2e-assertion-inventory.mjs baseline-a.json baseline-b.json after.json
// Exits 0 when `differences` is empty, 1 otherwise. `noisyTests` is always
// printed, whichever way it exits — a noisy test is not silently green.

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname } from 'node:path';
import { pathToFileURL } from 'node:url';

const DEFAULT_OUTPUT_PATH = 'test-results/assertion-inventory.json';

// ---------------------------------------------------------------------------
// Reporter
// ---------------------------------------------------------------------------

/**
 * The reporter's own key for a test: `project › file basename › describe… ›
 * test title`. `location` is deliberately never part of this key or of an
 * entry's shape (plan §4.2) — an extracted helper legitimately moves its
 * `expect` call to another file, and that move is the refactor, not a
 * change the inventory should ever see.
 */
function testKey(test) {
  const projectName = test.parent?.project?.()?.name ?? '';
  const fileBase = basename(test.location.file);
  // titlePath() = ['', <project>, <file>, ...<describe titles>, <test title>].
  // The first three entries are the root/project/file suites; everything
  // after is describe nesting followed by the test's own title.
  const rest = test.titlePath().slice(3);
  return [projectName, fileBase, ...rest].join(' › ');
}

export default class AssertionInventoryReporter {
  constructor() {
    /** @type {Record<string, Array<{title: string, subtitle: string}>>} */
    this.inventory = {};
  }

  onTestEnd(test) {
    // Registers the test even when it recorded no `expect` step at all, so a
    // test that legitimately asserts nothing is present in the inventory
    // (and a genuinely missing test can still be told apart from one that
    // never asserted).
    const key = testKey(test);
    this.inventory[key] ??= [];
  }

  onStepEnd(test, result, step) {
    if (step.category !== 'expect') return;
    if (result.retry !== 0) return;

    const key = testKey(test);
    (this.inventory[key] ??= []).push({ title: step.title, subtitle: step.subtitle ?? '' });
  }

  onEnd() {
    const outputPath = process.env['E2E_ASSERTION_INVENTORY_FILE'] ?? DEFAULT_OUTPUT_PATH;
    mkdirSync(dirname(outputPath), { recursive: true });
    writeFileSync(outputPath, JSON.stringify(this.inventory, null, 2));
  }
}

// ---------------------------------------------------------------------------
// Diff (pure, pinned by scripts/e2e-assertion-inventory.test.mjs)
// ---------------------------------------------------------------------------

/**
 * Merges *consecutive* identical `{title, subtitle}` entries into one. A
 * `toPass`-style retry loop re-runs the same `expect` a variable number of
 * times, and that count is not behaviour.
 */
export function collapseRuns(entries) {
  const collapsed = [];
  for (const entry of entries) {
    const last = collapsed[collapsed.length - 1];
    if (last !== undefined && last.title === entry.title && last.subtitle === entry.subtitle) {
      continue;
    }
    collapsed.push(entry);
  }
  return collapsed;
}

function collapseInventory(inventory) {
  const collapsed = {};
  for (const [key, entries] of Object.entries(inventory)) {
    collapsed[key] = collapseRuns(entries);
  }
  return collapsed;
}

function entriesEqual(a, b) {
  if (a.length !== b.length) return false;
  return a.every((entry, index) => entry.title === b[index].title && entry.subtitle === b[index].subtitle);
}

function describeEntry(entry) {
  return entry === undefined ? '(missing)' : `${entry.title} (${entry.subtitle})`;
}

function unionKeys(...inventories) {
  const keys = new Set();
  for (const inventory of inventories) {
    for (const key of Object.keys(inventory)) keys.add(key);
  }
  return keys;
}

/**
 * Compares `after` to `baselineA` per test, after collapsing every test's
 * entries in all three inventories. A test where the two baselines already
 * disagree (real noise, not attributable to the change) is named in
 * `noisyTests` and excluded from `differences` — it must never suppress a
 * genuine difference found on any other test.
 */
export function diffInventories(baselineA, baselineB, after) {
  const collapsedA = collapseInventory(baselineA);
  const collapsedB = collapseInventory(baselineB);
  const collapsedAfter = collapseInventory(after);

  const noisyTests = [];
  const baselineKeys = unionKeys(baselineA, baselineB);
  for (const key of baselineKeys) {
    const a = collapsedA[key] ?? [];
    const b = collapsedB[key] ?? [];
    if (!entriesEqual(a, b)) {
      noisyTests.push(key);
    }
  }
  const noisySet = new Set(noisyTests);

  const differences = [];
  for (const key of baselineKeys) {
    if (noisySet.has(key)) continue;

    const before = collapsedA[key] ?? [];
    if (!(key in collapsedAfter)) {
      differences.push(`${key}: test is missing from the after run`);
      continue;
    }

    const afterEntries = collapsedAfter[key];
    if (entriesEqual(before, afterEntries)) continue;

    const length = Math.max(before.length, afterEntries.length);
    for (let index = 0; index < length; index++) {
      const beforeEntry = before[index];
      const afterEntry = afterEntries[index];
      if (beforeEntry?.title === afterEntry?.title && beforeEntry?.subtitle === afterEntry?.subtitle) continue;
      differences.push(`${key} › index ${index}: ${describeEntry(beforeEntry)} → ${describeEntry(afterEntry)}`);
    }
  }

  return { differences, noisyTests };
}

// ---------------------------------------------------------------------------
// CLI entry
// ---------------------------------------------------------------------------

function readInventory(path) {
  return JSON.parse(readFileSync(path, 'utf8'));
}

function main() {
  const [, , baselineAPath, baselineBPath, afterPath] = process.argv;
  if (!baselineAPath || !baselineBPath || !afterPath) {
    process.stderr.write(
      'e2e-assertion-inventory: usage: node scripts/e2e-assertion-inventory.mjs <baseline-a.json> <baseline-b.json> <after.json>\n',
    );
    process.exit(1);
  }

  const baselineA = readInventory(baselineAPath);
  const baselineB = readInventory(baselineBPath);
  const after = readInventory(afterPath);

  const { differences, noisyTests } = diffInventories(baselineA, baselineB, after);

  if (noisyTests.length > 0) {
    process.stdout.write('noisyTests (excluded from the comparison below — the baselines disagree on these):\n');
    for (const test of noisyTests) process.stdout.write(`  ${test}\n`);
  } else {
    process.stdout.write('noisyTests: none\n');
  }

  if (differences.length > 0) {
    process.stdout.write('differences:\n');
    for (const difference of differences) process.stdout.write(`  ${difference}\n`);
    process.exit(1);
  }

  process.stdout.write('e2e-assertion-inventory: no differences\n');
  process.exit(0);
}

// `pathToFileURL` (not a raw `file://${...}` template) so this comparison is
// correct on Windows, where `import.meta.url` and a raw path disagree on
// separators and drive-letter casing (mirrors render-leg-summary.mjs).
const isMainModule = process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;

if (isMainModule) {
  main();
}
