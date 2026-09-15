// Guard for #2397 / spec 162.
//
// `apps/shared/vitest.config.ts` narrows `include` to `src/**/*.test.ts(x)`,
// so `apps/shared/src/realtime/client.spec.ts` had never been collected by
// any runner: not CI, not a human running `pnpm test`, because nothing ever
// asked the question. This guard asks it — the same question the gate asks,
// through the same tool the gate uses.
//
// Two kinds of assertion here, and they are not equally strong:
//   * "every test-shaped file under an app is collected" runs `vitest list
//     --filesOnly --json` inside the app, so it covers a file added
//     tomorrow under a directory no current glob happens to reach — memory:
//     guards that read the design artefact. The candidate scan walks the
//     whole app root (excluding node_modules/dist/.vite), not just `src/`:
//     `apps/shared` currently narrows its own vitest `include` to
//     `src/**/*.test.ts(x)`, so a test file placed outside `src` there would
//     be uncollected *and* unflagged if the scan stopped at `src` too
//     (#2397 review finding 5). No app currently has a test-shaped file
//     outside `src/` — verified by a full-tree search — so this widens the
//     guard's claim without changing what it reports today.
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
//
// Root discovery deliberately does not assume a runner ("has a `test`
// script" only), but asking the runner *the collection question* does — it
// shells out to `vitest` specifically. Those two used to disagree silently:
// a future `apps/<x>` with `"test": "node --test"` or `"test": "jest"` would
// pass discovery and then blow up `require.resolve('vitest/package.json')`
// with a bare `MODULE_NOT_FOUND`, taking the whole guard down instead of
// just that app (#2397 review finding 1). Fixed by making them agree
// explicitly: an app is only asked the vitest collection question if its own
// `package.json` declares `vitest` as a dependency; one that doesn't is
// skipped, and the skip is reported (`t.diagnostic`) rather than silent, so
// a `node --test`/jest app shows up as a visible skip instead of either an
// opaque crash or a quiet no-op.

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

// Whether `appRoot` actually uses vitest as its test runner, read from its
// own `package.json` (dependency or devDependency) rather than assumed from
// having a `test` script at all — see the header note on why `vitestBinFor`
// must not be reached for an app that doesn't declare vitest.
async function usesVitestRunner(appRoot) {
  const packageJson = JSON.parse(await readFile(path.join(appRoot, 'package.json'), 'utf8'));
  return Boolean(packageJson.dependencies?.vitest ?? packageJson.devDependencies?.vitest);
}

// Resolve the app's own installed `vitest` bin and run it directly with
// `process.execPath`, rather than shelling out to `npx`. Two things this
// avoids (#2397 review finding 8): the `npx`/`npx.cmd` platform branch, which
// was dead anyway once `shell: true` was in play — the shell resolves `npx`
// through PATHEXT on Windows regardless of which spelling is passed — and
// the `shell: true` option itself, which drew a DEP0190 deprecation warning
// on every run. No shell, no platform branch, no warning.
function vitestBinFor(appRoot) {
  const relativeAppRoot = normalise(path.relative(repositoryRoot, appRoot));
  const require = createRequire(path.join(appRoot, 'package.json'));
  const vitestPackageJsonPath = require.resolve('vitest/package.json');
  const vitestPackage = JSON.parse(readFileSync(vitestPackageJsonPath, 'utf8'));
  const vitestBinEntry = typeof vitestPackage.bin === 'string' ? vitestPackage.bin : vitestPackage.bin?.vitest;
  assert.ok(
    typeof vitestBinEntry === 'string',
    `${relativeAppRoot}'s installed vitest package has no 'bin.vitest' entry (bin: ${JSON.stringify(vitestPackage.bin)})`,
  );
  return path.join(path.dirname(vitestPackageJsonPath), vitestBinEntry);
}

// The "ask the tool" half: vitest's own resolution of what it will collect,
// run through the app's real, shipped config — not a re-implementation of
// its include globs.
function collectedFilesFor(appRoot) {
  const relativeAppRoot = normalise(path.relative(repositoryRoot, appRoot));
  const result = spawnSync(process.execPath, [vitestBinFor(appRoot), 'list', '--filesOnly', '--json'], {
    cwd: appRoot,
    encoding: 'utf8',
  });

  assert.equal(result.status, 0, `vitest list failed in ${relativeAppRoot}: ${result.stderr}`);

  let entries;
  try {
    entries = JSON.parse(result.stdout);
  } catch (error) {
    const firstLine = result.stdout.split('\n')[0];
    // A config that logs during `vitest list` (both app configs evaluate
    // `process.env` at load) makes stdout something other than pure JSON;
    // name the app and show what was actually seen, rather than a
    // `SyntaxError` naming no one (#2397 review finding 3).
    throw new Error(
      `vitest list produced non-JSON stdout in ${relativeAppRoot} (first line: ${JSON.stringify(firstLine)}): ${error.message}`,
    );
  }
  return new Set(entries.map((entry) => normalise(path.resolve(entry.file))));
}

test('every test-shaped file under an app is collected by that app vitest', async (t) => {
  const appRoots = await discoverAppRoots(appsDirectory);
  assert.ok(appRoots.length > 0, 'expected at least one app under apps/ with a test script');

  const uncollected = [];
  for (const appRoot of appRoots) {
    const appName = path.basename(appRoot);

    // Discovery keys on "has a `test` script", not "uses vitest" — a future
    // app with `"test": "node --test"` or jest must be skipped here, visibly,
    // rather than reaching `vitestBinFor` and throwing a bare
    // `MODULE_NOT_FOUND` that names no app and takes the whole guard down
    // (#2397 review finding 1).
    if (!(await usesVitestRunner(appRoot))) {
      t.diagnostic(`skipping apps/${appName}: no vitest dependency declared, cannot ask 'vitest list'`);
      continue;
    }

    const collected = collectedFilesFor(appRoot);
    // A collected set of zero would make the inner loop vacuously pass —
    // every candidate would report "uncollected", true, but a `discovered
    // app that collects nothing` is itself a sign the guard checked nothing
    // real for it. Accumulate this into the same `uncollected` list instead
    // of asserting per app: an `assert.ok` here would abort the loop on the
    // first empty app and leave every later app unchecked — worst in
    // exactly the scenario this guard exists to catch (#2397 review finding
    // 4). Accumulating means one run names every affected app, not just the
    // first.
    if (collected.size === 0) {
      uncollected.push(`apps/${appName}'s vitest collected zero files (expected at least one)`);
      continue;
    }

    // Walk the whole app root, not `<appRoot>/src` — see the header note on
    // finding 5. Directories that are never source (node_modules/dist/.vite)
    // are excluded by name inside testShapedFilesUnder.
    const candidates = await testShapedFilesUnder(appRoot);

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
