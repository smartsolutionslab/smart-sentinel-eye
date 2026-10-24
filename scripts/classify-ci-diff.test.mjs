// Guard for #3122 / spec 448.
//
// `scripts/classify-ci-diff.sh` sorts a CI diff into one of three buckets:
// `none` (specs/ only), `backend` (paths only the backend job reads) and
// `full` (anything else, and every case where the diff cannot be read).
//
// The script is run for real, inside a throwaway git repository built per
// case, with GITHUB_OUTPUT and GITHUB_STEP_SUMMARY pointed at temp files. So
// this asserts what the script *does* with a diff, not what it contains.
// The last test reads ci.yml, so it is backed by the phase-5 throwaway PR.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const script = path.join(repositoryRoot, 'scripts', 'classify-ci-diff.sh');
const workflowPath = path.join(repositoryRoot, '.github', 'workflows', 'ci.yml');

// A bash that cannot be found is the only reason to skip: the frontend CI job
// runs on ubuntu, where it always exists, so this never silently skips there.
const bashAvailable = spawnSync('bash', ['--version'], { encoding: 'utf8' }).status === 0;
const gitAvailable = spawnSync('git', ['--version'], { encoding: 'utf8' }).status === 0;
const canRun = bashAvailable && gitAvailable;

const zeroSha = '0'.repeat(40);
const unreachableSha = '1'.repeat(40);

// Files present in the base commit, so a change set can edit, delete or move them.
const seedFiles = {
  'src/Foo.cs': 'class Foo {}\n',
  'docs/adr/0001-seed.md': '# seed\n',
  'specs/001-seed/spec.md': '# seed\n',
  'CLAUDE.md': '# seed\n',
};

function git(directory, ...args) {
  const result = spawnSync('git', args, { cwd: directory, encoding: 'utf8' });
  assert.equal(result.status, 0, `git ${args.join(' ')} failed: ${result.stderr}`);
  return result.stdout.trim();
}

function write(directory, relativePath, content) {
  const target = path.join(directory, relativePath);
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, content);
}

// Builds a repository with a base commit, applies `change`, commits the head,
// runs the script and returns what it reported.
function classify({ change, event = 'pull_request', base = 'real', head = 'real', pathPrefix }) {
  const directory = mkdtempSync(path.join(tmpdir(), 'classify-ci-diff-'));
  git(directory, 'init', '-q');
  git(directory, 'config', 'user.email', 'test@example.invalid');
  git(directory, 'config', 'user.name', 'test');
  git(directory, 'config', 'commit.gpgsign', 'false');
  for (const [file, content] of Object.entries(seedFiles)) write(directory, file, content);
  git(directory, 'add', '-A');
  git(directory, 'commit', '-q', '-m', 'base');
  const baseSha = git(directory, 'rev-parse', 'HEAD');

  change?.(directory);
  git(directory, 'add', '-A');
  git(directory, 'commit', '-q', '--allow-empty', '-m', 'head');
  const headSha = git(directory, 'rev-parse', 'HEAD');

  const baseArgument = { real: baseSha, zero: zeroSha, empty: '', unreachable: unreachableSha }[base];
  const headArgument = head === 'real' ? headSha : unreachableSha;

  const outputFile = path.join(directory, '.github-output');
  const summaryFile = path.join(directory, '.github-summary');
  writeFileSync(outputFile, '');
  writeFileSync(summaryFile, '');

  const result = spawnSync('bash', [script, event, baseArgument, headArgument], {
    cwd: directory,
    encoding: 'utf8',
    env: {
      ...process.env,
      ...(pathPrefix ? { PATH: `${pathPrefix}${path.delimiter}${process.env.PATH}` } : {}),
      GITHUB_OUTPUT: outputFile,
      GITHUB_STEP_SUMMARY: summaryFile,
    },
  });

  const outputLines = readFileSync(outputFile, 'utf8').split('\n').filter(Boolean);
  const bucketLines = outputLines.filter((line) => line.startsWith('bucket='));
  const summaryLines = readFileSync(summaryFile, 'utf8').split('\n').filter(Boolean);
  return {
    status: result.status,
    stderr: result.stderr,
    firstOutput: outputLines[0],
    // GitHub keeps the last write of a key, so the last one is the decision.
    lastBucket: bucketLines.length ? bucketLines[bucketLines.length - 1].slice('bucket='.length) : undefined,
    summaryLines,
  };
}

function expectBucket(result, bucket) {
  assert.equal(result.status, 0, `script must exit 0; stderr: ${result.stderr}`);
  assert.equal(result.firstOutput, 'bucket=full', 'bucket=full must be the first write');
  assert.equal(result.lastBucket, bucket);
  assert.equal(result.summaryLines.length, 1, `exactly one summary line, got: ${JSON.stringify(result.summaryLines)}`);
  assert.ok(
    result.summaryLines[0].includes(`bucket=${bucket}`),
    `summary must name bucket=${bucket}, got: ${result.summaryLines[0]}`,
  );
}

const touching =
  (...files) =>
  (directory) => {
    for (const file of files) write(directory, file, `changed ${file}\n`);
  };

const options = { skip: canRun ? false : 'bash or git is unavailable' };

test('classify_ci_diff_script_exists_and_is_executable_in_git', options, () => {
  assert.ok(existsSync(script), `${script} does not exist`);
  const mode = git(repositoryRoot, 'ls-files', '-s', 'scripts/classify-ci-diff.sh');
  assert.ok(mode.startsWith('100755'), `script must be committed executable, got: ${mode}`);
});

test('a_docs_adr_only_diff_is_backend', options, () => {
  expectBucket(classify({ change: touching('docs/adr/0160-x.md') }), 'backend');
});

for (const file of [
  'CLAUDE.md',
  '.specify/memory/constitution.md',
  '.claude/agents/x.md',
  '.claude/commands/x.md',
  'docs/runbooks/x.md',
  'docs/design/x.md',
]) {
  test(`${file}_alone_is_backend`, options, () => {
    expectBucket(classify({ change: touching(file) }), 'backend');
  });
}

test('a_specs_only_diff_is_none_and_the_summary_names_the_specs_skip', options, () => {
  const result = classify({ change: touching('specs/999-x/spec.md') });
  expectBucket(result, 'none');
  assert.match(result.summaryLines[0], /specs\//);
});

test('specs_plus_an_adr_is_backend_not_none', options, () => {
  expectBucket(classify({ change: touching('specs/999-x/spec.md', 'docs/adr/0160-x.md') }), 'backend');
});

test('an_adr_plus_one_src_file_is_full', options, () => {
  expectBucket(classify({ change: touching('docs/adr/0160-x.md', 'src/Cameras/Domain/Camera.cs') }), 'full');
});

for (const file of [
  '.specify/scripts/powershell/x.ps1',
  '.specify/templates/x.md',
  'docs/deployment-frontend-env.md',
  'docs/adrx.md',
  'apps/CLAUDE.md',
  'src/docs/adr/x.md',
  'CLAUDE.md.bak',
]) {
  test(`near_miss_${file}_is_full`, options, () => {
    expectBucket(classify({ change: touching(file) }), 'full');
  });
}

test('a_rename_out_of_src_into_docs_adr_is_full', options, () => {
  const change = (directory) => {
    mkdirSync(path.join(directory, 'docs', 'adr'), { recursive: true });
    git(directory, 'mv', 'src/Foo.cs', 'docs/adr/Foo.md');
  };
  expectBucket(classify({ change }), 'full');
});

test('a_rename_out_of_src_into_specs_is_full', options, () => {
  const change = (directory) => {
    mkdirSync(path.join(directory, 'specs'), { recursive: true });
    git(directory, 'mv', 'src/Foo.cs', 'specs/Foo.md');
  };
  expectBucket(classify({ change }), 'full');
});

test('an_empty_diff_is_full', options, () => {
  expectBucket(classify({}), 'full');
});

test('an_all_zero_base_on_push_is_full', options, () => {
  expectBucket(classify({ event: 'push', base: 'zero', change: touching('docs/adr/0160-x.md') }), 'full');
});

test('an_empty_base_on_workflow_dispatch_is_full', options, () => {
  expectBucket(
    classify({ event: 'workflow_dispatch', base: 'empty', change: touching('docs/adr/0160-x.md') }),
    'full',
  );
});

test('an_unreachable_base_commit_is_full', options, () => {
  expectBucket(classify({ base: 'unreachable', change: touching('docs/adr/0160-x.md') }), 'full');
});

test('an_unreadable_head_commit_is_full', options, () => {
  expectBucket(classify({ head: 'unreachable', change: touching('docs/adr/0160-x.md') }), 'full');
});

test('a_git_that_crashes_leaves_bucket_full_written_first', options, () => {
  const stubDirectory = mkdtempSync(path.join(tmpdir(), 'classify-ci-diff-stub-'));
  const stub = path.join(stubDirectory, 'git');
  writeFileSync(stub, '#!/usr/bin/env bash\nexit 139\n');
  chmodSync(stub, 0o755);
  const result = classify({ pathPrefix: stubDirectory, change: touching('docs/adr/0160-x.md') });
  assert.equal(result.firstOutput, 'bucket=full');
  assert.equal(result.lastBucket, 'full');
});

test('ci_yml_wires_the_three_buckets_into_the_job_gates', () => {
  const workflow = readFileSync(workflowPath, 'utf8');
  const jobs = {};
  let current;
  let inJobs = false;
  for (const line of workflow.split('\n')) {
    if (/^jobs:\s*$/.test(line)) inJobs = true;
    else if (inJobs) {
      const header = /^ {2}([A-Za-z0-9_-]+):\s*$/.exec(line);
      if (header) {
        current = header[1];
        jobs[current] = '';
      }
    }
    if (inJobs && current) jobs[current] += `${line}\n`;
  }

  const field = (job, key) => {
    const match = new RegExp(`^ {4}${key}:(.*)$`, 'm').exec(jobs[job] ?? '');
    return match ? match[1] : '';
  };

  assert.ok(jobs.changes, 'changes job missing');
  assert.ok(jobs.changes.includes('scripts/classify-ci-diff.sh'), 'changes must run scripts/classify-ci-diff.sh');
  assert.ok(!workflow.includes('docs_only'), 'docs_only must be gone from ci.yml');

  const failSafe = "needs.changes.outputs.bucket != 'none' && needs.changes.outputs.bucket != 'backend'";
  assert.equal(field('backend', 'if').trim(), "needs.changes.outputs.bucket != 'none'", 'backend must run unless bucket is none');
  for (const job of ['frontend', 'integration-shards', 'integration-shard-coverage', 'e2e-shards', 'e2e-shard-coverage']) {
    assert.equal(field(job, 'if').trim(), failSafe, `${job} must skip only on a known none or backend bucket`);
  }
  for (const job of ['integration', 'e2e']) {
    assert.equal(field(job, 'if').trim(), `always() && ${failSafe}`, `${job} must be always() and skip only on none or backend`);
  }
  for (const job of ['integration-shards', 'integration-shard-coverage', 'e2e-shards']) {
    assert.ok(/\bchanges\b/.test(field(job, 'needs')), `${job} must need changes`);
  }
});

test('ci_yml_maps_the_changes_bucket_output_to_the_classify_step_id', () => {
  const workflow = readFileSync(workflowPath, 'utf8');
  const output = /^ {6}bucket: [$][{][{] steps[.]([A-Za-z0-9_-]+)[.]outputs[.]bucket [}][}] *$/m.exec(workflow);
  assert.ok(output, 'changes.outputs.bucket must reference steps.<id>.outputs.bucket');
  const step = new RegExp(`^ {8}id: ${output[1]} *$`, 'm').exec(workflow);
  assert.ok(step, `a step with id: ${output[1]} must exist`);
  const after = workflow.slice(step.index, step.index + 1500);
  assert.ok(after.includes('scripts/classify-ci-diff.sh'), 'that step must be the one running the classifier');
});

test('a_non_ascii_path_is_classified_by_its_real_path', options, () => {
  expectBucket(classify({ change: touching('docs/adr/0161-ü.md') }), 'backend');
});
