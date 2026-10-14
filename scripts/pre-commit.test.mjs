// Guard for #2869 / spec 355: the pre-commit hook must reject exactly what
// CI's two format gates reject (`dotnet format --verify-no-changes` and
// `prettier --check`), name the offending file, and touch nothing.
//
// Two layers, mirroring scripts/create-new-feature.test.mjs:
//
//  - End to end: the real `scripts/pre-commit.mjs` runs inside throwaway git
//    repositories with real git and real prettier. Every negative case asserts
//    the failing file's *name* in the output, not just a non-zero exit — a
//    missing script also exits non-zero, so an exit-code-only assertion would
//    pass for the wrong reason.
//  - Unit: the pure planning functions, with the `dotnet` runner injected.
//
// `dotnet` itself is never started. End-to-end C# cases put a stand-in named
// `dotnet` first on PATH: a copy of the Node binary, preloaded with a script
// that records its arguments and exits with a scripted code. That keeps the
// suite runnable on the `frontend` CI job, which has no .NET SDK.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import test, { after, before } from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const hookScript = path.join(repositoryRoot, 'scripts', 'pre-commit.mjs');

const scratchRoots = [];

after(() => {
  for (const root of scratchRoots) rmSync(root, { recursive: true, force: true });
});

function scratch(prefix) {
  const root = mkdtempSync(path.join(tmpdir(), prefix));
  scratchRoots.push(root);
  return root;
}

// A missing module must fail the one test that needs it, not the whole file.
async function loadHook() {
  return import(pathToFileURL(hookScript).href);
}

// ---------------------------------------------------------------------------
// Throwaway repositories
// ---------------------------------------------------------------------------

function git(cwd, ...args) {
  const result = spawnSync('git', ['-c', 'core.autocrlf=false', ...args], { cwd, encoding: 'utf8' });
  if (result.status !== 0) {
    throw new Error(`git ${args.join(' ')} failed in ${cwd}:\n${result.stdout}${result.stderr}`);
  }
  return result.stdout;
}

function write(repository, relativePath, content) {
  const target = path.join(repository, ...relativePath.split('/'));
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, content);
}

function commitAll(repository, message) {
  git(repository, 'add', '-A');
  git(repository, '-c', 'user.name=Test', '-c', 'user.email=test@example.com', 'commit', '-q', '-m', message);
}

const CLEAN_TS = "const a = 'x';\n";
const DIRTY_TS = 'const a = "x"\n';
const DIRTY_MARKDOWN = '#   Heading\n\n\n\n*   item\n';
const DIRTY_YAML = 'a:    1\n';

function dirtyContentFor(file) {
  if (file.endsWith('.md')) return DIRTY_MARKDOWN;
  if (file.endsWith('.yml')) return DIRTY_YAML;
  return DIRTY_TS;
}

// A repository with the real prettier config, the real ignore file and a
// package.json carrying the real `format:check`, plus whatever the case needs
// committed as the base.
function buildRepository(baseFiles = {}) {
  const repository = scratch('sse-pre-commit-');
  git(repository, 'init', '-q', '--initial-branch=develop');
  copyFileSync(path.join(repositoryRoot, '.prettierrc.json'), path.join(repository, '.prettierrc.json'));
  copyFileSync(path.join(repositoryRoot, '.prettierignore'), path.join(repository, '.prettierignore'));
  const manifest = JSON.parse(readFileSync(path.join(repositoryRoot, 'package.json'), 'utf8'));
  write(
    repository,
    'package.json',
    JSON.stringify({ name: 'fixture', private: true, scripts: { 'format:check': manifest.scripts['format:check'] } }),
  );
  for (const [file, content] of Object.entries(baseFiles)) write(repository, file, content);
  commitAll(repository, 'base');
  return repository;
}

function stage(repository, files) {
  for (const [file, content] of Object.entries(files)) write(repository, file, content);
  git(repository, 'add', '-A');
}

function snapshot(repository) {
  return [git(repository, 'status', '--porcelain'), git(repository, 'diff'), git(repository, 'diff', '--cached')].join(
    '\n---\n',
  );
}

// ---------------------------------------------------------------------------
// The stand-in `dotnet`
// ---------------------------------------------------------------------------

let shimDirectory = null;
let markerFile = null;

before(() => {
  shimDirectory = scratch('sse-fake-dotnet-');
  markerFile = path.join(shimDirectory, 'invocations.log');
  const executable = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';
  copyFileSync(process.execPath, path.join(shimDirectory, executable));
  writeFileSync(
    path.join(shimDirectory, 'preload.cjs'),
    [
      "const path = require('node:path');",
      "if (path.basename(process.execPath).toLowerCase().startsWith('dotnet')) {",
      "  const fs = require('node:fs');",
      "  fs.appendFileSync(process.env.FAKE_DOTNET_LOG, process.argv.slice(1).join(' ') + '\\n');",
      "  if (process.env.FAKE_DOTNET_OUT) process.stdout.write(process.env.FAKE_DOTNET_OUT + '\\n');",
      "  process.exit(Number(process.env.FAKE_DOTNET_EXIT || '0'));",
      '}',
      '',
    ].join('\n'),
  );
});

function dotnetInvocations() {
  return existsSync(markerFile) ? readFileSync(markerFile, 'utf8').split('\n').filter(Boolean) : [];
}

// ---------------------------------------------------------------------------
// Running the real hook
// ---------------------------------------------------------------------------

function runHook(repository, { dotnetExit = 0, dotnetOutput = '', environment = {} } = {}) {
  rmSync(markerFile, { force: true });
  const env = { ...process.env };
  delete env.HUSKY;
  const pathKey = Object.keys(env).find((key) => key.toLowerCase() === 'path') ?? 'PATH';
  env[pathKey] = `${shimDirectory}${path.delimiter}${env[pathKey] ?? ''}`;
  env.NODE_OPTIONS = `--require ${path.join(shimDirectory, 'preload.cjs')}`;
  env.FAKE_DOTNET_LOG = markerFile;
  env.FAKE_DOTNET_EXIT = String(dotnetExit);
  env.FAKE_DOTNET_OUT = dotnetOutput;
  Object.assign(env, environment);

  const before = existsSync(path.join(repository, '.git')) ? snapshot(repository) : null;
  const result = spawnSync(process.execPath, [hookScript], { cwd: repository, encoding: 'utf8', env });
  const after = existsSync(path.join(repository, '.git')) ? snapshot(repository) : null;
  return {
    status: result.status,
    output: `${result.stdout ?? ''}${result.stderr ?? ''}`,
    untouched: before === after,
  };
}

function assertPassed(outcome) {
  assert.equal(outcome.status, 0, `expected exit 0, got ${outcome.status}:\n${outcome.output}`);
  assert.equal(outcome.untouched, true, 'the hook changed the working tree or the index');
}

function assertBlocked(outcome, ...mustContain) {
  assert.notEqual(outcome.status, 0, `expected a non-zero exit:\n${outcome.output}`);
  for (const text of mustContain) {
    assert.ok(outcome.output.includes(text), `output should contain ${JSON.stringify(text)}:\n${outcome.output}`);
  }
  assert.equal(outcome.untouched, true, 'the hook changed the working tree or the index');
}

// ---------------------------------------------------------------------------
// End to end: the frontend leg
// ---------------------------------------------------------------------------

test('a clean staged frontend file passes', () => {
  const repository = buildRepository();
  stage(repository, { 'apps/a/x.ts': CLEAN_TS });

  assertPassed(runHook(repository));
});

test('a mis-formatted staged frontend file blocks the commit and is named with its fix command', () => {
  const repository = buildRepository();
  stage(repository, { 'apps/a/x.ts': DIRTY_TS });

  const outcome = runHook(repository);

  assertBlocked(outcome, 'apps/a/x.ts', 'pnpm prettier --write apps/a/x.ts', '--no-verify');
});

test('only the mis-formatted file is named when a clean one is staged beside it', () => {
  const repository = buildRepository();
  stage(repository, { 'apps/a/ok.ts': CLEAN_TS, 'apps/a/bad.ts': DIRTY_TS });

  const outcome = runHook(repository);

  assertBlocked(outcome, 'apps/a/bad.ts');
  assert.equal(outcome.output.includes('apps/a/ok.ts'), false, outcome.output);
});

test('a staged path containing a space is named intact', () => {
  const repository = buildRepository();
  stage(repository, { 'apps/a/my file.ts': DIRTY_TS });

  assertBlocked(runHook(repository), 'apps/a/my file.ts');
});

for (const blockedPath of ['e2e/specs/y.ts', 'playwright.config.ts', 'apps/a/notes.md', 'apps/a/ci.yml']) {
  test(`a mis-formatted ${blockedPath} is inside the format gate and blocks`, () => {
    const repository = buildRepository();
    stage(repository, { [blockedPath]: dirtyContentFor(blockedPath) });

    assertBlocked(runHook(repository), blockedPath);
  });
}

for (const ignoredPath of [
  'docs/n.md',
  'specs/999-x/spec.md',
  'scripts/x.mjs',
  '.github/workflows/ci.yml',
  'apps/a/x.mts',
]) {
  test(`a mis-formatted ${ignoredPath} is outside the format gate and passes`, () => {
    const repository = buildRepository();
    stage(repository, { [ignoredPath]: dirtyContentFor(ignoredPath) });

    assertPassed(runHook(repository));
  });
}

test('a mis-formatted file under a .prettierignore d directory passes', () => {
  const repository = buildRepository();
  stage(repository, { 'apps/a/dist/bundle.js': 'var a = "x"\n' });

  assertPassed(runHook(repository));
});

test('a staged-clean file with a mis-formatted unstaged copy is judged by the staged content and passes', () => {
  const repository = buildRepository({ 'apps/a/x.ts': CLEAN_TS });
  write(repository, 'apps/a/x.ts', "const a = 'y';\n");
  git(repository, 'add', 'apps/a/x.ts');
  write(repository, 'apps/a/x.ts', DIRTY_TS);

  assertPassed(runHook(repository));
});

test('a staged-mis-formatted file with a clean unstaged copy is judged by the staged content and blocks', () => {
  const repository = buildRepository({ 'apps/a/x.ts': CLEAN_TS });
  write(repository, 'apps/a/x.ts', DIRTY_TS);
  git(repository, 'add', 'apps/a/x.ts');
  write(repository, 'apps/a/x.ts', CLEAN_TS);

  assertBlocked(runHook(repository), 'apps/a/x.ts');
});

test('a staged deletion of a mis-formatted file and a staged clean rename pass', () => {
  const repository = buildRepository({ 'apps/a/old.ts': DIRTY_TS, 'apps/a/r.ts': CLEAN_TS });
  git(repository, 'rm', '-q', 'apps/a/old.ts');
  git(repository, 'mv', 'apps/a/r.ts', 'apps/a/r2.ts');

  assertPassed(runHook(repository));
});

test('a rename whose destination is mis-formatted blocks and names the destination', () => {
  const repository = buildRepository({ 'apps/a/r.ts': CLEAN_TS });
  git(repository, 'mv', 'apps/a/r.ts', 'apps/a/renamed.ts');
  write(repository, 'apps/a/renamed.ts', DIRTY_TS);
  git(repository, 'add', 'apps/a/renamed.ts');

  assertBlocked(runHook(repository), 'apps/a/renamed.ts');
});

test('the hook leaves a mis-formatted unstaged edit exactly as it found it', () => {
  const repository = buildRepository({ 'apps/a/x.ts': CLEAN_TS, 'apps/a/y.ts': CLEAN_TS });
  write(repository, 'apps/a/y.ts', DIRTY_TS);
  stage(repository, { 'apps/a/x.ts': DIRTY_TS });

  const outcome = runHook(repository);

  assertBlocked(outcome, 'apps/a/x.ts');
  assert.equal(readFileSync(path.join(repository, 'apps', 'a', 'y.ts'), 'utf8'), DIRTY_TS);
  assert.equal(readFileSync(path.join(repository, 'apps', 'a', 'x.ts'), 'utf8'), DIRTY_TS);
});

// ---------------------------------------------------------------------------
// End to end: nothing to do, opt-outs
// ---------------------------------------------------------------------------

test('nothing staged exits 0 without starting dotnet', () => {
  const repository = buildRepository();

  assertPassed(runHook(repository));
  assert.deepEqual(dotnetInvocations(), []);
});

test('only an out-of-scope file staged exits 0 without starting dotnet', () => {
  const repository = buildRepository();
  stage(repository, { 'docs/adr/README.md': DIRTY_MARKDOWN });

  assertPassed(runHook(repository));
  assert.deepEqual(dotnetInvocations(), []);
});

test('HUSKY=0 skips the check even with a mis-formatted file staged', () => {
  const repository = buildRepository();
  stage(repository, { 'apps/a/x.ts': DIRTY_TS });

  assertPassed(runHook(repository, { environment: { HUSKY: '0' } }));
});

test('a directory without .git exits 0', () => {
  const directory = scratch('sse-no-git-');
  write(directory, 'apps/a/x.ts', DIRTY_TS);

  const outcome = runHook(directory, { environment: { GIT_CEILING_DIRECTORIES: path.dirname(directory) } });

  assert.equal(outcome.status, 0, outcome.output);
});

// ---------------------------------------------------------------------------
// End to end: the C# leg, against the stand-in dotnet
// ---------------------------------------------------------------------------

const LIBRARY_BASE = { 'src/Lib/Lib.csproj': '<Project />\n', 'src/Lib/X.cs': 'class X { }\n' };

test('a staged .cs file runs dotnet format against its owning project with the CI flags', () => {
  const repository = buildRepository(LIBRARY_BASE);
  stage(repository, { 'src/Lib/X.cs': 'class X { int a; }\n' });

  assertPassed(runHook(repository));

  const invocations = dotnetInvocations();
  assert.equal(invocations.length, 1, invocations.join('\n'));
  for (const expected of [
    'format',
    'Lib.csproj',
    '--verify-no-changes',
    '--no-restore',
    '--exclude-diagnostics IDE1006',
    '--include',
    'X.cs',
  ]) {
    assert.ok(invocations[0].includes(expected), `dotnet arguments should contain ${expected}: ${invocations[0]}`);
  }
});

test("dotnet format exit 2 blocks, shows dotnet's own diagnostic and names the fix command", () => {
  const repository = buildRepository(LIBRARY_BASE);
  stage(repository, { 'src/Lib/X.cs': 'class X { int a; }\n' });

  const outcome = runHook(repository, {
    dotnetExit: 2,
    dotnetOutput: 'src/Lib/X.cs(1,11): error WHITESPACE: Fix whitespace formatting.',
  });

  assertBlocked(
    outcome,
    'error WHITESPACE: Fix whitespace formatting.',
    'dotnet format src/Lib/Lib.csproj --include src/Lib/X.cs',
    'mis-formatted',
    '--no-verify',
  );
});

test('a dotnet exit code other than 0 or 2 is a tool failure that blocks and says so', () => {
  const repository = buildRepository(LIBRARY_BASE);
  stage(repository, { 'src/Lib/X.cs': 'class X { int a; }\n' });

  const outcome = runHook(repository, { dotnetExit: 1 });

  assertBlocked(outcome, 'dotnet restore', '--no-verify');
  assert.equal(outcome.output.includes('mis-formatted'), false, outcome.output);
});

test('a renamed .cs file is checked under its destination name only', () => {
  const repository = buildRepository({ 'src/Lib/Lib.csproj': '<Project />\n', 'src/Lib/A.cs': 'class A { }\n' });
  git(repository, 'mv', 'src/Lib/A.cs', 'src/Lib/B.cs');

  assertPassed(runHook(repository));

  const invocations = dotnetInvocations();
  assert.equal(invocations.length, 1, invocations.join('\n'));
  assert.ok(invocations[0].includes('B.cs'), invocations[0]);
  assert.equal(invocations[0].includes('A.cs'), false, invocations[0]);
});

test('a deleted .cs file does not start dotnet', () => {
  const repository = buildRepository(LIBRARY_BASE);
  git(repository, 'rm', '-q', 'src/Lib/X.cs');

  assertPassed(runHook(repository));
  assert.deepEqual(dotnetInvocations(), []);
});

test('a staged .cs file with no owning .csproj is not checked', () => {
  const repository = buildRepository();
  stage(repository, { 'src/Orphan/Z.cs': 'class Z { int a; }\n' });

  assertPassed(runHook(repository));
  assert.deepEqual(dotnetInvocations(), []);
});

test('a staged .cs file that also has unstaged changes prints a working-tree note', () => {
  const repository = buildRepository(LIBRARY_BASE);
  stage(repository, { 'src/Lib/X.cs': 'class X { int a; }\n' });
  write(repository, 'src/Lib/X.cs', 'class X { int a; int b; }\n');

  const outcome = runHook(repository);

  assert.equal(outcome.status, 0, outcome.output);
  assert.match(outcome.output, /working[- ]tree/i);
});

test('a mis-formatted frontend file and a failing .cs file are both reported in one attempt', () => {
  const repository = buildRepository(LIBRARY_BASE);
  stage(repository, { 'apps/a/x.ts': DIRTY_TS, 'src/Lib/X.cs': 'class X { int a; }\n' });

  const outcome = runHook(repository, { dotnetExit: 2 });

  assertBlocked(outcome, 'apps/a/x.ts', 'src/Lib/X.cs');
});

// ---------------------------------------------------------------------------
// Unit: classification
// ---------------------------------------------------------------------------

test('classify sorts staged paths into C# and prettier-scope buckets and drops the rest', async () => {
  const { classify } = await loadHook();

  const result = classify([
    'src/A/Domain/X.cs',
    'apps/kiosk-web/src/a.ts',
    'apps/kiosk-web/src/a.tsx',
    'apps/kiosk-web/a.json',
    'apps/kiosk-web/a.css',
    'apps/kiosk-web/a.md',
    'apps/kiosk-web/a.yaml',
    'apps/kiosk-web/a.yml',
    'apps/kiosk-web/a.js',
    'apps/kiosk-web/a.jsx',
    'e2e/specs/s.ts',
    'playwright.config.ts',
    'tests/T/Y.cs',
    'docs/a.md',
    'specs/355-x/spec.md',
    'scripts/x.mjs',
    '.github/workflows/ci.yml',
    'package.json',
    'apps/kiosk-web/a.mts',
    'playwright.config.js',
    'other/apps/a.ts',
  ]);

  assert.deepEqual(result, {
    csharp: ['src/A/Domain/X.cs', 'tests/T/Y.cs'],
    prettier: [
      'apps/kiosk-web/src/a.ts',
      'apps/kiosk-web/src/a.tsx',
      'apps/kiosk-web/a.json',
      'apps/kiosk-web/a.css',
      'apps/kiosk-web/a.md',
      'apps/kiosk-web/a.yaml',
      'apps/kiosk-web/a.yml',
      'apps/kiosk-web/a.js',
      'apps/kiosk-web/a.jsx',
      'e2e/specs/s.ts',
      'playwright.config.ts',
    ],
  });
});

test('classify of nothing is two empty lists', async () => {
  const { classify } = await loadHook();

  assert.deepEqual(classify([]), { csharp: [], prettier: [] });
});

// ---------------------------------------------------------------------------
// Unit: owning project
// ---------------------------------------------------------------------------

function listingOf(tree) {
  const calls = [];
  const list = (directory) => {
    calls.push(directory);
    return tree[directory] ?? [];
  };
  return { list, calls };
}

test('owningProject returns the nearest ancestor directory holding exactly one .csproj', async () => {
  const { owningProject } = await loadHook();
  const { list } = listingOf({
    'src/A/Domain': ['X.cs'],
    'src/A': ['A.csproj', 'Domain'],
    src: ['A', 'Other.csproj'],
  });

  assert.equal(owningProject('src/A/Domain/X.cs', list), 'src/A/A.csproj');
});

test('owningProject prefers a nested project over an outer one', async () => {
  const { owningProject } = await loadHook();
  const { list } = listingOf({
    'src/A/Tests': ['Tests.csproj', 'X.cs'],
    'src/A': ['A.csproj'],
  });

  assert.equal(owningProject('src/A/Tests/X.cs', list), 'src/A/Tests/Tests.csproj');
});

test('owningProject skips a directory holding two .csproj files and keeps walking', async () => {
  const { owningProject } = await loadHook();
  const { list } = listingOf({
    'src/A/Api': ['One.csproj', 'Two.csproj', 'X.cs'],
    'src/A': ['A.csproj'],
  });

  assert.equal(owningProject('src/A/Api/X.cs', list), 'src/A/A.csproj');
});

test('owningProject returns null and never looks above the repository root', async () => {
  const { owningProject } = await loadHook();
  const { list, calls } = listingOf({});

  assert.equal(owningProject('src/A/Domain/X.cs', list), null);
  assert.deepEqual(calls, ['src/A/Domain', 'src/A', 'src', '']);
});

test('owningProject resolves a project at the repository root', async () => {
  const { owningProject } = await loadHook();
  const { list } = listingOf({ '': ['Root.csproj', 'X.cs'] });

  assert.equal(owningProject('X.cs', list), 'Root.csproj');
});

// ---------------------------------------------------------------------------
// Unit: grouping and the solution fallback
// ---------------------------------------------------------------------------

function lookupOf(map) {
  return (file) => map[file] ?? null;
}

test('planDotnetFormat groups files by owning project', async () => {
  const { planDotnetFormat } = await loadHook();
  const lookup = lookupOf({
    'src/A/X.cs': 'src/A/A.csproj',
    'src/A/Y.cs': 'src/A/A.csproj',
    'src/B/Z.cs': 'src/B/B.csproj',
  });

  const plan = planDotnetFormat(['src/A/X.cs', 'src/B/Z.cs', 'src/A/Y.cs'], lookup);

  assert.deepEqual(plan, [
    { project: 'src/A/A.csproj', files: ['src/A/X.cs', 'src/A/Y.cs'] },
    { project: 'src/B/B.csproj', files: ['src/B/Z.cs'] },
  ]);
});

test('planDotnetFormat drops files that have no owning project', async () => {
  const { planDotnetFormat } = await loadHook();

  const plan = planDotnetFormat(['src/A/X.cs', 'loose/Q.cs'], lookupOf({ 'src/A/X.cs': 'src/A/A.csproj' }));

  assert.deepEqual(plan, [{ project: 'src/A/A.csproj', files: ['src/A/X.cs'] }]);
});

test('planDotnetFormat of nothing is an empty plan', async () => {
  const { planDotnetFormat } = await loadHook();

  assert.deepEqual(planDotnetFormat([], lookupOf({})), []);
});

test('planDotnetFormat keeps per-project runs at three projects', async () => {
  const { planDotnetFormat } = await loadHook();
  const lookup = lookupOf({
    'src/A/X.cs': 'src/A/A.csproj',
    'src/B/X.cs': 'src/B/B.csproj',
    'src/C/X.cs': 'src/C/C.csproj',
  });

  const plan = planDotnetFormat(['src/A/X.cs', 'src/B/X.cs', 'src/C/X.cs'], lookup);

  assert.equal(plan.length, 3);
});

test('planDotnetFormat falls back to one solution-wide run above three projects', async () => {
  const { planDotnetFormat } = await loadHook();
  const lookup = lookupOf({
    'src/A/X.cs': 'src/A/A.csproj',
    'src/B/X.cs': 'src/B/B.csproj',
    'src/C/X.cs': 'src/C/C.csproj',
    'src/D/X.cs': 'src/D/D.csproj',
  });

  const plan = planDotnetFormat(['src/A/X.cs', 'src/B/X.cs', 'src/C/X.cs', 'src/D/X.cs'], lookup);

  assert.deepEqual(plan, [
    {
      project: 'SmartSentinelEye.slnx',
      files: ['src/A/X.cs', 'src/B/X.cs', 'src/C/X.cs', 'src/D/X.cs'],
    },
  ]);
});

test('the fallback threshold is three projects', async () => {
  const { SOLUTION_FALLBACK_PROJECT_COUNT } = await loadHook();

  assert.equal(SOLUTION_FALLBACK_PROJECT_COUNT, 3);
});

// ---------------------------------------------------------------------------
// Unit: the dotnet command line
// ---------------------------------------------------------------------------

test("dotnetArgs reproduces CI's flags exactly, targeting the project", async () => {
  const { dotnetArgs } = await loadHook();

  assert.deepEqual(dotnetArgs({ project: 'src/A/A.csproj', files: ['src/A/X.cs', 'src/A/Y.cs'] }), [
    'format',
    'src/A/A.csproj',
    '--verify-no-changes',
    '--no-restore',
    '--exclude-diagnostics',
    'IDE1006',
    '--include',
    'src/A/X.cs',
    'src/A/Y.cs',
  ]);
});

test('dotnetInvocations splits a long file list so each command line stays under the limit', async () => {
  const { dotnetInvocations: invocationsFor } = await loadHook();
  const files = [];
  for (let index = 0; index < 40; index += 1) files.push(`src/A/File${String(index).padStart(2, '0')}.cs`);

  const invocations = invocationsFor({ project: 'src/A/A.csproj', files }, 400);

  assert.ok(invocations.length > 1, 'forty long paths cannot fit one 400-character command line');
  const seen = [];
  for (const args of invocations) {
    assert.ok(args.join(' ').length <= 400, `command line too long: ${args.join(' ').length}`);
    assert.equal(args[1], 'src/A/A.csproj');
    seen.push(...args.slice(args.indexOf('--include') + 1));
  }
  assert.equal(seen.length, 40);
  assert.equal(new Set(seen).size, 40);
  assert.ok(seen.includes('src/A/File00.cs'));
  assert.ok(seen.includes('src/A/File39.cs'));
});

test('dotnetInvocations keeps a short file list in one command', async () => {
  const { dotnetInvocations: invocationsFor } = await loadHook();

  const invocations = invocationsFor({ project: 'src/A/A.csproj', files: ['src/A/X.cs'] }, 30000);

  assert.equal(invocations.length, 1);
});

// ---------------------------------------------------------------------------
// Unit: exit-code mapping, runner injected
// ---------------------------------------------------------------------------

function runnerReturning(...statuses) {
  const calls = [];
  const runner = (args) => {
    calls.push(args);
    return { status: statuses[Math.min(calls.length - 1, statuses.length - 1)] };
  };
  return { runner, calls };
}

const SINGLE_PLAN = [{ project: 'src/A/A.csproj', files: ['src/A/X.cs'] }];

test('checkCsharp passes silently when dotnet exits 0', async () => {
  const { checkCsharp } = await loadHook();
  const { runner, calls } = runnerReturning(0);

  const result = checkCsharp(SINGLE_PLAN, runner);

  assert.deepEqual(result, { ok: true, messages: [] });
  assert.deepEqual(calls, [
    [
      'format',
      'src/A/A.csproj',
      '--verify-no-changes',
      '--no-restore',
      '--exclude-diagnostics',
      'IDE1006',
      '--include',
      'src/A/X.cs',
    ],
  ]);
});

test('checkCsharp reports exit 2 as a formatting failure with the fix command', async () => {
  const { checkCsharp } = await loadHook();
  const { runner } = runnerReturning(2);

  const result = checkCsharp(SINGLE_PLAN, runner);

  assert.equal(result.ok, false);
  assert.equal(result.messages.length, 1);
  assert.ok(result.messages[0].includes('mis-formatted'), result.messages[0]);
  assert.ok(result.messages[0].includes('dotnet format src/A/A.csproj --include src/A/X.cs'), result.messages[0]);
});

test('checkCsharp reports exit 1 as a tool failure, not a formatting failure, and still blocks', async () => {
  const { checkCsharp } = await loadHook();
  const { runner } = runnerReturning(1);

  const result = checkCsharp(SINGLE_PLAN, runner);

  assert.equal(result.ok, false);
  assert.equal(result.messages.length, 1);
  assert.ok(result.messages[0].includes('dotnet restore'), result.messages[0]);
  assert.ok(result.messages[0].includes('--no-verify'), result.messages[0]);
  assert.equal(result.messages[0].includes('mis-formatted'), false, result.messages[0]);
});

test('checkCsharp treats a runner that could not start dotnet as a tool failure', async () => {
  const { checkCsharp } = await loadHook();
  const { runner } = runnerReturning(null);

  const result = checkCsharp(SINGLE_PLAN, runner);

  assert.equal(result.ok, false);
  assert.ok(result.messages[0].includes('dotnet restore'), result.messages[0]);
});

test('checkCsharp keeps going after a failing project so one attempt reports everything', async () => {
  const { checkCsharp } = await loadHook();
  const { runner, calls } = runnerReturning(2, 2);
  const plan = [
    { project: 'src/A/A.csproj', files: ['src/A/X.cs'] },
    { project: 'src/B/B.csproj', files: ['src/B/Z.cs'] },
  ];

  const result = checkCsharp(plan, runner);

  assert.equal(calls.length, 2);
  assert.equal(result.messages.length, 2);
  assert.ok(result.messages[0].includes('src/A/A.csproj'));
  assert.ok(result.messages[1].includes('src/B/B.csproj'));
});

test('checkCsharp of an empty plan never calls the runner', async () => {
  const { checkCsharp } = await loadHook();
  const { runner, calls } = runnerReturning(0);

  assert.deepEqual(checkCsharp([], runner), { ok: true, messages: [] });
  assert.deepEqual(calls, []);
});

// ---------------------------------------------------------------------------
// Unit: the scope cannot drift from format:check
// ---------------------------------------------------------------------------

const FORMAT_CHECK_GLOBS = ['{apps,e2e}/**/*.{ts,tsx,js,jsx,json,css,md,yaml,yml}', 'playwright.config.ts'];

test("FORMAT_CHECK_SCOPE is the two globs CI's format:check names", async () => {
  const { FORMAT_CHECK_SCOPE } = await loadHook();

  assert.deepEqual([...FORMAT_CHECK_SCOPE], FORMAT_CHECK_GLOBS);
});

test("every glob the hook checks appears verbatim in package.json's format:check", async () => {
  const { FORMAT_CHECK_SCOPE, findScopeDrift } = await loadHook();
  const manifest = JSON.parse(readFileSync(path.join(repositoryRoot, 'package.json'), 'utf8'));
  const command = manifest.scripts['format:check'];

  assert.ok(FORMAT_CHECK_SCOPE.length > 0);
  for (const glob of FORMAT_CHECK_SCOPE) assert.ok(command.includes(glob), `format:check lost ${glob}`);
  assert.deepEqual(findScopeDrift(command), []);
});

test('findScopeDrift names a glob that format:check no longer carries', async () => {
  const { findScopeDrift } = await loadHook();

  const drift = findScopeDrift('prettier --check "{apps,e2e}/**/*.{ts,tsx}" playwright.config.ts');

  assert.deepEqual(drift, [FORMAT_CHECK_GLOBS[0]]);
});

test('findScopeDrift of a command carrying both globs is empty', async () => {
  const { findScopeDrift } = await loadHook();

  assert.deepEqual(findScopeDrift(`prettier --check "${FORMAT_CHECK_GLOBS[0]}" ${FORMAT_CHECK_GLOBS[1]}`), []);
});
