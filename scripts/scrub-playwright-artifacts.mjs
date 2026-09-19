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
//      error's `message`/`stack`, and every attachment's `body` run through
//      the same pattern backstop. These are the four fields
//      `scripts/summarise-e2e-retries.test.mjs:17-19` already names as
//      capable of carrying a token.
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

function redactTraceTrace(buffer) {
  const lines = buffer.toString('utf8').split('\n');
  const sweepValues = new Set();

  const rewritten = lines.map((line) => {
    if (line.trim().length === 0) return line;
    let record;
    try {
      record = JSON.parse(line);
    } catch {
      return line;
    }
    redactTraceRecord(record, sweepValues);
    return JSON.stringify(record);
  });

  let text = rewritten.join('\n');

  // A value found above (a fill() targeting a credential-shaped selector, or
  // a password-typed DOM value) is repeated elsewhere in this same entry as
  // free text: a `{"type":"log",...,"message":"  fill(\"...\")"}` line, and
  // a DOM snapshot can hold it under a field that isn't itself typed
  // `password` (a client secret's input is `type="text"`). This sweeps the
  // *value just found*, not a credential this script had to already know.
  for (const value of sweepValues) {
    text = text.split(value).join(REDACTED);
  }

  return Buffer.from(text, 'utf8');
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
// The JSON report (§7.5)
// ---------------------------------------------------------------------------

// Walks the whole report tree (`suites[].specs[].tests[].results[]...`,
// recursively) rather than hardcoding that path, so a nested `suites[]`
// (JSONReportSuite.suites) is covered the same way
// `scripts/summarise-e2e-retries.mjs` already recurses it.
function redactReportNode(node) {
  if (Array.isArray(node)) {
    for (const child of node) redactReportNode(child);
    return;
  }
  if (!node || typeof node !== 'object') return;

  if (Array.isArray(node.stdout)) redactTextEntries(node.stdout);
  if (Array.isArray(node.stderr)) redactTextEntries(node.stderr);
  if (node.error && typeof node.error === 'object') redactReportError(node.error);
  if (Array.isArray(node.errors)) {
    for (const error of node.errors) redactReportError(error);
  }
  if (Array.isArray(node.attachments)) redactAttachments(node.attachments);

  for (const value of Object.values(node)) {
    redactReportNode(value);
  }
}

function redactTextEntries(entries) {
  for (const entry of entries) {
    if (entry && typeof entry.text === 'string') {
      entry.text = applyPatternBackstop(entry.text);
    }
  }
}

function redactReportError(error) {
  if (!error || typeof error !== 'object') return;
  if (typeof error.message === 'string') error.message = applyPatternBackstop(error.message);
  if (typeof error.stack === 'string') error.stack = applyPatternBackstop(error.stack);
}

function redactAttachments(attachments) {
  for (const attachment of attachments) {
    if (attachment && typeof attachment.body === 'string') {
      attachment.body = applyPatternBackstop(attachment.body);
    }
  }
}

function scrubJsonReportFile(filePath) {
  try {
    const report = JSON.parse(readFileSync(filePath, 'utf8'));
    redactReportNode(report);
    writeFileSync(filePath, JSON.stringify(report, null, 2));
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
// reporting success.
function classifyCandidate(filePath) {
  if (path.extname(filePath).toLowerCase() === '.json') return 'json';
  if (isZipFile(filePath)) return 'zip';
  return null;
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
    const outcome = kind === 'zip' ? scrubZipFile(file) : scrubJsonReportFile(file);
    if (outcome === 'failed') failedCount += 1;
  }

  console.log(`scrub-playwright-artifacts: processed ${candidates.length} artifact(s), ${failedCount} failure(s)`);
  process.exit(failedCount > 0 ? 1 : 0);
}

main();
