// Guard for #2397 / spec 162.
//
// `apps/shared/vitest.config.ts` narrows `include` to `src/**/*.test.ts(x)`,
// so `apps/shared/src/realtime/client.spec.ts` had never been collected by
// any runner: not CI, not a human running `pnpm test`, because nothing ever
// asked the question. This guard asks it — the same question the gate asks,
// through the same tool the gate uses.
//
// Two kinds of assertion here, and they are not equally strong:
//   * "every test-shaped file under an app's src/ is collected" runs
//     `vitest list --filesOnly --json` inside the app, so it covers a file
//     added tomorrow under a directory no current glob happens to reach —
//     memory: guards that read the design artefact.
//   * Root discovery ("which directories under apps/ are apps") reads
//     `apps/*/package.json` for a `test` script, rather than naming
//     `['shared', 'kiosk-web', 'management-web']` in this file. A guard that
//     hardcodes three paths is correct today and silently incomplete the
//     day a fourth app lands — the exact failure mode under repair. Proven
//     not to be a hardcoded list by a fixture tree whose directory names
//     share nothing with the real apps.
//
// FIRST-RUN PROVENANCE, 2026-09-16 (past tense — this is history, not a
// status report): "every test-shaped file ... is collected" was observed RED
// on the first run of this guard, naming
// apps/shared/src/realtime/client.spec.ts. "app roots are discovered, not
// hardcoded" was GREEN on that same first run, which proved nothing on its
// own — the defect was one additive uncollected file, not a universal
// failure to flag, so a guard that flagged every file it enumerates would
// have looked identical on that axis. It became evidence only once the first
// assertion had been observed red-then-green with this same guard in place,
// and once the scratch-file counterfactual (spec 162, T004) had run.
//
// A prior revision of this file also asserted "apps with no known gap report
// zero uncollected files" against a hardcoded `['kiosk-web',
// 'management-web']`. Deleted (#2397 review finding 4): it could not fail
// without the first assertion also failing — the first assertion already
// iterates every discovered app, including both of those — and it hardcoded
// exactly the list the *other* assertion (app roots are discovered, not
// hardcoded) exists to argue against.
//
// Divergence from `lint-scope.test.mjs`'s precedent, chosen rather than
// inherited: that guard asks ESLint through its Node API; this one shells
// out to the app's own `vitest` bin, five times across a `pnpm test` run.
// Defensible — it is more faithfully "the tool the gate uses", since vitest
// (unlike ESLint) has no supported programmatic "what would you collect"
// API — but it is a choice, not a default.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const appsDirectory = path.join(repositoryRoot, 'apps');

const TEST_FILE_PATTERN = /\.(test|spec)\.(ts|tsx|mts|cts|js|jsx|mjs|cjs)$/;
const SKIP_DIRECTORY_NAMES = new Set(['node_modules', 'dist', '.vite']);

// vitest returns forward slashes even on Windows; readdir/path.join give the
// platform separator. Normalise both sides before comparing — memory:
// source-scanning tests need slash normalising.
const normalise = (filePath) => filePath.split(path.sep).join('/');

// The one assertion in this file that is about discovery rather than
// content: which directories under `directory` are "an app" (a `test`
// script in their package.json), read from the filesystem each run.
// A missing directory (ENOENT) is tolerated — it means "no app here", which
// is what the `gamma-not-a-package` fixture (no package.json at all) relies
// on. Any other failure — in particular a `package.json` that exists but
// fails to parse — propagates and fails the guard loudly. Swallowing it
// would silently drop that app out of `appRoots`, and the guard would report
// green having checked nothing for it: the exact failure mode this guard
// exists to catch, turned on itself (#2397 review finding 2).
async function discoverAppRoots(directory) {
  let entries;
  try {
    entries = await readdir(directory, { withFileTypes: true });
  } catch (error) {
    if (error.code === 'ENOENT') return [];
    throw error;
  }

  const roots = [];
  for (const entry of entries) {
    if (!entry.isDirectory()) continue;

    const packageJsonPath = path.join(directory, entry.name, 'package.json');
    let packageJson;
    try {
      packageJson = JSON.parse(await readFile(packageJsonPath, 'utf8'));
    } catch (error) {
      if (error.code === 'ENOENT') continue;
      throw error;
    }

    if (typeof packageJson.scripts?.test === 'string') {
      roots.push(path.join(directory, entry.name));
    }
  }
  return roots.sort();
}

// Same tolerance as discoverAppRoots, and for the same reason: a missing
// directory is a legitimate "nothing here" (an app with no `src/`), but any
// other readdir failure must propagate rather than silently produce an
// empty candidate set (#2397 review finding 2).
async function testShapedFilesUnder(directory) {
  let entries;
  try {
    entries = await readdir(directory, { withFileTypes: true });
  } catch (error) {
    if (error.code === 'ENOENT') return [];
    throw error;
  }

  const files = [];
  for (const entry of entries) {
    if (SKIP_DIRECTORY_NAMES.has(entry.name)) continue;
    const fullPath = path.join(directory, entry.name);

    if (entry.isDirectory()) {
      files.push(...(await testShapedFilesUnder(fullPath)));
    } else if (entry.isFile() && TEST_FILE_PATTERN.test(entry.name)) {
      files.push(fullPath);
    }
  }
  return files;
}

// Resolve the app's own installed `vitest` bin and run it directly with
// `process.execPath`, rather than shelling out to `npx`. Two things this
// avoids (#2397 review finding 8): the `npx`/`npx.cmd` platform branch, which
// was dead anyway once `shell: true` was in play — the shell resolves `npx`
// through PATHEXT on Windows regardless of which spelling is passed — and
// the `shell: true` option itself, which drew a DEP0190 deprecation warning
// on every run. No shell, no platform branch, no warning.
function vitestBinFor(appRoot) {
  const require = createRequire(path.join(appRoot, 'package.json'));
  const vitestPackageJsonPath = require.resolve('vitest/package.json');
  const vitestPackage = JSON.parse(readFileSync(vitestPackageJsonPath, 'utf8'));
  const vitestBinEntry = typeof vitestPackage.bin === 'string' ? vitestPackage.bin : vitestPackage.bin.vitest;
  return path.join(path.dirname(vitestPackageJsonPath), vitestBinEntry);
}

// The "ask the tool" half: vitest's own resolution of what it will collect,
// run through the app's real, shipped config — not a re-implementation of
// its include globs.
function collectedFilesFor(appRoot) {
  const result = spawnSync(process.execPath, [vitestBinFor(appRoot), 'list', '--filesOnly', '--json'], {
    cwd: appRoot,
    encoding: 'utf8',
  });

  assert.equal(
    result.status,
    0,
    `vitest list failed in ${path.relative(repositoryRoot, appRoot)}: ${result.stderr}`,
  );

  const entries = JSON.parse(result.stdout);
  return new Set(entries.map((entry) => normalise(path.resolve(entry.file))));
}

test('every test-shaped file under an app src/ is collected by that app vitest', async () => {
  const appRoots = await discoverAppRoots(appsDirectory);
  assert.ok(appRoots.length > 0, 'expected at least one app under apps/ with a test script');

  const uncollected = [];
  for (const appRoot of appRoots) {
    const appName = path.basename(appRoot);
    const collected = collectedFilesFor(appRoot);
    // A collected set of zero would make the inner loop vacuously pass —
    // every candidate would report "uncollected", true, but a `discovered
    // app that collects nothing` is itself a sign the guard checked nothing
    // real for it (#2397 review finding 2). Assert it per app, not only via
    // the top-level `appRoots.length > 0`, which fires only if *every* app
    // vanished.
    assert.ok(collected.size > 0, `expected apps/${appName}'s vitest to collect at least one file`);
    const candidates = await testShapedFilesUnder(path.join(appRoot, 'src'));

    for (const candidate of candidates) {
      if (!collected.has(normalise(path.resolve(candidate)))) {
        const relative = path.relative(repositoryRoot, candidate);
        uncollected.push(`${normalise(relative)} is not collected by apps/${appName}'s vitest`);
      }
    }
  }

  assert.deepEqual(uncollected, [], `test files not collected by any vitest run:\n${uncollected.join('\n')}`);
});

test('app roots are discovered from apps/*/package.json, not a hardcoded list', async () => {
  const fixtureRoot = await mkdtemp(path.join(tmpdir(), 'test-collection-fixture-'));
  try {
    const appWithTestScript = path.join(fixtureRoot, 'alpha-app');
    const appWithoutTestScript = path.join(fixtureRoot, 'beta-app');
    const directoryWithoutPackageJson = path.join(fixtureRoot, 'gamma-not-a-package');

    await mkdir(appWithTestScript, { recursive: true });
    await writeFile(
      path.join(appWithTestScript, 'package.json'),
      JSON.stringify({ name: 'alpha-app', scripts: { test: 'vitest run' } }),
    );

    await mkdir(appWithoutTestScript, { recursive: true });
    await writeFile(
      path.join(appWithoutTestScript, 'package.json'),
      JSON.stringify({ name: 'beta-app', scripts: { build: 'tsc' } }),
    );

    await mkdir(directoryWithoutPackageJson, { recursive: true });

    const roots = await discoverAppRoots(fixtureRoot);

    // None of these fixture names overlap with the real apps
    // ('shared'/'kiosk-web'/'management-web'), so a discovery function that
    // secretly returned a hardcoded list would fail this assertion by
    // returning an empty array instead.
    assert.deepEqual(
      roots.map((root) => path.basename(root)),
      ['alpha-app'],
      'expected only the fixture directory whose package.json declares a `test` script',
    );
  } finally {
    await rm(fixtureRoot, { recursive: true, force: true });
  }
});
