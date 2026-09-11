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

// $STUB_DATABASES_WITH_HISTORY is the set of databases whose
// __EFMigrationsHistory table exists and carries rows; anything else answers
// the way psql does when the relation is not there. $STUB_NO_CONTAINER hides
// the postgres container altogether.
const dockerStub = `#!/usr/bin/env bash
case "$1" in
  ps)
    if [ -n "\${STUB_NO_CONTAINER:-}" ]; then exit 0; fi
    echo "stubpostgres"
    ;;
  exec)
    for argument in "$@"; do
      for database in \${STUB_DATABASES_WITH_HISTORY:-}; do
        case "$argument" in
          *"'$database'"*) echo "4"; exit 0 ;;
        esac
      done
    done
    echo 'ERROR:  relation "__EFMigrationsHistory" does not exist' >&2
    exit 1
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
  return spawnSync('bash', [script], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${stubs}${path.delimiter}${process.env.PATH}`,
      GITHUB_WORKSPACE: repositoryRoot,
      ...environment,
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
