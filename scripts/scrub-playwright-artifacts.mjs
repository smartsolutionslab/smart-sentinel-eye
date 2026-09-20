#!/usr/bin/env node
// scripts/scrub-playwright-artifacts.mjs
//
// #2287 / spec 186 ("A trace that keeps its secrets").
// `specs/186-a-trace-that-keeps-its-secrets/spec.md` §1.3 opened a real
// Playwright trace produced by this stack's real Keycloak sign-in and found a
// bearer token, a refresh token, a client secret and the seeded realm
// password sitting verbatim in the zip `ci.yml` uploads for 14 days. This
// script runs between the e2e job's `Stop the AppHost` and
// `Upload Playwright report` steps and rewrites those artifacts in place so
// none of that reaches the upload.
//
//   node scripts/scrub-playwright-artifacts.mjs [dir...]
//
// Defaults to `test-results` and `playwright-report`, relative to the
// working directory — the same two trees `ci.yml` uploads. Directories are
// arguments (rather than hardcoded) so the guard
// (`scripts/scrub-playwright-artifacts.test.mjs`) can point this at a temp
// copy of its fixtures instead of the repository's own artifacts, driving it
// as a real child process — the same shape `summarise-e2e-retries.test.mjs`
// already uses for its own script.
//
// ---------------------------------------------------------------------------
// Design (plan.md §7)
// ---------------------------------------------------------------------------
//
// A file is a trace-zip candidate when its first four bytes are the zip
// magic number (`PK\x03\x04`), **not** when its name ends in `.zip`
// (`spec.md` §1.4): the HTML reporter copies attachments into
// `playwright-report/data/` under two different naming schemes, and one of
// them carries no extension at all. A `*.json` file is handled as a
// Playwright JSON report instead (§7.5).
//
// Inside a trace zip, redaction runs in three layers, each catching what the
// one before it cannot:
//
//   1. **Structural** (§7.3) — `trace.network` (NDJSON HAR-shaped records)
//      and `trace.trace` (NDJSON actions/logs/DOM snapshots) are parsed and
//      rewritten by field name / structural position: request and response
//      headers, a token's own query parameter (checked in **both**
//      `request.url` and the separately-serialised `request.queryString[]` —
//      spec.md §1.3 S2's trap), form-encoded post-data fields, a `fill()`
//      action whose *selector* names a credential-shaped field, and a DOM
//      snapshot's `__playwright_value_` on any element typed `password`.
//
//      **Deliberately contextual, not a password list.** A `fill` action is
//      redacted because of what its *selector* targets (something that reads
//      as a password, secret or token field), never because its value
//      matches a literal credential this script would otherwise have to
//      hold. Once a sensitive value is found this way, the exact same value
//      is swept from the rest of that trace.trace entry (a `fill(...)` log
//      line repeats it as free text; a DOM snapshot may repeat it on a field
//      that isn't itself typed `password` — a client secret's input is
//      typically `type="text"`). That sweep uses the value just found, not a
//      credential written into this file.
//
//   2. **Pattern backstop** (§7.4) — after the structural pass, every entry's
//      raw bytes (including images and other binary resources) are checked
//      against a small set of shape-based patterns: a JWT, a
//      `"access_token": "..."`-shaped JSON field, a `key=value` form field
//      naming a known-sensitive key, and a bare `Bearer <token>` header
//      value. This is what catches `resources/*.json` (a token response
//      body) and `resources/*.dat` (a token request's form-encoded body) —
//      neither has a schema this script otherwise walks. Entries round-trip
//      through `latin1`, which is byte-preserving in Node, so a JPEG or WebM
//      passes through unchanged unless it genuinely contains one of these
//      byte sequences.
//
//   3. **The JSON report** (§7.5) — separately, `test-results/e2e-report.json`
//      (or any `*.json` this script finds) has its `stdout`, `stderr`, every
//      error's `message`/`stack`/`codeframe`, every step's `title`/`error`,
//      and every attachment's `body` run through the same pattern backstop.
//      These are the fields `scripts/summarise-e2e-retries.test.mjs:17-19`
//      already names as capable of carrying a token, plus `codeframe` and the
//      step fields a phase-6 security review added (below).
//
//   4. **Report-level value sweep** (phase-6 reopen, blocker 2a) — a bare
//      quoted literal in free text (`unexpected value "..."`, a step title's
//      `Fill "..." locator('...')`) matches none of the four shape-based
//      patterns above. Mirroring the trace.trace mechanism in reverse: once
//      Playwright's own call-log wording ties a value to a credential-shaped
//      *locator* within one test result (a `Fill "<value>" locator('<sel>')`
//      step title, or a `Locator: locator('<sel>')` line paired with an
//      `unexpected value "<value>"` line in the same error message), that
//      value is swept from every string field of that same result — the
//      report-shaped analogue of trace.trace's own selector-keyed sweep.
//
// The HTML reporter's `playwright-report/index.html` (phase-6 reopen,
// blocker 1) embeds its entire report data set as a base64-encoded zip inside
// a `<template id="playwrightReportBase64">` element. That embedded zip's
// entries are JSON files in the same report shape, so they get layers 3 and 4
// above, with the pattern backstop (layer 2) as a fallback for anything that
// doesn't parse as JSON.
//
// `error-context.md` (phase-6 reopen, blocker 2b) is Playwright's per-failure
// ARIA-snapshot dump. It has no distinct role for a password input — a
// `textbox`/`searchbox` line repeats the typed value verbatim — so its
// `# Page snapshot` section gets its own structural pass: a role line is
// redacted when its *label* reads as credential-shaped, mirroring
// `CREDENTIAL_SELECTOR_PATTERN` below, never because of what the value is.
//
// **Fail closed** (§7.6): an artifact this script cannot process — a corrupt
// or truncated zip, a write failure — is deleted rather than left for the
// upload, and the run ends non-zero. Finding zero artifacts to scrub is
// itself a failure (AS-7): a scrubber that silently stops matching (a
// Playwright upgrade, a renamed directory, an extension-filtering bug) must
// not report a comforting zero.
//
// **Over-redaction is the accepted bias** (plan.md §7.4): a trace with one
// extra `[REDACTED]` is a minor annoyance; a trace with one real token is the
// defect this whole script exists to prevent.

import { closeSync, openSync, readFileSync, readSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { unzipSync, zipSync } from 'fflate';

const DEFAULT_DIRECTORIES = ['test-results', 'playwright-report'];
const REDACTED = '[REDACTED]';

// ---------------------------------------------------------------------------
// Pattern backstop (§7.4) — shape-based, run over raw bytes / plain strings.
// ---------------------------------------------------------------------------

const JWT_PATTERN = /eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*/g;
const JSON_TOKEN_FIELD_PATTERN = /"(access_token|refresh_token|id_token|client_secret)"\s*:\s*"[^"]*"/g;
const FORM_TOKEN_FIELD_PATTERN = /(access_token|refresh_token|id_token|client_secret|password)=[^&\s"']*/g;
const BEARER_HEADER_PATTERN = /Bearer\s+[A-Za-z0-9._~+/-]{20,}/g;

function applyPatternBackstop(text) {
  return text
    .replace(JWT_PATTERN, '[REDACTED-JWT]')
    .replace(JSON_TOKEN_FIELD_PATTERN, '"$1":"[REDACTED]"')
    .replace(FORM_TOKEN_FIELD_PATTERN, '$1=[REDACTED]')
    .replace(BEARER_HEADER_PATTERN, 'Bearer [REDACTED]');
}

// Applied to a whole zip entry's bytes. `latin1` is a byte-preserving
// round-trip in Node, so this can run over every entry — text or binary —
// without a per-type allowlist and without corrupting an image or video.
function applyPatternBackstopToEntry(buffer) {
  return Buffer.from(applyPatternBackstop(buffer.toString('latin1')), 'latin1');
}

// ---------------------------------------------------------------------------
// Structural pass — trace.network (§7.3)
// ---------------------------------------------------------------------------

const SENSITIVE_HEADER_NAMES = new Set(['authorization', 'cookie', 'set-cookie', 'proxy-authorization', 'x-api-key']);
const SENSITIVE_QUERY_PARAMETERS = new Set(['access_token', 'id_token', 'refresh_token', 'code', 'token']);
const SENSITIVE_FORM_FIELDS = new Set(['client_secret', 'password', 'refresh_token', 'code']);
const SENSITIVE_URL_QUERY_PATTERN = /([?&])(access_token|id_token|refresh_token|code|token)=([^&]*)/gi;
const SENSITIVE_FORM_TEXT_PATTERN = /(client_secret|password|refresh_token|code)=([^&]*)/gi;

function redactHeaderList(headers) {
  if (!Array.isArray(headers)) return;
  for (const header of headers) {
    if (header && typeof header.name === 'string' && SENSITIVE_HEADER_NAMES.has(header.name.toLowerCase())) {
      header.value = REDACTED;
    }
  }
}

function redactNameValueList(list, sensitiveNames) {
  if (!Array.isArray(list)) return;
  for (const entry of list) {
    if (entry && typeof entry.name === 'string' && sensitiveNames.has(entry.name.toLowerCase())) {
      entry.value = REDACTED;
    }
  }
}

// spec.md §1.3 S2 / AS-3: a SignalR negotiate carries the same token twice in
// one record — once in `request.url`, once again, separately, in
// `request.queryString[]`. Both must be rewritten; rewriting only the URL
// leaves the value visible in the trace viewer's query-parameter table.
function redactNetworkRecord(record) {
  const request = record?.snapshot?.request;
  const response = record?.snapshot?.response;

  if (request) {
    redactHeaderList(request.headers);
    if (typeof request.url === 'string') {
      request.url = request.url.replace(SENSITIVE_URL_QUERY_PATTERN, (match, separator, key) => `${separator}${key}=${REDACTED}`);
    }
    redactNameValueList(request.queryString, SENSITIVE_QUERY_PARAMETERS);
    if (request.postData) {
      if (typeof request.postData.text === 'string' && request.postData.text.length > 0) {
        request.postData.text = request.postData.text.replace(SENSITIVE_FORM_TEXT_PATTERN, (match, key) => `${key}=${REDACTED}`);
      }
      redactNameValueList(request.postData.params, SENSITIVE_FORM_FIELDS);
    }
  }

  if (response) {
    redactHeaderList(response.headers);
  }
}

function redactTraceNetwork(buffer) {
  const lines = buffer.toString('utf8').split('\n');
  const rewritten = lines.map((line) => {
    if (line.trim().length === 0) return line;
    let record;
    try {
      record = JSON.parse(line);
    } catch {
      // Not a JSON line this pass understands — leave it for the pattern
      // backstop that runs over the whole entry afterwards.
      return line;
    }
    redactNetworkRecord(record);
    return JSON.stringify(record);
  });
  return Buffer.from(rewritten.join('\n'), 'utf8');
}

// ---------------------------------------------------------------------------
// Structural pass — trace.trace (§7.3)
// ---------------------------------------------------------------------------

// Matches a `fill()` target that reads as a credential field by name —
// `#password`, `[type="password"]`, `#client-secret`, `#access-token`, and
// so on — never a literal value. This is the mechanism, not a password list:
// the field is redacted because of what it is, not because of what was typed
// into it.
const CREDENTIAL_SELECTOR_PATTERN = /password|secret|token/i;
// A value has to be long enough to plausibly be a credential before it is
// swept from the rest of the entry as free text — otherwise a short,
// incidental value (an empty string, a single character before it was
// filled) would blow away unrelated matching substrings elsewhere in the
// trace.
const MINIMUM_SWEEPABLE_VALUE_LENGTH = 4;

function redactTraceRecord(record, sweepValues) {
  if (record && record.type === 'before' && record.class === 'Frame' && record.method === 'fill' && record.params) {
    const { selector, value } = record.params;
    if (
      typeof selector === 'string' &&
      CREDENTIAL_SELECTOR_PATTERN.test(selector) &&
      typeof value === 'string' &&
      value.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH
    ) {
      sweepValues.add(value);
      record.params.value = REDACTED;
    }
  }

  redactPasswordDomValues(record, sweepValues);
}

// A DOM snapshot represents an element as `["TAG", {attrs}, ...children]`.
// spec.md §1.3 S5 is the surface the issue never named: the browser masks a
// `type="password"` field on screen, but the snapshot records what was
// typed into `__playwright_value_` verbatim. Walked generically (not scoped
// to a known tree shape) because the snapshot format nests arbitrarily and a
// back-reference-diffed tree is not worth modelling just to find this one
// attribute.
function redactPasswordDomValues(node, sweepValues) {
  if (Array.isArray(node)) {
    for (const child of node) redactPasswordDomValues(child, sweepValues);
    return;
  }
  if (node && typeof node === 'object') {
    if (
      node.type === 'password' &&
      typeof node.__playwright_value_ === 'string' &&
      node.__playwright_value_.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH
    ) {
      sweepValues.add(node.__playwright_value_);
      node.__playwright_value_ = REDACTED;
    }
    for (const value of Object.values(node)) {
      redactPasswordDomValues(value, sweepValues);
    }
  }
}

// Walks a record's own string-typed values (recursively — mirrors
// `redactPasswordDomValues`'s generic tree walk) and sweeps a found value out
// of each one *before* the record is re-serialised. This is deliberately not
// a substring replace over the already-`JSON.stringify`'d line (phase-6
// reopen, should-fix): that ran over the whole serialised text without
// knowing which parts of it were inside a JSON string versus a bare
// (unquoted) numeric/boolean/null literal, so a swept value that ever
// collided with one of the latter could corrupt that line's JSON syntax.
// Touching only fields that are already JSON strings — never a number,
// boolean or null — makes that corruption impossible: whatever comes out the
// other side is still a string, and `JSON.stringify` re-escapes it correctly.
function sweepStringValues(node, sweepValues) {
  if (Array.isArray(node)) {
    for (let index = 0; index < node.length; index += 1) {
      const child = node[index];
      if (typeof child === 'string') {
        node[index] = sweepString(child, sweepValues);
      } else {
        sweepStringValues(child, sweepValues);
      }
    }
    return;
  }
  if (node && typeof node === 'object') {
    for (const key of Object.keys(node)) {
      const value = node[key];
      if (typeof value === 'string') {
        node[key] = sweepString(value, sweepValues);
      } else {
        sweepStringValues(value, sweepValues);
      }
    }
  }
}

function sweepString(text, sweepValues) {
  let result = text;
  for (const value of sweepValues) {
    result = result.split(value).join(REDACTED);
  }
  return result;
}

function redactTraceTrace(buffer) {
  const lines = buffer.toString('utf8').split('\n');
  const sweepValues = new Set();

  // Two passes, because a value found on one line (a fill() targeting a
  // credential-shaped selector, or a password-typed DOM value) is repeated
  // elsewhere in this same entry as free text — a
  // `{"type":"log",...,"message":"  fill(\"...\")"}` line, or a DOM snapshot
  // holding it under a field that isn't itself typed `password` (a client
  // secret's input is `type="text"`) — regardless of which line it appears
  // on relative to where it was first found. Pass 1 parses every line and
  // runs the structural redaction, accumulating `sweepValues`; pass 2 sweeps
  // the complete set out of every record's own string fields before
  // re-serialising.
  const parsedLines = lines.map((line) => {
    if (line.trim().length === 0) return { line, record: null };
    try {
      return { line, record: JSON.parse(line) };
    } catch {
      // Not a JSON line this pass understands — leave it for the pattern
      // backstop that runs over the whole entry afterwards.
      return { line, record: null };
    }
  });

  for (const { record } of parsedLines) {
    if (record !== null) redactTraceRecord(record, sweepValues);
  }

  const rewritten = parsedLines.map(({ line, record }) => {
    if (record === null) return line;
    sweepStringValues(record, sweepValues);
    return JSON.stringify(record);
  });

  return Buffer.from(rewritten.join('\n'), 'utf8');
}

// ---------------------------------------------------------------------------
// Zip handling
// ---------------------------------------------------------------------------

const ZIP_MAGIC = Buffer.from([0x50, 0x4b, 0x03, 0x04]);

function readMagicBytes(filePath) {
  const fd = openSync(filePath, 'r');
  try {
    const buffer = Buffer.alloc(4);
    const bytesRead = readSync(fd, buffer, 0, 4, 0);
    return buffer.subarray(0, bytesRead);
  } finally {
    closeSync(fd);
  }
}

function isZipFile(filePath) {
  try {
    const magic = readMagicBytes(filePath);
    return magic.length === 4 && magic.equals(ZIP_MAGIC);
  } catch {
    return false;
  }
}

// Entry names are preserved exactly (SC-003 / plan.md §7.3): `trace.network`
// references `resources/<sha1>` entries by name, and the trace viewer
// resolves by name, not by re-hashing content that redaction has changed.
function redactZipEntry(name, buffer) {
  let content = buffer;
  if (name === 'trace.network') {
    content = redactTraceNetwork(content);
  } else if (name === 'trace.trace') {
    content = redactTraceTrace(content);
  }
  return applyPatternBackstopToEntry(content);
}

function scrubZipFile(filePath) {
  try {
    const archive = unzipSync(readFileSync(filePath));
    const rewritten = {};
    for (const [name, data] of Object.entries(archive)) {
      rewritten[name] = redactZipEntry(name, Buffer.from(data));
    }
    writeFileSync(filePath, zipSync(rewritten));
    console.log(`scrub-playwright-artifacts: scrubbed ${filePath} (${Object.keys(rewritten).length} entries)`);
    return 'scrubbed';
  } catch (error) {
    deleteUnprocessable(filePath, error);
    return 'failed';
  }
}

// ---------------------------------------------------------------------------
// The JSON report (§7.5) and its report-level value sweep (phase-6 reopen,
// blocker 2a)
// ---------------------------------------------------------------------------

// Playwright's own call-log wording for a `fill()` step, when the HTML
// reporter's per-file report renders it as a step title: `Fill "<value>"
// locator('<selector>')`. Both the typed value and the selector it targeted
// are in the same string, so this is a direct structural analogue of
// `redactTraceRecord`'s `fill` handling above — reused here because the JSON
// report has no `fill()` action log of its own to key off.
const FILL_STEP_TITLE_PATTERN = /Fill "([^"]*)" locator\('([^']*)'\)/g;
// Playwright's own call-log wording when a locator assertion (e.g.
// `toHaveValue`) fails: the failing locator is named on one line, and the
// value actually found is repeated as a bare quoted literal a few lines
// later — never in the same regex match, so the two are captured separately
// and correlated by "same error message". `Received:` (`unexpected value`)
// and `Expected:` are the two sides of the same failure — round 3 reopen S3
// found a `toHaveValue` that asserts a credential and doesn't get it, which
// puts the credential on the `Expected:` side instead.
const LOCATOR_LINE_PATTERN = /Locator:\s*locator\('([^']*)'\)/;
const UNEXPECTED_VALUE_PATTERN = /unexpected value "([^"]*)"/g;
const EXPECTED_VALUE_PATTERN = /Expected:\s*"([^"]*)"/g;

// Playwright colours this wording with ANSI SGR escapes (`\x1b[32m`, …) in
// the JSON report — always adjacent to a token boundary, never splitting one
// — so matching is done against an escape-stripped copy. The *value itself*
// never contains an embedded escape, so the value strings this yields are
// swept out of the original (unstripped) text unchanged by
// `applySweepAndPatternBackstop`'s plain substring replace.
const ANSI_ESCAPE_PATTERN = /\x1b\[[0-9;]*m/g;

function stripAnsiCodes(text) {
  return text.replace(ANSI_ESCAPE_PATTERN, '');
}

// Round 3 reopen, B1: a plain source-code literal assignment
// (`const WALL_PASSWORD = 'Wall-munich-1234';`) has no `fill()`/locator
// pairing at all — Playwright echoes it into a codeframe or snippet whenever
// *any* test in the file fails within that codeframe mechanism's window,
// regardless of whether that test ever touches the constant. Matched by
// shape (a credential-shaped *identifier* being assigned), never by value,
// mirroring `CREDENTIAL_SELECTOR_PATTERN`'s existing selector-name
// convention.
//
// The middle `.*` (greedy) is deliberate, not merely permissive: this
// repository's own `e2e/wall-withdrawal.spec.ts:43` assigns through
// `process.env['SSE_KEYCLOAK_ADMIN_PASSWORD'] ?? 'dev-only-keycloak-admin'`
// — the real fallback literal sits after an env-var read and a `??`, not
// immediately after `=`. A lazy match would instead capture the env var's
// own *name* (`'SSE_KEYCLOAK_ADMIN_PASSWORD'`, itself just a quoted string
// earlier on the line) and miss the actual secret. Greedy backtracking
// finds the *last* quoted string before the statement ends, which is the
// value actually assigned. `.` does not cross a newline without the `s`
// flag, so this cannot reach into an unrelated later statement.
const SOURCE_LITERAL_ASSIGNMENT_PATTERN =
  /\b(?:const|let|var)\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*=.*['"]([^'"]*)['"][^'"]*?(?:[;,)\n]|$)/g;

function collectSourceLiteralSweepValues(text, sweepValues) {
  if (typeof text !== 'string') return;
  const plainText = stripAnsiCodes(text);
  for (const match of plainText.matchAll(SOURCE_LITERAL_ASSIGNMENT_PATTERN)) {
    const [, identifier, value] = match;
    if (CREDENTIAL_SELECTOR_PATTERN.test(identifier) && value.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH) {
      sweepValues.add(value);
    }
  }
}

// Scans one string field for either call-log shape above and adds any value
// tied to a credential-shaped selector to `sweepValues` — the same
// found-once-sweep-everywhere mechanism `redactTraceTrace` already uses,
// applied to report free text instead of a trace's own structured fill()
// records.
function collectReportSweepValues(text, sweepValues) {
  if (typeof text !== 'string') return;
  const plainText = stripAnsiCodes(text);

  for (const match of plainText.matchAll(FILL_STEP_TITLE_PATTERN)) {
    const [, value, selector] = match;
    if (CREDENTIAL_SELECTOR_PATTERN.test(selector) && value.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH) {
      sweepValues.add(value);
    }
  }

  const locatorMatch = plainText.match(LOCATOR_LINE_PATTERN);
  if (locatorMatch && CREDENTIAL_SELECTOR_PATTERN.test(locatorMatch[1])) {
    for (const match of plainText.matchAll(UNEXPECTED_VALUE_PATTERN)) {
      const value = match[1];
      if (value.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH) sweepValues.add(value);
    }
    for (const match of plainText.matchAll(EXPECTED_VALUE_PATTERN)) {
      const value = match[1];
      if (value.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH) sweepValues.add(value);
    }
  }
}

// Round 3 reopen, B1: the source-literal pairing above is deliberately NOT
// scoped to one test result — a codeframe's window is anchored to *its own*
// failure location, not to the constant's, so the same literal can surface
// in one test's small (2-above/3-below) `error.snippet` window and a
// completely different, otherwise-unrelated test's much larger (100-above/
// 100-below) `errors[].codeframe` window, in the same file. Collected once,
// over every string in the *whole* report document, and applied everywhere
// (`redactReportResult` unions this into each result's own sweep set) —
// the report-wide analogue of `redactTraceTrace`'s per-entry sweep.
function collectDocumentSourceLiteralValues(node, sweepValues) {
  const allStrings = [];
  collectStringValues(node, allStrings);
  for (const text of allStrings) collectSourceLiteralSweepValues(text, sweepValues);
}

function collectStringValues(node, out) {
  if (Array.isArray(node)) {
    for (const child of node) collectStringValues(child, out);
  } else if (node && typeof node === 'object') {
    for (const value of Object.values(node)) collectStringValues(value, out);
  } else if (typeof node === 'string') {
    out.push(node);
  }
}

function applySweepAndPatternBackstop(text, sweepValues) {
  let result = applyPatternBackstop(text);
  for (const value of sweepValues) {
    result = result.split(value).join(REDACTED);
  }
  return result;
}

// The entry point for a whole report document (a plain JSON report, or one
// per-test-file entry from the HTML report's embedded zip). Round 3 reopen,
// B1: a document-wide source-literal pass runs *before* the per-result walk,
// because that leak isn't scoped to any one test's own result (see
// `collectDocumentSourceLiteralValues`).
function redactReport(report) {
  const documentSweepValues = new Set();
  collectDocumentSourceLiteralValues(report, documentSweepValues);
  redactReportNode(report, documentSweepValues);
}

// Walks the whole report tree (`suites[].specs[].tests[].results[]...`,
// recursively) rather than hardcoding that path, so a nested `suites[]`
// (JSONReportSuite.suites) is covered the same way
// `scripts/summarise-e2e-retries.mjs` already recurses it. Also matches the
// HTML reporter's own per-file report shape (`tests[].results[].steps[]` /
// `.errors[]`), which nests differently but reaches the same field names.
function redactReportNode(node, documentSweepValues) {
  if (Array.isArray(node)) {
    for (const child of node) redactReportNode(child, documentSweepValues);
    return;
  }
  if (!node || typeof node !== 'object') return;

  const isResultNode =
    (Array.isArray(node.stdout) && node.stdout.length > 0) ||
    (Array.isArray(node.stderr) && node.stderr.length > 0) ||
    (node.error && typeof node.error === 'object') ||
    (Array.isArray(node.errors) && node.errors.length > 0) ||
    (Array.isArray(node.attachments) && node.attachments.length > 0) ||
    (Array.isArray(node.steps) && node.steps.length > 0);

  if (isResultNode) {
    redactReportResult(node, documentSweepValues);
  }

  for (const value of Object.values(node)) {
    redactReportNode(value, documentSweepValues);
  }
}

// One "result" (a single test attempt, in either report shape) is one sweep
// scope: every string field anywhere within it is scanned once for the
// call-log shapes above, then the values found — unioned with the
// document-wide source-literal values found by `redactReport` — are swept
// out of every string field in that same result. The call-log shapes stay
// scoped to "this result"; the source-literal shape is deliberately unioned
// in from the whole document (round 3 reopen, B1).
function redactReportResult(node, documentSweepValues) {
  const allStrings = [];
  collectStringValues(node, allStrings);
  const sweepValues = new Set(documentSweepValues);
  for (const text of allStrings) collectReportSweepValues(text, sweepValues);

  if (Array.isArray(node.stdout)) redactTextEntries(node.stdout, sweepValues);
  if (Array.isArray(node.stderr)) redactTextEntries(node.stderr, sweepValues);
  if (node.error && typeof node.error === 'object') redactReportError(node.error, sweepValues);
  if (Array.isArray(node.errors)) {
    for (const error of node.errors) redactReportError(error, sweepValues);
  }
  if (Array.isArray(node.attachments)) redactAttachments(node.attachments, sweepValues);
  if (Array.isArray(node.steps)) redactReportSteps(node.steps, sweepValues);
}

function redactTextEntries(entries, sweepValues) {
  for (const entry of entries) {
    if (entry && typeof entry.text === 'string') {
      entry.text = applySweepAndPatternBackstop(entry.text, sweepValues);
    }
  }
}

function redactReportError(error, sweepValues) {
  if (!error || typeof error !== 'object') return;
  if (typeof error.message === 'string') error.message = applySweepAndPatternBackstop(error.message, sweepValues);
  if (typeof error.stack === 'string') error.stack = applySweepAndPatternBackstop(error.stack, sweepValues);
  // `codeframe` (phase-6 reopen, blocker 1): the HTML reporter's per-file
  // report re-reads source lines around the failure into this field. A
  // literal credential passed directly to `.fill(...)` in a real e2e file
  // (rather than imported from a constant) lands here verbatim.
  if (typeof error.codeframe === 'string') error.codeframe = applySweepAndPatternBackstop(error.codeframe, sweepValues);
  // `snippet` (round 3 reopen, B1): the JSON report's own small
  // (2-above/3-below) `@babel/code-frame` window, carried on `result.error`
  // itself rather than on an `errors[]` entry — a different field from
  // `codeframe` above, which is the HTML report's much larger window.
  if (typeof error.snippet === 'string') error.snippet = applySweepAndPatternBackstop(error.snippet, sweepValues);
}

function redactAttachments(attachments, sweepValues) {
  for (const attachment of attachments) {
    if (attachment && typeof attachment.body === 'string') {
      attachment.body = applySweepAndPatternBackstop(attachment.body, sweepValues);
    }
  }
}

// The HTML reporter's per-file report also carries a `steps[]` tree (nested
// arbitrarily — "Before Hooks" contains its own sub-steps) with `title` and
// `error` string fields that repeat a fill()'d value as free text (phase-6
// reopen, blocker 1's `Fill "<value>" locator('<selector>')` step title).
function redactReportSteps(steps, sweepValues) {
  for (const step of steps) {
    if (!step || typeof step !== 'object') continue;
    if (typeof step.title === 'string') step.title = applySweepAndPatternBackstop(step.title, sweepValues);
    if (typeof step.error === 'string') step.error = applySweepAndPatternBackstop(step.error, sweepValues);
    if (typeof step.snippet === 'string') step.snippet = applySweepAndPatternBackstop(step.snippet, sweepValues);
    if (Array.isArray(step.steps)) redactReportSteps(step.steps, sweepValues);
  }
}

function scrubJsonReportFile(filePath) {
  try {
    const report = JSON.parse(readFileSync(filePath, 'utf8'));
    redactReport(report);
    writeFileSync(filePath, JSON.stringify(report, null, 2));
    console.log(`scrub-playwright-artifacts: scrubbed ${filePath}`);
    return 'scrubbed';
  } catch (error) {
    deleteUnprocessable(filePath, error);
    return 'failed';
  }
}

// ---------------------------------------------------------------------------
// The HTML report (phase-6 reopen, blocker 1)
// ---------------------------------------------------------------------------

// Playwright's HTML reporter (`_writeReportData` in its own runner) embeds
// its entire report data set — one JSON entry per test file, plus a
// `report.json` summary — as a base64-encoded zip inside `index.html`,
// wrapped in this template. Detected by content, not merely a `.html`
// extension, so a plain (non-report) HTML file is never treated as a
// candidate.
//
// Round 3 reopen, S1: detection and extraction are deliberately two
// different strictness levels. `HTML_REPORT_DETECTION_PATTERN` (below) is
// loose — it only asks "does this file mention `playwrightReportBase64`
// anywhere" — so a harmless drift in the exact wrapping (a quote-style
// change, extra whitespace) still gets the file *classified* as a report and
// routed through `scrubHtmlReportFile`. `HTML_REPORT_TEMPLATE_PATTERN`
// (extraction, used only inside `scrubHtmlReportFile`) stays strict: if the
// wrapping really has changed in a way this script can't parse, extraction
// fails and the file goes through the existing fail-closed
// delete-and-exit-nonzero path instead of being silently classified as "not
// a candidate" and uploaded untouched.
const HTML_REPORT_DETECTION_PATTERN = /playwrightReportBase64/;
const HTML_REPORT_TEMPLATE_PATTERN = /<template id="playwrightReportBase64">data:application\/zip;base64,([^<]+)<\/template>/;

function isHtmlReportFile(filePath) {
  if (path.extname(filePath).toLowerCase() !== '.html') return false;
  try {
    return HTML_REPORT_DETECTION_PATTERN.test(readFileSync(filePath, 'utf8'));
  } catch {
    return false;
  }
}

// The embedded zip's entries are JSON report data (the same shape
// `redactReport` already walks, extended above for `codeframe` and step
// text), so they get that same structural redaction; anything that doesn't
// parse as JSON falls back to the pattern backstop, matching how a trace
// zip's own non-`trace.network`/`trace.trace` entries are handled.
function redactHtmlReportZipEntry(name, buffer) {
  if (path.extname(name).toLowerCase() === '.json') {
    try {
      const parsed = JSON.parse(buffer.toString('utf8'));
      redactReport(parsed);
      return Buffer.from(JSON.stringify(parsed), 'utf8');
    } catch {
      // Not parseable JSON despite the extension — fall through rather than
      // fail the whole artifact over one unexpected entry.
    }
  }
  return applyPatternBackstopToEntry(buffer);
}

function scrubHtmlReportFile(filePath) {
  try {
    const html = readFileSync(filePath, 'utf8');
    const match = html.match(HTML_REPORT_TEMPLATE_PATTERN);
    if (!match) {
      throw new Error('expected a playwrightReportBase64 template with an embedded zip');
    }

    const archive = unzipSync(Buffer.from(match[1], 'base64'));
    const rewritten = {};
    for (const [name, data] of Object.entries(archive)) {
      rewritten[name] = redactHtmlReportZipEntry(name, Buffer.from(data));
    }
    const scrubbedBase64 = Buffer.from(zipSync(rewritten)).toString('base64');

    const rewrittenHtml =
      html.slice(0, match.index) +
      `<template id="playwrightReportBase64">data:application/zip;base64,${scrubbedBase64}</template>` +
      html.slice(match.index + match[0].length);

    writeFileSync(filePath, rewrittenHtml, 'utf8');
    console.log(
      `scrub-playwright-artifacts: scrubbed ${filePath} (${Object.keys(rewritten).length} embedded report-data entries)`,
    );
    return 'scrubbed';
  } catch (error) {
    deleteUnprocessable(filePath, error);
    return 'failed';
  }
}

// ---------------------------------------------------------------------------
// error-context.md (phase-6 reopen, blocker 2b)
// ---------------------------------------------------------------------------

// Playwright's ARIA-snapshot dump, written for every failing test. A role
// line looks like `- textbox "<label>" [ref=e3]: <value>` (optionally with
// more `[...]` annotations, e.g. `[active]`); there is no distinct role for a
// password input, so it falls back to the generic `textbox`/`searchbox` role
// and the typed value is written in verbatim.
const ARIA_ROLE_VALUE_LINE_PATTERN = /^(\s*-\s*(?:textbox|searchbox)\s+"([^"]*)"(?:\s*\[[^\]]*\])*\s*:\s*)(.*)$/;
// Round 3 reopen, S2: an unlabelled field (no `<label>`, no `aria-label`) gets
// no quoted accessible name at all — just the role, optional `[...]`
// annotations (`[active]`, `[ref=e2]`) and the value. There is nothing here
// to reason about (no label to test against `CREDENTIAL_SELECTOR_PATTERN`),
// so — per this feature's over-redaction bias (`plan.md` §7.4) — every such
// line is redacted regardless of what it is. This intentionally does not
// touch a *named* line: the required `\s*:\s*` immediately after the role
// (and any brackets) cannot follow a quoted label without also consuming its
// opening quote, so `ARIA_ROLE_VALUE_LINE_PATTERN` above and this pattern
// never both match the same line.
const ARIA_UNLABELLED_ROLE_VALUE_LINE_PATTERN = /^(\s*-\s*(?:textbox|searchbox)(?:\s*\[[^\]]*\])*\s*:\s*)(.*)$/;

// Redacted because of what the *label* says — matching this feature's
// existing `CREDENTIAL_SELECTOR_PATTERN` convention — never because of what
// the value is. A legitimate field (a username, a search box) is left alone.
// The value found this way is also added to `sweepValues`: `error-context.md`
// carries its own `# Test source` section, which is Playwright re-reading
// the spec file's surrounding lines the same way the HTML report's
// `errors[].codeframe` does (blocker 1) — a literal credential passed
// directly to `.fill(...)` in source lands there too, with no ARIA role of
// its own to key a redaction off. This sweeps the exact value the ARIA
// snapshot just identified out of the rest of the file, the same
// found-once-sweep-everywhere mechanism used throughout this script.
function redactAriaSnapshotSection(text, sweepValues) {
  const headingMatch = text.match(/^# Page snapshot\s*$/m);
  if (!headingMatch) return text;

  const sectionStart = headingMatch.index + headingMatch[0].length;
  const nextHeadingMatch = text.slice(sectionStart).match(/^# /m);
  const sectionEnd = nextHeadingMatch ? sectionStart + nextHeadingMatch.index : text.length;

  const redactedSection = text
    .slice(sectionStart, sectionEnd)
    .split('\n')
    .map((line) => {
      const match = line.match(ARIA_ROLE_VALUE_LINE_PATTERN);
      if (match) {
        const [, prefix, label, value] = match;
        if (!CREDENTIAL_SELECTOR_PATTERN.test(label)) return line;
        if (value.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH) sweepValues.add(value);
        return `${prefix}${REDACTED}`;
      }

      // Round 3 reopen, S2: no quoted name to test at all — redact
      // unconditionally (see the pattern's own comment above).
      const unlabelledMatch = line.match(ARIA_UNLABELLED_ROLE_VALUE_LINE_PATTERN);
      if (!unlabelledMatch) return line;
      const [, prefix, value] = unlabelledMatch;
      if (value.length === 0) return line;
      if (value.length >= MINIMUM_SWEEPABLE_VALUE_LENGTH) sweepValues.add(value);
      return `${prefix}${REDACTED}`;
    })
    .join('\n');

  return text.slice(0, sectionStart) + redactedSection + text.slice(sectionEnd);
}

function scrubMarkdownReportFile(filePath) {
  try {
    const text = readFileSync(filePath, 'utf8');
    const sweepValues = new Set();
    // Round 3 reopen, B1: scanned over the whole file (not just the "# Page
    // snapshot" section `redactAriaSnapshotSection` walks) because the leak
    // lives in "# Test source" instead — Playwright's own re-read of the
    // spec file's source around the failure, the same mechanism as the HTML
    // report's `errors[].codeframe`, just rendered as markdown here.
    collectSourceLiteralSweepValues(text, sweepValues);
    let redacted = applyPatternBackstop(redactAriaSnapshotSection(text, sweepValues));
    for (const value of sweepValues) {
      redacted = redacted.split(value).join(REDACTED);
    }
    writeFileSync(filePath, redacted, 'utf8');
    console.log(`scrub-playwright-artifacts: scrubbed ${filePath}`);
    return 'scrubbed';
  } catch (error) {
    deleteUnprocessable(filePath, error);
    return 'failed';
  }
}

// ---------------------------------------------------------------------------
// Fail-closed exit handling (§7.6)
// ---------------------------------------------------------------------------

function deleteUnprocessable(filePath, error) {
  console.error(`scrub-playwright-artifacts: deleting ${filePath} — could not scrub it safely (${error?.message ?? error})`);
  try {
    rmSync(filePath, { force: true });
  } catch (removeError) {
    console.error(`scrub-playwright-artifacts: also failed to delete ${filePath}: ${removeError.message ?? removeError}`);
  }
}

function collectFiles(rootDirectory) {
  let rootStat;
  try {
    rootStat = statSync(rootDirectory);
  } catch (error) {
    if (error.code === 'ENOENT') {
      console.error(`scrub-playwright-artifacts: ${rootDirectory} does not exist — nothing to scrub there`);
      return [];
    }
    throw error;
  }
  if (!rootStat.isDirectory()) {
    throw new Error(`${rootDirectory} is not a directory`);
  }

  const files = [];
  const pending = [rootDirectory];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        pending.push(fullPath);
      } else if (entry.isFile()) {
        files.push(fullPath);
      }
    }
  }
  return files;
}

// A candidate is detected by content (a zip's magic bytes) or by a `.json`
// extension (the report path) — never by a `.zip` extension (spec.md §1.4 /
// AS-4): the HTML reporter copies one of its two attachment spellings with
// no extension at all, and a filename filter would skip a real trace while
// reporting success. `.md` is detected by extension (phase-6 reopen, blocker
// 2b): Playwright always names this artifact `error-context.md`, so unlike
// the zip case there is no naming ambiguity to guard against. The HTML
// report is the exception in the other direction — detected by content, not
// merely a `.html` extension (`isHtmlReportFile`), so a plain HTML file is
// never treated as a candidate.
function classifyCandidate(filePath) {
  if (path.extname(filePath).toLowerCase() === '.json') return 'json';
  if (path.extname(filePath).toLowerCase() === '.md') return 'markdown';
  if (isZipFile(filePath)) return 'zip';
  if (isHtmlReportFile(filePath)) return 'html';
  return null;
}

function scrubCandidate(file, kind) {
  switch (kind) {
    case 'zip':
      return scrubZipFile(file);
    case 'html':
      return scrubHtmlReportFile(file);
    case 'markdown':
      return scrubMarkdownReportFile(file);
    default:
      return scrubJsonReportFile(file);
  }
}

function main() {
  const args = process.argv.slice(2);
  const directories = args.length > 0 ? args : DEFAULT_DIRECTORIES;

  let files;
  try {
    files = directories.flatMap((directory) => collectFiles(directory));
  } catch (error) {
    // A directory that exists but cannot be walked (e.g. a permissions
    // error) is a fatal failure: there is no way to know what it might have
    // hidden, so nothing from any of the target trees may reach the upload.
    console.error(`scrub-playwright-artifacts: fatal error walking ${directories.join(', ')}: ${error.stack ?? error}`);
    for (const directory of directories) {
      try {
        rmSync(directory, { recursive: true, force: true });
      } catch {
        // best effort — the fatal exit code is what actually blocks the upload
      }
    }
    process.exit(1);
  }

  const candidates = files
    .map((file) => ({ file, kind: classifyCandidate(file) }))
    .filter(({ kind }) => kind !== null);

  if (candidates.length === 0) {
    console.error(
      `scrub-playwright-artifacts: found 0 artifacts to scrub across ${directories.join(', ')} — ` +
        'refusing to report success over an empty set (AS-7)',
    );
    process.exit(1);
  }

  let failedCount = 0;
  for (const { file, kind } of candidates) {
    const outcome = scrubCandidate(file, kind);
    if (outcome === 'failed') failedCount += 1;
  }

  console.log(`scrub-playwright-artifacts: processed ${candidates.length} artifact(s), ${failedCount} failure(s)`);
  process.exit(failedCount > 0 ? 1 : 0);
}

main();
