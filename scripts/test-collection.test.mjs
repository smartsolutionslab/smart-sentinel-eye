// Guard for #2397 / spec 162.
//
// `apps/shared/vitest.config.ts` narrows `include` to `src/**/*.test.ts(x)`,
// so `apps/shared/src/realtime/client.spec.ts` has never been collected by
// any runner: not CI, not a human running `pnpm test`, because nothing ever
// asked the question. This guard asks it — the same question the gate asks,
// through the same tool the gate uses.
//
// Two kinds of assertion here, and they are not equally strong:
//   * "every test-shaped file under an app's src/ is collected" and "the
//     apps with no known gap report nothing" both run
//     `npx vitest list --filesOnly --json` inside the app, so they cover a
//     file added tomorrow under a directory no current glob happens to
//     reach — memory: guards that read the design artefact.
//   * Root discovery ("which directories under apps/ are apps") reads
//     `apps/*/package.json` for a `test` script, rather than naming
//     `['shared', 'kiosk-web', 'management-web']` in this file. A guard that
//     hardcodes three paths is correct today and silently incomplete the
//     day a fourth app lands — the exact failure mode under repair. Proven
//     not to be a hardcoded list by a fixture tree whose directory names
//     share nothing with the real apps.
//
// FIRST-RUN NOTE, read before treating any of this as a status report:
//   * "every test-shaped file ... is collected" is expected RED right now,
//     naming apps/shared/src/realtime/client.spec.ts.
//   * "apps with no known gap report nothing" and "app roots are
//     discovered, not hardcoded" will be GREEN on this very first run, and
//     that is expected and proves NOTHING on its own: the defect is one
//     additive uncollected file, not a universal failure to flag, so a
//     guard that flagged every file it enumerates would look identical on
//     these two axes. They only become evidence once the first assertion
//     has been observed red-then-green with this same guard in place, and
//     once the scratch-file counterfactual (spec 162, T004) has run. Do not
//     read "1 of 3 red" here as "the other two were already satisfied".

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
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
async function discoverAppRoots(directory) {
  let entries;
  try {
    entries = await readdir(directory, { withFileTypes: true });
  } catch {
    return [];
  }

  const roots = [];
  for (const entry of entries) {
    if (!entry.isDirectory()) continue;

    const packageJsonPath = path.join(directory, entry.name, 'package.json');
    let packageJson;
    try {
      packageJson = JSON.parse(await readFile(packageJsonPath, 'utf8'));
    } catch {
      continue;
    }

    if (typeof packageJson.scripts?.test === 'string') {
      roots.push(path.join(directory, entry.name));
    }
  }
  return roots.sort();
}

async function testShapedFilesUnder(directory) {
  let entries;
  try {
    entries = await readdir(directory, { withFileTypes: true });
  } catch {
    return [];
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

// The "ask the tool" half: vitest's own resolution of what it will collect,
// run through the app's real, shipped config — not a re-implementation of
// its include globs.
function collectedFilesFor(appRoot) {
  const command = process.platform === 'win32' ? 'npx.cmd' : 'npx';
  const result = spawnSync(command, ['vitest', 'list', '--filesOnly', '--json'], {
    cwd: appRoot,
    encoding: 'utf8',
    shell: true,
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

test('apps with no known include gap report zero uncollected files', async () => {
  const appsWithoutKnownGap = ['kiosk-web', 'management-web'];

  for (const appName of appsWithoutKnownGap) {
    const appRoot = path.join(appsDirectory, appName);
    const collected = collectedFilesFor(appRoot);
    const candidates = await testShapedFilesUnder(path.join(appRoot, 'src'));

    const uncollected = candidates
      .filter((candidate) => !collected.has(normalise(path.resolve(candidate))))
      .map((candidate) => normalise(path.relative(repositoryRoot, candidate)));

    assert.deepEqual(
      uncollected,
      [],
      `expected zero uncollected test files in apps/${appName}, got: ${uncollected.join(', ')}`,
    );
  }
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
