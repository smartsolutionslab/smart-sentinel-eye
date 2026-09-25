// Guard for #2137 / spec 128, gap 2.
//
// `wait-for-e2e-stack.sh` waited on three Vite ports and one gateway route and
// asked nothing about the schema, so a stack whose migration run aborted
// satisfied it and the Playwright suite then failed in ways that describe the
// product rather than the boot.
//
// The script is run for real, under stubbed `curl`, `docker` and `sleep` on
// PATH. The stubs describe a stack — ports serving, gateway answering 401 —
// and vary only in what the databases say about applied migrations. So this
// asserts what the script *does*, not what it contains.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const script = path.join(repositoryRoot, 'scripts', 'wait-for-e2e-stack.sh');

// A bash that cannot be found is the only reason to skip: the frontend CI job
// runs on ubuntu, where it always exists, so this never silently skips there.
const bashAvailable = spawnSync('bash', ['--version'], { encoding: 'utf8' }).status === 0;

// Answers every request the script makes of a healthy stack: the three Vite
// ports serve, the served bundle names a gateway origin, and that gateway
// answers 401 for camera-catalog (auth up, not 5xx).
const curlStub = `#!/usr/bin/env bash
for argument in "$@"; do
  case "$argument" in
    *gateway.ts) echo "export const GATEWAY = 'http://localhost:18888';"; exit 0 ;;
    */camera-catalog/cameras) printf '401'; exit 0 ;;
  esac
done
exit 0
`;

// Instant, so a poll loop that would wait five minutes costs nothing here.
const sleepStub = `#!/usr/bin/env bash
exit 0
`;

// Answers the one exec the script makes per poll with a `<database>=<rows>`
// line each, the way psql-inside-the-container does.
// $STUB_DATABASES_WITH_HISTORY is the set whose __EFMigrationsHistory table
// exists and carries rows; the rest answer `none`, which is what the script
// sees when the relation is not there. $STUB_NO_CONTAINER hides the postgres
// container altogether.
const dockerStub = `#!/usr/bin/env bash
case "$1" in
  ps)
    if [ -n "\${STUB_NO_CONTAINER:-}" ]; then exit 0; fi
    echo "stubpostgres"
    ;;
  exec)
    for database in \${STUB_ALL_DATABASES:-}; do
      rows=none
      for migrated in \${STUB_DATABASES_WITH_HISTORY:-}; do
        if [ "$migrated" = "$database" ]; then rows=4; fi
      done
      printf '%s=%s\\n' "$database" "$rows"
    done
    ;;
esac
exit 0
`;

const allDatabases = [
  'camera-catalog-db',
  'stream-distribution-db',
  'layout-composition-db',
  'overlay-designer-db',
  'system-variables-db',
  'event-ingestion-db',
  'automation-db',
  'identity-db',
  'audit-db',
];

// Guard for #2268 / spec 181.
//
// `wait-for-e2e-stack.sh` waited on three ports, the gateway and the nine
// databases' migration history, and asked nothing about whether the AppHost's
// own composition actually started — so a resource that could not even be
// pulled (minio, #2264) or one still `Waiting` on it (audit-observability)
// satisfied every probe above and the gate opened over a stack that was
// missing a service. The AppHost is meant to write a status report (plan.md
// §2.4) naming every resource it composed and the state each reached; these
// tests describe what the gate must do with that report, under
// `STACK_STATUS_FILE` pointing at a fixture instead of the AppHost's real
// path. The report format and the writer that produces it are phase 4b's job
// (`StackStatusReport.cs`) — none of that exists yet, which is exactly why
// the gate cannot read one today and these tests are red.
//
// The set of resources below stands in for "the composition" without
// depending on the AppHost: every one is Running, except the one-shot
// `migrations` resource, which is Finished with exit code 0 — the shape
// spec.md's happy-path scenario describes.
const composedResources = [
  'postgres',
  'rabbitmq',
  'keycloak',
  'storage',
  'migrations',
  'camera-catalog',
  'stream-distribution',
  'layout-composition',
  'overlay-designer',
  'system-variables',
  'event-ingestion',
  'automation',
  'identity',
  'audit-observability',
  'management-web',
  'kiosk-web',
  'kiosk-wall',
  'fixture-video',
];

// One line per resource in `composedResources`, `<name>\t<state>\t<exit
// code>` (plan.md §2.4), sorted by name so a diff between two reports reads
// cleanly. `overrides` replaces individual resources' `[state, exitCode]`.
function statusReportBody(overrides = {}) {
  const states = new Map(
    composedResources.map((name) => [name, name === 'migrations' ? ['Finished', '0'] : ['Running', '']]),
  );
  for (const [name, entry] of Object.entries(overrides)) {
    states.set(name, entry);
  }

  const lines = ['# stack-status v1 2026-09-18T00:00:00Z'];
  for (const name of [...states.keys()].sort()) {
    const [state, exitCode] = states.get(name);
    lines.push(`${name}\t${state}\t${exitCode}`);
  }
  return `${lines.join('\n')}\n`;
}

// Writes a status-report fixture into its own tmp directory (never the
// AppHost's real path) and returns the path to hand the script via
// `STACK_STATUS_FILE`.
function writeStatusReport(overrides) {
  const directory = mkdtempSync(path.join(tmpdir(), 'wait-for-e2e-stack-status-'));
  const file = path.join(directory, 'stack-status.tsv');
  writeFileSync(file, statusReportBody(overrides));
  return file;
}

function stubDirectory() {
  const directory = mkdtempSync(path.join(tmpdir(), 'wait-for-e2e-stack-'));
  for (const [name, body] of [
    ['curl', curlStub],
    ['docker', dockerStub],
    ['sleep', sleepStub],
  ]) {
    const file = path.join(directory, name);
    writeFileSync(file, body);
    chmodSync(file, 0o755);
  }
  return directory;
}

function runScript(environment) {
  const stubs = stubDirectory();

  // A test that does not name STACK_STATUS_FILE gets the AppHost's own
  // happy-path fixture, written into its own tmp directory and passed
  // explicitly — the resource gate (#2268) is now the first thing every run
  // of this script does, so every test that isn't specifically exercising
  // that gate needs a status report to read, the same way the existing
  // curl/docker/sleep stubs already answer every OTHER probe for a healthy
  // stack. Never the gate's own default path
  // (`${GITHUB_WORKSPACE:-$PWD}/stack-status.tsv`, i.e. this repository's
  // root): a developer running the AppHost locally in this worktree with the
  // default path could have a live report sitting there, and writing (then
  // deleting) over it would be a real bug, not a test-only one. Tests that
  // set their own STACK_STATUS_FILE (or deliberately want none) are
  // unaffected — this only fills in a default, never overrides an explicit
  // one.
  const seededEnvironment = 'STACK_STATUS_FILE' in environment
    ? environment
    : { STACK_STATUS_FILE: writeStatusReport(), ...environment };

  return spawnSync('bash', [script], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${stubs}${path.delimiter}${process.env.PATH}`,
      GITHUB_WORKSPACE: repositoryRoot,
      STUB_ALL_DATABASES: allDatabases.join(' '),
      ...seededEnvironment,
    },
  });
}

test('a stack whose databases carry applied migrations is reported ready', { skip: !bashAvailable }, () => {
  const result = runScript({ STUB_DATABASES_WITH_HISTORY: allDatabases.join(' ') });

  assert.equal(
    result.status,
    0,
    `expected the script to succeed against a fully migrated stack:\n${result.stdout}${result.stderr}`,
  );
});

test('a stack whose migration run aborted partway is not reported ready', { skip: !bashAvailable }, () => {
  // The shape an abort leaves behind: the contexts the runner reached are
  // migrated, the ones after it have no history table at all.
  const result = runScript({ STUB_DATABASES_WITH_HISTORY: allDatabases.slice(0, 5).join(' ') });

  assert.notEqual(
    result.status,
    0,
    `the script accepted a stack whose migration run aborted (#2137):\n${result.stdout}${result.stderr}`,
  );
  assert.match(`${result.stdout}${result.stderr}`, /identity-db/);
});

test('a stack the script cannot ask about migrations is not reported ready', { skip: !bashAvailable }, () => {
  // "I could not ask" is not "the schema is there" — the gateway probe above
  // it is deliberately best-effort, and this one deliberately is not.
  const result = runScript({ STUB_NO_CONTAINER: '1' });

  assert.notEqual(
    result.status,
    0,
    `the script accepted a stack it could not ask about migrations (#2137):\n${result.stdout}${result.stderr}`,
  );
});

test('a stack whose status report names a resource that never started is not reported ready (#2268)', { skip: !bashAvailable }, () => {
  // #2264's stack exactly (the resource that never started was `minio` at the
  // time; the example below uses the stack's current storage resource name,
  // `storage`, per ADR-0155): every existing probe is satisfied — ports
  // serve, the gateway answers 401, every database carries applied
  // migrations — and the only thing wrong is a status report naming storage
  // as FailedToStart and audit-observability as Waiting.
  const statusFile = writeStatusReport({
    storage: ['FailedToStart', ''],
    'audit-observability': ['Waiting', ''],
  });

  const result = runScript({
    STUB_DATABASES_WITH_HISTORY: allDatabases.join(' '),
    STACK_STATUS_FILE: statusFile,
  });

  const output = `${result.stdout}${result.stderr}`;
  assert.notEqual(
    result.status,
    0,
    `the gate opened over a stack missing storage (#2268):\n${output}`,
  );
  // US2: the refusal must name the offender, not just refuse (tasks.md T009).
  assert.match(
    output,
    /storage/,
    `expected the failure to name storage, the resource that never started:\n${output}`,
  );
  // ... and must not name a resource that did start — "postgres" is Running
  // in this fixture, same as every other composed resource but storage and
  // audit-observability.
  assert.doesNotMatch(
    output,
    /postgres/,
    `expected the failure to name only the resources that did not start, not a healthy one:\n${output}`,
  );
});

test('a stack with no status report for the whole wait window is not reported ready (#2268)', { skip: !bashAvailable }, () => {
  // "I could not ask" is not "it started" — the migration probe above already
  // treats a question it could not ask as fatal (#2137), and this reuses that
  // stance for the resource gate: a missing report is not best-effort.
  const directory = mkdtempSync(path.join(tmpdir(), 'wait-for-e2e-stack-status-'));
  const statusFile = path.join(directory, 'never-written.tsv');

  const result = runScript({
    STUB_DATABASES_WITH_HISTORY: allDatabases.join(' '),
    STACK_STATUS_FILE: statusFile,
  });

  const output = `${result.stdout}${result.stderr}`;
  assert.notEqual(
    result.status,
    0,
    `the gate opened with no status report at all (#2268):\n${output}`,
  );
  assert.match(
    output,
    /StackStatusFile/,
    `expected the failure to name the switch that produces the report:\n${output}`,
  );
});

test('a one-shot resource that ended non-zero fails the gate immediately (#2268)', { skip: !bashAvailable }, () => {
  const statusFile = writeStatusReport({
    migrations: ['Finished', '1'],
  });

  const result = runScript({
    STUB_DATABASES_WITH_HISTORY: allDatabases.join(' '),
    STACK_STATUS_FILE: statusFile,
  });

  const output = `${result.stdout}${result.stderr}`;
  assert.notEqual(
    result.status,
    0,
    `the gate opened over a stack whose migrations resource exited 1 (#2268):\n${output}`,
  );
  // Shape, not wall-clock (the sleep stub is already instant): a fatal
  // one-shot resource must stop the gate before it ever reaches the
  // downstream probes that wait on it, so their messages must not appear.
  assert.doesNotMatch(
    output,
    /management-web/,
    `expected the gate to fail before spending the wait budget on resources that wait for migrations (#2268):\n${output}`,
  );
});
