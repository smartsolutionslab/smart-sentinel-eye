// Spec 497: the suite names the one origin in one place and no UI port anywhere else. The scanner is a pure function over {path, text} records,
// so each rule is pinned on fixtures; the last case reads the real tree, which is what CI enforces.
import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import { findHardCodedOrigins, isScanned, readScannedFiles } from './e2e-origins.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const none = { permanent: [], migration: [] };

const file = (filePath, text) => ({ path: filePath, text });

test('a hard-coded origin is reported with its file and line', () => {
  const { violations } = findHardCodedOrigins(
    [
      file(
        'e2e/a.spec.ts',
        "const a = 1;\nconst b = await browser.newContext({ baseURL: 'http://localhost:5174' });\n",
      ),
    ],
    none,
  );

  assert.equal(violations.length, 1);
  assert.equal(violations[0].file, 'e2e/a.spec.ts');
  assert.equal(violations[0].line, 2);
  assert.equal(violations[0].port, '5174');
});

test('a bare port number is reported too, because a localhost search would miss it', () => {
  const { violations } = findHardCodedOrigins(
    [file('e2e/k.spec.ts', "expect(new URL(page.url()).port, 'not the provider').toBe('5174');\n")],
    none,
  );

  assert.deepEqual(
    violations.map((v) => [v.file, v.line, v.port]),
    [['e2e/k.spec.ts', 1, '5174']],
  );
});

test('every fixed UI port is reported, 5173 to 5177', () => {
  const text = [
    'http://localhost:5173',
    'http://localhost:5174',
    'http://localhost:5175',
    'http://localhost:5176',
    'http://localhost:5177',
  ].join('\n');
  const { violations } = findHardCodedOrigins([file('e2e/ports.spec.ts', text)], none);

  assert.deepEqual(
    violations.map((v) => v.port),
    ['5173', '5174', '5175', '5176', '5177'],
  );
});

test('the one origin may be spelled in its module and nowhere else', () => {
  const text = "export const ONE_ORIGIN = 'http://localhost:5180';\n";

  assert.deepEqual(findHardCodedOrigins([file('e2e/support/origins.ts', text)], none).violations, []);
  assert.equal(findHardCodedOrigins([file('e2e/one-origin-sign-in.spec.ts', text)], none).violations.length, 1);
});

test('a port named only in a comment is prose, not a hard-coded origin', () => {
  const text = [
    '// Kiosk app (:5174), spec 011',
    ' * the wall display on 5175',
    '/* port 5173 */',
    '# warm :5176 first',
    'const x = ONE_ORIGIN; // was :5174',
  ].join('\n');

  assert.deepEqual(
    findHardCodedOrigins([file('e2e/prose.spec.ts', text)], none).violations.map((v) => v.line),
    [5],
  );
});

test('a number inside an identifier or a version is not a port', () => {
  const text = [
    "const id = '11111111-1111-5173-8000-000000000001';",
    "const v = '1.5174.2';",
    'const big = 15174;',
    'const n = 51730;',
  ].join('\n');

  assert.deepEqual(findHardCodedOrigins([file('e2e/ids.spec.ts', text)], none).violations, []);
});

test('a permanent entry exempts its whole file, and says why', () => {
  const permanent = [{ file: 'scripts/wait-for-e2e-stack.sh', reason: 'readiness waits on the upstream dev servers' }];
  const text = 'curl http://localhost:5176/\nfor port in 5174 5175; do\n';

  assert.deepEqual(
    findHardCodedOrigins([file('scripts/wait-for-e2e-stack.sh', text)], { permanent, migration: [] }).violations,
    [],
  );
  assert.ok(permanent.every((entry) => entry.reason.length > 0));
});

test('a migration entry exempts one port in one file and no other port', () => {
  const migration = [{ file: 'e2e/k.spec.ts', port: '5174', stage: 2 }];
  const text = "a('http://localhost:5174');\nb('http://localhost:5175');\n";
  const { violations } = findHardCodedOrigins([file('e2e/k.spec.ts', text)], { permanent: [], migration });

  assert.deepEqual(
    violations.map((v) => v.port),
    ['5175'],
  );
});

test('a migration entry whose occurrence is gone is stale and is reported, so the list can only shrink', () => {
  const migration = [{ file: 'e2e/moved.spec.ts', port: '5174', stage: 2 }];
  const { stale, violations } = findHardCodedOrigins([file('e2e/moved.spec.ts', 'a(ONE_ORIGIN);\n')], {
    permanent: [],
    migration,
  });

  assert.deepEqual(violations, []);
  assert.deepEqual(stale, [{ file: 'e2e/moved.spec.ts', port: '5174', stage: 2 }]);
});

test('a migration entry for a file that no longer exists is stale too', () => {
  const migration = [{ file: 'e2e/deleted.spec.ts', port: '5175', stage: 3 }];

  assert.equal(findHardCodedOrigins([], { permanent: [], migration }).stale.length, 1);
});

test('what is scanned: the suite, its config, the CI scripts and the application source', () => {
  for (const scanned of [
    'e2e/a.spec.ts',
    'e2e/support/sign-in.ts',
    'playwright.config.ts',
    'scripts/wait-for-e2e-stack.sh',
    '.github/workflows/ci.yml',
    'apps/management-web/src/app/auth.ts',
    'apps/management-web/vite.config.ts',
  ]) {
    assert.equal(isScanned(scanned), true, `${scanned} should be scanned`);
  }
});

test('what is not scanned: origins held as data or pinned on purpose', () => {
  for (const skipped of [
    'e2e/support/sweep-recovery.test.mjs',
    'apps/management-web/src/app/navigation/remotes.test.ts',
    'apps/kiosk-web/src/app/signinPaths.test.ts',
    'src/AppHost/WebApplications.cs',
    'tests/Integration.Tests/ApiGateway/GatewayApiPrefixTests.cs',
    'docs/deployment-frontend-env.md',
    'specs/497-e2e-one-origin/spec.md',
    'scripts/e2e-origins.test.mjs',
    'node_modules/x/index.js',
  ]) {
    assert.equal(isScanned(skipped), false, `${skipped} should not be scanned`);
  }
});

test('the real tree names no fixed UI port outside the allow-lists', () => {
  const { violations, stale } = findHardCodedOrigins(readScannedFiles(root));

  const report = [
    ...violations.map((v) => `${v.file}:${v.line}  ${v.port}  ${v.text.trim()}`),
    ...stale.map((s) => `${s.file}  ${s.port}  stale migration entry (stage ${s.stage}): remove it`),
  ];
  assert.deepEqual(report, [], `hard-coded origins outside the named exceptions:\n${report.join('\n')}`);
});
