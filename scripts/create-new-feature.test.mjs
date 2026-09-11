// Guard for #2177 / spec 135.
//
// `Get-NextBranchNumber` took the maximum of three sources, and two of them —
// `git branch -a` and `git ls-remote --heads`, both via
// `Get-HighestNumberFromNames` — match `^(\d{3,})-`. No branch in this
// repository has ever started with the spec number (they are `fix/2111-…`,
// `docs/2124-…`, where the digits are an *issue* number), so the number came
// entirely from the working tree's `specs/` directory. A checkout that is
// behind therefore mints a number that is already taken — it produced a
// duplicate 084.
//
// These run the real script against real throwaway git repositories, so they
// assert the number the script *returns*, not what its source text contains.
// Each fixture is built so that the answer is only reachable from git: the
// working tree is deliberately left behind `origin/develop`, and one number
// exists only on a pushed-but-unmerged branch.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync, copyFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const scriptSource = path.join(repositoryRoot, '.specify', 'scripts', 'powershell');

// Windows has `powershell` (5.1) and no `pwsh`; the ubuntu CI runner has
// `pwsh` and no `powershell`. Both are tried, so this only skips on a machine
// with neither — which is neither a developer box here nor the frontend job.
function findPowerShell() {
  for (const candidate of ['pwsh', 'powershell']) {
    const probe = spawnSync(candidate, ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.Major'], {
      encoding: 'utf8',
    });
    if (probe.status === 0) return candidate;
  }
  return null;
}

const shell = findPowerShell();

function git(cwd, ...args) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8' });
  if (result.status !== 0) {
    throw new Error(`git ${args.join(' ')} failed in ${cwd}:\n${result.stdout}${result.stderr}`);
  }
  return result.stdout;
}

function addSpec(repository, name) {
  const directory = path.join(repository, 'specs', name);
  mkdirSync(directory, { recursive: true });
  writeFileSync(path.join(directory, 'spec.md'), `# ${name}\n`);
}

function commitAll(repository, message) {
  git(repository, 'add', '-A');
  git(repository, '-c', 'user.name=Test', '-c', 'user.email=test@example.com', 'commit', '-q', '-m', message);
}

// Builds: a bare origin whose `develop` carries 001, 002 and 003; a `work`
// clone left on the commit that only knows 001 and 002 (fetched, not merged);
// and a second clone used to push whatever else the case needs.
function buildFixture() {
  const root = mkdtempSync(path.join(tmpdir(), 'sse-feature-number-'));
  const origin = path.join(root, 'origin.git');
  const work = path.join(root, 'work');
  const other = path.join(root, 'other');

  git(root, 'init', '--bare', '--initial-branch=develop', '-q', origin);
  git(root, 'clone', '-q', origin, work);

  mkdirSync(path.join(work, '.specify', 'scripts', 'powershell'), { recursive: true });
  for (const file of ['create-new-feature.ps1', 'common.ps1']) {
    copyFileSync(path.join(scriptSource, file), path.join(work, '.specify', 'scripts', 'powershell', file));
  }
  addSpec(work, '001-alpha');
  addSpec(work, '002-beta');
  // The timestamp escape the numeric scan must keep ignoring.
  addSpec(work, '20260101-120000-legacy');
  commitAll(work, 'specs 001 and 002');
  git(work, 'push', '-q', '-u', 'origin', 'develop');

  git(root, 'clone', '-q', origin, other);
  addSpec(other, '003-gamma');
  commitAll(other, 'spec 003');
  git(other, 'push', '-q', 'origin', 'develop');

  // `work` now knows origin/develop carries 003 but its own tree does not —
  // exactly the stale checkout the duplicate came from.
  git(work, 'fetch', '-q', 'origin');

  return { root, work, other };
}

function featureNumber(work) {
  const result = spawnSync(
    shell,
    [
      '-NoProfile',
      '-File',
      path.join(work, '.specify', 'scripts', 'powershell', 'create-new-feature.ps1'),
      '-Json',
      '-DryRun',
      '-ShortName',
      'probe',
      'a probe feature',
    ],
    { cwd: work, encoding: 'utf8' },
  );
  assert.equal(result.status, 0, `script failed:\n${result.stdout}\n${result.stderr}`);
  const line = result.stdout.split(/\r?\n/).find((l) => l.trim().startsWith('{'));
  assert.ok(line, `no JSON in script output:\n${result.stdout}\n${result.stderr}`);
  return JSON.parse(line).FEATURE_NUM;
}

test(
  'a checkout behind origin/develop does not mint a number develop already holds',
  { skip: shell ? false : 'no pwsh or powershell on PATH' },
  () => {
    const { root, work } = buildFixture();
    try {
      assert.equal(featureNumber(work), '004');
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  },
);

test(
  'a spec held only by a pushed unmerged branch is not handed out twice',
  { skip: shell ? false : 'no pwsh or powershell on PATH' },
  () => {
    const { root, work, other } = buildFixture();
    try {
      // 004 exists nowhere in develop's tree — only on an in-flight branch,
      // which is how several of today's numbers were held.
      git(other, 'checkout', '-q', '-b', 'fix/1234-delta');
      addSpec(other, '004-delta');
      commitAll(other, 'spec 004');
      git(other, 'push', '-q', 'origin', 'fix/1234-delta');
      git(work, 'fetch', '-q', 'origin');

      assert.equal(featureNumber(work), '005');
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  },
);

test(
  'a timestamp-prefixed spec directory does not become the sequential number',
  { skip: shell ? false : 'no pwsh or powershell on PATH' },
  () => {
    const { root, work, other } = buildFixture();
    try {
      // 20260102-… would parse as a number vastly larger than any spec if the
      // timestamp escape were dropped; 004 says it is still excluded.
      git(other, 'checkout', '-q', 'develop');
      addSpec(other, '20260102-130000-another-legacy');
      commitAll(other, 'a timestamp-prefixed directory');
      git(other, 'push', '-q', 'origin', 'develop');
      git(work, 'fetch', '-q', 'origin');

      assert.equal(featureNumber(work), '004');
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  },
);
