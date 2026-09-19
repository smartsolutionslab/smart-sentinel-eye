// Guard for #2287 / spec 186 ("A trace that keeps its secrets").
//
// `playwright.config.ts` traces every retried CI run (`trace:
// 'on-first-retry'`), and ADR-0108's real Keycloak sign-in means a trace
// records real bearer tokens, real refresh tokens, a real client secret and
// the seeded realm passwords — verbatim, in a zip that `ci.yml` uploads for
// 14 days to anyone who can read this repository's Actions artifacts
// (`specs/186-a-trace-that-keeps-its-secrets/spec.md` §1.3).
//
// This guard has two halves, and both matter (AS-2, `spec.md` §5):
//
//   - The **presence** assertions (T004) prove the committed
//     `leaky-trace.zip` fixture is genuinely leaky — a real Chromium trace,
//     not a synthetic shape the guard's own author invented. Without this
//     half, a scrubber that deleted the fixture outright would satisfy every
//     absence assertion while proving nothing.
//   - The **absence** and **invariant** assertions (T005-T007) prove
//     `scripts/scrub-playwright-artifacts.mjs` actually closes every leak
//     `spec.md` §1.3 found, fails closed on a file it cannot process
//     (AS-6), and refuses to report success having scrubbed nothing (AS-7,
//     US2) — the same "assertion passing over an empty set" defect class
//     spec 185 was filed against.
//
// The fixtures live in `scripts/fixtures/trace-redaction/`:
//   - `leaky-trace.zip` — a REAL trace, produced by real headless Chromium
//     under real Playwright 1.62.1 tracing (`make-leaky-trace.mjs`, run by
//     hand — see its header comment). Every secret in it is a planted
//     `SSE_FAKE_*` sentinel (`sentinels.mjs`); nothing real leaked in.
//   - `leaky-report.json` — a minimal Playwright JSON report with the same
//     sentinel planted in the four fields
//     `summarise-e2e-retries.test.mjs:17-19` already names as capable of
//     carrying a token: `stdout`, `stderr`, an error `message`, and an
//     attachment `body`.
//
// The scrubber is invoked as a real child process — the same shape
// `summarise-e2e-retries.test.mjs` already uses for its own script — against
// a temp copy of the fixtures, never the committed originals.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

import { readZipEntries } from './fixtures/trace-redaction/read-zip-entries.mjs';
import {
  ACCESS_TOKEN_SENTINEL,
  CLIENT_SECRET_SENTINEL,
  PASSWORD_SENTINEL,
  REFRESH_TOKEN_SENTINEL,
} from './fixtures/trace-redaction/sentinels.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const scrubberScript = path.join(repositoryRoot, 'scripts', 'scrub-playwright-artifacts.mjs');
const fixturesDir = path.join(repositoryRoot, 'scripts', 'fixtures', 'trace-redaction');
const leakyTracePath = path.join(fixturesDir, 'leaky-trace.zip');
const leakyReportPath = path.join(fixturesDir, 'leaky-report.json');

const ALL_SENTINELS = [ACCESS_TOKEN_SENTINEL, REFRESH_TOKEN_SENTINEL, CLIENT_SECRET_SENTINEL, PASSWORD_SENTINEL];

// ---- fixture-tree builder ----------------------------------------------
//
// Mirrors the two trees `ci.yml` uploads (`spec.md` §1.4): Playwright's own
// `test-results/<project>-<test>-<retry>/trace.zip`, and the HTML reporter's
// `playwright-report/data/<sha1>` copy — deliberately given **no extension**,
// exactly as one of the reporter's two attachment-naming spellings produces
// (`spec.md` §1.4), so AS-4 (magic-bytes detection, not a `*.zip` filter) has
// something real to catch a naive scrubber missing.

function buildLeakyArtifactTree() {
  const root = mkdtempSync(path.join(tmpdir(), 'scrub-playwright-artifacts-'));

  const testResultsDir = path.join(root, 'test-results');
  const traceDir = path.join(testResultsDir, 'sign-in-chromium-retry1');
  mkdirSync(traceDir, { recursive: true });
  cpSync(leakyTracePath, path.join(traceDir, 'trace.zip'));
  cpSync(leakyReportPath, path.join(testResultsDir, 'e2e-report.json'));

  const playwrightReportDir = path.join(root, 'playwright-report');
  const reportDataDir = path.join(playwrightReportDir, 'data');
  mkdirSync(reportDataDir, { recursive: true });
  // AS-4: an extension-less copy, named the way the HTML reporter names one
  // of its two attachment spellings (`calculateSha1(attachment.path + salt)`,
  // no extension — `spec.md` §1.4). The exact name is irrelevant; the
  // missing `.zip` suffix on real zip bytes is the point.
  cpSync(leakyTracePath, path.join(reportDataDir, '5f4dcc3b5aa765d61d8327deb882cf99'));

  return {
    root,
    testResultsDir,
    playwrightReportDir,
    tracePath: path.join(traceDir, 'trace.zip'),
    reportPath: path.join(testResultsDir, 'e2e-report.json'),
    extensionlessCopyPath: path.join(reportDataDir, '5f4dcc3b5aa765d61d8327deb882cf99'),
  };
}

// ---- running the real scrubber -----------------------------------------

function runScrubber(directories) {
  return spawnSync('node', [scrubberScript, ...directories], {
    cwd: repositoryRoot,
    encoding: 'utf8',
  });
}

function describeScrubberFailure(result) {
  return `exit ${result.status}\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`;
}

// ---- zip inspection helpers ---------------------------------------------

function decodeEntry(entries, name) {
  const buffer = entries.get(name);
  assert.ok(buffer, `expected an entry named "${name}" in the zip; got: ${[...entries.keys()].join(', ')}`);
  return buffer.toString('utf8');
}

function entriesMatching(entries, pattern) {
  return [...entries].filter(([name]) => pattern.test(name));
}

function readTraceZip(zipPath) {
  return readZipEntries(readFileSync(zipPath));
}

// Every byte of every entry, decoded loss-free (latin1 is a byte-preserving
// round trip in Node — `plan.md` §7.4) — used where a sentinel could in
// principle appear anywhere, including a binary resource.
function allEntryBytesAsText(entries) {
  return [...entries.values()].map((buffer) => buffer.toString('latin1')).join('\n---entry-boundary---\n');
}

// AS-3's own trap, checked on every `trace.network` record: `request.url`
// and the separate `request.queryString[]` entry are two different fields
// that both carry the token in a real trace (`spec.md` §1.3 S2). A scrubber
// that rewrites one and forgets the other must fail this, not the
// whole-file substring check above, which cannot tell them apart.
function assertNetworkUrlAndQueryStringClean(networkText, label) {
  const lines = networkText.split('\n').filter((line) => line.trim().length > 0);
  assert.ok(lines.length > 0, `${label}: expected at least one trace.network record to check`);

  for (const line of lines) {
    const record = JSON.parse(line);
    const request = record?.snapshot?.request;
    if (!request) continue;

    assert.ok(
      !String(request.url ?? '').includes(ACCESS_TOKEN_SENTINEL),
      `${label}: request.url still carries the access-token sentinel: ${request.url}`,
    );
    for (const parameter of request.queryString ?? []) {
      assert.ok(
        !String(parameter.value ?? '').includes(ACCESS_TOKEN_SENTINEL),
        `${label}: request.queryString still carries the access-token sentinel (name=${parameter.name})`,
      );
    }
  }
}

// ---- T004 — presence (AS-2): the fixture is genuinely leaky, before any scrubbing ----

test('T004 AS-2: leaky-trace.zip carries every sentinel in the entries spec.md §1.3 names, unscrubbed', () => {
  const entries = readTraceZip(leakyTracePath);

  const networkText = decodeEntry(entries, 'trace.network');
  const traceText = decodeEntry(entries, 'trace.trace');
  const jsonResourceTexts = entriesMatching(entries, /^resources\/.*\.json$/).map(([, buffer]) => buffer.toString('utf8'));
  const datResourceTexts = entriesMatching(entries, /^resources\/.*\.dat$/).map(([, buffer]) => buffer.toString('utf8'));

  assert.ok(jsonResourceTexts.length > 0, 'expected at least one resources/*.json entry in the fixture');
  assert.ok(datResourceTexts.length > 0, 'expected at least one resources/*.dat entry in the fixture');

  // SSE_FAKE_ACCESS_TOKEN_PAYLOAD -> trace.network AND a resources/*.json
  assert.ok(networkText.includes(ACCESS_TOKEN_SENTINEL), 'expected the access-token sentinel in trace.network');
  assert.ok(
    jsonResourceTexts.some((text) => text.includes(ACCESS_TOKEN_SENTINEL)),
    'expected the access-token sentinel in a resources/*.json file (the token endpoint response body)',
  );

  // SSE_FAKE_REFRESH_TOKEN_VALUE -> a resources/*.json
  assert.ok(
    jsonResourceTexts.some((text) => text.includes(REFRESH_TOKEN_SENTINEL)),
    'expected the refresh-token sentinel in a resources/*.json file — it appears nowhere else (spec.md §1.3 S3)',
  );

  // SSE_FAKE_CLIENT_SECRET -> trace.network, trace.trace, resources/*.dat
  assert.ok(networkText.includes(CLIENT_SECRET_SENTINEL), 'expected the client-secret sentinel in trace.network');
  assert.ok(traceText.includes(CLIENT_SECRET_SENTINEL), 'expected the client-secret sentinel in trace.trace');
  assert.ok(
    datResourceTexts.some((text) => text.includes(CLIENT_SECRET_SENTINEL)),
    'expected the client-secret sentinel in a resources/*.dat file (the token request post body)',
  );

  // SSE_FAKE_PASSWORD_VALUE -> trace.trace, in all three sub-places (spec.md §1.3 S5)
  assert.match(
    traceText,
    new RegExp(`"method":"fill"[^}]*"value":"${PASSWORD_SENTINEL}"`),
    'expected the password sentinel in a fill action\'s params.value',
  );
  assert.match(
    traceText,
    new RegExp(`"type":"log"[^}]*fill\\(\\\\"${PASSWORD_SENTINEL}\\\\"\\)`),
    'expected the password sentinel in the fill action\'s log message',
  );
  assert.match(
    traceText,
    new RegExp(`"__playwright_value_":"${PASSWORD_SENTINEL}"`),
    'expected the password sentinel in a DOM snapshot\'s __playwright_value_ attribute',
  );

  // AS-3's own surface, confirmed present before any fix: the SignalR
  // negotiate carries the token in both request.url and request.queryString
  // in the same record.
  assertUrlAndQueryStringBothCarryToken(networkText);
});

function assertUrlAndQueryStringBothCarryToken(networkText) {
  const lines = networkText.split('\n').filter((line) => line.trim().length > 0);
  const negotiateRecords = lines
    .map((line) => JSON.parse(line))
    .filter((record) => String(record?.snapshot?.request?.url ?? '').includes('/negotiate'));

  assert.ok(negotiateRecords.length > 0, 'expected at least one negotiate request in trace.network');
  assert.ok(
    negotiateRecords.some((record) => String(record.snapshot.request.url).includes(ACCESS_TOKEN_SENTINEL)),
    'expected the negotiate request.url to carry the access-token sentinel',
  );
  assert.ok(
    negotiateRecords.some((record) =>
      (record.snapshot.request.queryString ?? []).some((parameter) => String(parameter.value).includes(ACCESS_TOKEN_SENTINEL)),
    ),
    'expected the negotiate request.queryString[] to separately carry the access-token sentinel',
  );
}

// ---- T005 — absence (AS-1, AS-3, AS-4, AS-5): every leak is closed ------

test('T005 AS-1/AS-3/AS-5: the scrubber removes every sentinel from the test-results trace and the JSON report', () => {
  const tree = buildLeakyArtifactTree();

  const result = runScrubber([tree.testResultsDir, tree.playwrightReportDir]);
  assert.equal(result.status, 0, describeScrubberFailure(result));

  const scrubbedEntries = readTraceZip(tree.tracePath);
  const allText = allEntryBytesAsText(scrubbedEntries);
  for (const sentinel of ALL_SENTINELS) {
    assert.ok(!allText.includes(sentinel), `expected no "${sentinel}" left anywhere in the scrubbed test-results trace`);
  }

  // AS-3: url and queryString checked independently, not just "no sentinel
  // anywhere" — a scrubber that rewrites the URL and forgets the parsed
  // query array would pass a whole-file substring check by accident.
  assertNetworkUrlAndQueryStringClean(decodeEntry(scrubbedEntries, 'trace.network'), 'test-results trace');

  // AS-5: the JSON report's four token-bearing fields, and it must still parse.
  const scrubbedReportText = readFileSync(tree.reportPath, 'utf8');
  let scrubbedReport;
  assert.doesNotThrow(() => {
    scrubbedReport = JSON.parse(scrubbedReportText);
  }, `expected test-results/e2e-report.json to remain valid JSON after scrubbing:\n${scrubbedReportText}`);

  for (const sentinel of ALL_SENTINELS) {
    assert.ok(
      !scrubbedReportText.includes(sentinel),
      `expected no "${sentinel}" left in the scrubbed e2e-report.json`,
    );
  }

  const result0 = scrubbedReport.suites[0].specs[0].tests[0].results[0];
  assert.ok(!result0.stdout[0].text.includes(ACCESS_TOKEN_SENTINEL), 'expected stdout redacted');
  assert.ok(!result0.stderr[0].text.includes(ACCESS_TOKEN_SENTINEL), 'expected stderr redacted');
  assert.ok(!result0.error.message.includes(ACCESS_TOKEN_SENTINEL), 'expected the error message redacted');
  assert.ok(!result0.attachments[0].body.includes(ACCESS_TOKEN_SENTINEL), 'expected the attachment body redacted');
});

test('T005 AS-4: an extension-less copy of the trace (as the HTML reporter writes it) is scrubbed too', () => {
  const tree = buildLeakyArtifactTree();

  const result = runScrubber([tree.testResultsDir, tree.playwrightReportDir]);
  assert.equal(result.status, 0, describeScrubberFailure(result));

  assert.ok(existsSync(tree.extensionlessCopyPath), 'expected the extension-less copy to still exist (scrubbed, not deleted)');

  const scrubbedEntries = readTraceZip(tree.extensionlessCopyPath);
  const allText = allEntryBytesAsText(scrubbedEntries);
  for (const sentinel of ALL_SENTINELS) {
    assert.ok(
      !allText.includes(sentinel),
      `expected no "${sentinel}" left in the extension-less playwright-report/data copy — ` +
        'a scrubber that filters on "*.zip" would skip this file and report success (spec.md §1.4)',
    );
  }
});

// ---- T006 — invariants (AS-6, AS-7): fail closed, and never over nothing ----

test('T006 AS-6: a corrupt zip is deleted rather than left for the upload, and the scrubber exits non-zero', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'scrub-playwright-artifacts-corrupt-'));
  const testResultsDir = path.join(root, 'test-results', 'broken-trace');
  mkdirSync(testResultsDir, { recursive: true });

  // Anchored to real bytes, not a hand-crafted "corrupt" blob (plan.md §9):
  // truncate a copy of the real fixture so it is unmistakably a real zip
  // that got cut off, the way a killed job would leave one.
  const realBytes = readFileSync(leakyTracePath);
  const corruptTracePath = path.join(testResultsDir, 'trace.zip');
  writeFileSync(corruptTracePath, realBytes.subarray(0, Math.floor(realBytes.length / 2)));

  const playwrightReportDir = path.join(root, 'playwright-report');
  mkdirSync(path.join(playwrightReportDir, 'data'), { recursive: true });

  const result = runScrubber([path.join(root, 'test-results'), playwrightReportDir]);

  assert.notEqual(result.status, 0, `expected a non-zero exit when a corrupt zip is encountered:\n${describeScrubberFailure(result)}`);
  assert.ok(
    !existsSync(corruptTracePath),
    `expected the corrupt trace to be deleted rather than left for the upload:\n${describeScrubberFailure(result)}`,
  );
});

test('T006 AS-7: pointing the scrubber at directories with no artifacts fails, and says why, rather than reporting a comforting zero', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'scrub-playwright-artifacts-empty-'));
  const testResultsDir = path.join(root, 'test-results');
  const playwrightReportDir = path.join(root, 'playwright-report');
  mkdirSync(testResultsDir, { recursive: true });
  mkdirSync(playwrightReportDir, { recursive: true });

  const result = runScrubber([testResultsDir, playwrightReportDir]);
  const combinedOutput = `${result.stdout}\n${result.stderr}`;

  assert.notEqual(result.status, 0, `expected a non-zero exit when zero artifacts were found:\n${describeScrubberFailure(result)}`);
  // Distinguishes a genuine "I looked and found nothing" report from the
  // script simply not existing or crashing before it could look — the same
  // "assertion passing over an empty set" defect class spec 185 was filed
  // against, reproduced here inside its own fix (tasks.md).
  assert.doesNotMatch(
    combinedOutput,
    /cannot find module/i,
    `expected a deliberate "zero artifacts" report, not a missing script:\n${describeScrubberFailure(result)}`,
  );
  assert.match(
    combinedOutput,
    /\b0\b/,
    `expected the scrubber to state the count of artifacts it processed (zero):\n${describeScrubberFailure(result)}`,
  );
});

// ---- T007 — structural integrity (SC-003) --------------------------------

test('T007 SC-003: the scrubbed trace still unzips, trace.network still parses line-by-line, and entry names are unchanged', () => {
  const tree = buildLeakyArtifactTree();
  const originalNames = new Set(readTraceZip(tree.tracePath).keys());

  const result = runScrubber([tree.testResultsDir, tree.playwrightReportDir]);
  assert.equal(result.status, 0, describeScrubberFailure(result));

  let scrubbedEntries;
  assert.doesNotThrow(() => {
    scrubbedEntries = readTraceZip(tree.tracePath);
  }, 'expected the scrubbed trace to still be a well-formed zip that re-unzips cleanly');

  const scrubbedNames = new Set(scrubbedEntries.keys());
  assert.deepEqual(
    [...scrubbedNames].sort(),
    [...originalNames].sort(),
    'expected the same set of entry names before and after scrubbing — scrubbing may only alter contents, not add, remove or rename entries (plan.md §7.3)',
  );

  const networkText = decodeEntry(scrubbedEntries, 'trace.network');
  const lines = networkText.split('\n').filter((line) => line.trim().length > 0);
  assert.ok(lines.length > 0, 'expected at least one trace.network line to check');
  for (const line of lines) {
    assert.doesNotThrow(() => JSON.parse(line), `expected every trace.network line to remain valid JSON after scrubbing:\n${line}`);
  }
});
