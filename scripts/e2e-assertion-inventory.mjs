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
//     Each entry also carries `step.params` (Playwright 1.63's
//     `TestStep.params`, e.g. the matcher's expected value or a locator
//     description) serialised to a stable string — `title`/`subtitle` alone
//     miss a changed expected value or matcher option (`{ timeout }`) when
//     the receiver isn't a `Locator`. Writes the inventory to
//     `E2E_ASSERTION_INVENTORY_FILE` (default
//     `test-results/assertion-inventory.json`) in `onEnd`.
//
//   - **Diff** (named exports `collapseRuns`, `diffInventories`, plus a CLI
//     entry point when this file is run directly): pure, file-I/O-free
//     comparison of inventory objects, pinned by
//     `scripts/e2e-assertion-inventory.test.mjs` (this module is implemented
//     to match that file — ADR-0144 Phase 4 split — not the other way round).
//     Before comparing, every `title`/`subtitle`/`params` is normalised:
//     runs of 10+ digits (a `Date.now()` stamp) become `<n>` and UUID-shaped
//     substrings become `<uuid>`, so a seed's timestamp does not make its
//     test look noisy. A test that still disagrees between the two
//     baselines after normalising is genuinely noisy (e.g. a cleanup
//     teardown whose entry count varies) — it is never silently dropped:
//     `after` is checked against both baselines (positions where the
//     baselines themselves disagree are wildcards when they're the same
//     length; a length mismatch falls back to a whole-array match against
//     either baseline), and a difference is reported only when it matches
//     neither.
//
// `collapseRuns` has two known limits, left as-is (documented, not fixed):
// a `toPass` body asserting 2+ different `expect`s produces an A,B,A,B,…
// pattern that will NOT collapse — it fails safe (a false positive noisy/
// diff report, never a false negative); and deleting one of two identical
// *consecutive* entries is invisible to it (three of the same collapse the
// same as two), a real blind spot rather than a safety margin.
//
// CLI usage:
//   node scripts/e2e-assertion-inventory.mjs baseline-a.json baseline-b.json after.json
// Exits 0 when `differences` is empty, 1 otherwise. `noisyTests` is always
// printed, whichever way it exits — a noisy test is not silently green, and
// staying in that list does not mean it was excluded from the comparison.

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

/**
 * A stable, deterministic serialisation of a step's `params` (Playwright
 * 1.63's `TestStep.params`) — the matcher's expected value / options and, for
 * a `Locator` receiver, the locator description. Object keys are sorted so
 * two calls with the same params always serialise identically regardless of
 * property insertion order; a `RegExp` or function value (neither of which
 * `JSON.stringify` renders usefully) is coerced to a readable string instead
 * of being silently dropped or replaced with `{}`/`null`.
 */
function serializeParams(params) {
  if (params === undefined) return '';
  const sortKeysReplacer = (_key, value) => {
    if (value instanceof RegExp) return value.toString();
    if (typeof value === 'function') return '<function>';
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
  try {
    return JSON.stringify(params, sortKeysReplacer) ?? '';
  } catch {
    return String(params);
  }
}

export default class AssertionInventoryReporter {
  constructor() {
    /** @type {Record<string, Array<{title: string, subtitle: string, params: string}>>} */
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
    (this.inventory[key] ??= []).push({
      title: step.title,
      subtitle: step.subtitle ?? '',
      params: serializeParams(step.params),
    });
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
 * Merges *consecutive* identical `{title, subtitle, params}` entries into
 * one. A `toPass`-style retry loop re-runs the same `expect` a variable
 * number of times, and that count is not behaviour.
 */
export function collapseRuns(entries) {
  const collapsed = [];
  for (const entry of entries) {
    const last = collapsed[collapsed.length - 1];
    if (last !== undefined && entryFieldsEqual(last, entry)) {
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

// ---- volatile-token normalisation -----------------------------------------
//
// A seed's `Kiosk Seed Cam ${Date.now()}` (or a generated UUID) makes an
// otherwise-identical test look different between two baseline runs, purely
// because the stamp itself changed — that is noise, not a behaviour
// difference, and this normalisation is what tells the two apart.

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;
const LONG_DIGIT_RUN_PATTERN = /\d{10,}/g;

function normalizeVolatileTokens(text) {
  return text.replace(UUID_PATTERN, '<uuid>').replace(LONG_DIGIT_RUN_PATTERN, '<n>');
}

function normalizeEntry(entry) {
  return {
    title: normalizeVolatileTokens(entry.title),
    subtitle: normalizeVolatileTokens(entry.subtitle),
    params: normalizeVolatileTokens(entry.params ?? ''),
  };
}

function entryFieldsEqual(a, b) {
  return a.title === b.title && a.subtitle === b.subtitle && (a.params ?? '') === (b.params ?? '');
}

function entriesEqual(a, b) {
  if (a.length !== b.length) return false;
  return a.every((entry, index) => entryFieldsEqual(entry, b[index]));
}

function describeEntry(entry) {
  if (entry === undefined) return '(missing)';
  const paramsSuffix = entry.params ? `, params ${entry.params}` : '';
  return `${entry.title} (${entry.subtitle}${paramsSuffix})`;
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
 * entries in all three inventories and normalising volatile tokens
 * (`Date.now()` stamps, UUIDs) out of `title`/`subtitle`/`params`.
 *
 * A test where the two baselines still disagree once normalised is named in
 * `noisyTests` — real noise, not attributable to the change, but never
 * silently dropped: `after` is still checked against both baselines, and a
 * difference is reported only when it matches **neither**. When the two
 * baselines are the same length, the positions where they disagree become
 * wildcards (matched by anything in `after`) and every other position must
 * still agree; when their lengths differ, `after` must match one baseline's
 * full, exact shape.
 */
export function diffInventories(baselineA, baselineB, after) {
  const collapsedA = collapseInventory(baselineA);
  const collapsedB = collapseInventory(baselineB);
  const collapsedAfter = collapseInventory(after);

  const noisyTests = [];
  const differences = [];
  const baselineKeys = unionKeys(baselineA, baselineB);

  for (const key of baselineKeys) {
    const before = collapsedA[key] ?? [];
    const b = collapsedB[key] ?? [];
    const normBefore = before.map(normalizeEntry);
    const normB = b.map(normalizeEntry);
    const shapesAgree = entriesEqual(normBefore, normB);

    if (!shapesAgree) noisyTests.push(key);

    if (!(key in collapsedAfter)) {
      differences.push(`${key}: test is missing from the after run`);
      continue;
    }

    const afterEntries = collapsedAfter[key];
    const normAfter = afterEntries.map(normalizeEntry);

    if (shapesAgree) {
      // The baselines agree once volatile tokens are normalised — compare
      // `after` the same way, but describe any mismatch with the RAW
      // (un-normalised) entries so a reported difference still shows real
      // values rather than `<n>`/`<uuid>` placeholders.
      if (entriesEqual(normBefore, normAfter)) continue;

      const length = Math.max(before.length, afterEntries.length);
      for (let index = 0; index < length; index++) {
        const beforeNorm = normBefore[index];
        const afterNorm = normAfter[index];
        if (beforeNorm !== undefined && afterNorm !== undefined && entryFieldsEqual(beforeNorm, afterNorm)) continue;
        differences.push(
          `${key} › index ${index}: ${describeEntry(before[index])} → ${describeEntry(afterEntries[index])}`,
        );
      }
      continue;
    }

    // Still noisy once normalised: a genuinely different shape between the
    // baselines (e.g. a cleanup teardown whose entry count varies), not just
    // token noise. `after` is checked against both baselines and flagged
    // only when it matches neither.
    if (normBefore.length === normB.length) {
      if (normAfter.length !== normBefore.length) {
        differences.push(
          `${key}: noisy test (baselines disagree even after normalising volatile tokens) — after has ` +
            `${afterEntries.length} step(s), baselineA has ${before.length} and baselineB has ${b.length}, matching neither`,
        );
        continue;
      }
      for (let index = 0; index < normBefore.length; index++) {
        const baselinesAgreeHere = entryFieldsEqual(normBefore[index], normB[index]);
        if (!baselinesAgreeHere) continue; // wildcard position — matched by anything
        if (entryFieldsEqual(normBefore[index], normAfter[index])) continue;
        differences.push(
          `${key} › index ${index}: noisy test, but baselineA and baselineB agree here — ` +
            `${describeEntry(before[index])} → ${describeEntry(afterEntries[index])}`,
        );
      }
    } else {
      const matchesA = entriesEqual(normBefore, normAfter);
      const matchesB = entriesEqual(normB, normAfter);
      if (!matchesA && !matchesB) {
        differences.push(
          `${key}: noisy test (baselines disagree in shape, ${before.length} vs ${b.length} steps) — ` +
            `after (${afterEntries.length} steps) matches neither baseline`,
        );
      }
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
    process.stdout.write(
      'noisyTests (baselines disagree even after normalising volatile tokens — still checked against both, only an unmatched after run below is a difference):\n',
    );
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
