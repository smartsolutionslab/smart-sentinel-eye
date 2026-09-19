# Spec 186 — A trace that keeps its secrets

**Issue:** [#2287](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2287)
— **already on Project #13** ("Smart Sentinel Eye"), status **Todo**, labels
`tech-debt`, `agent:ready`, no `agent:blocked`. Verified 2026-09-19 via
`gh issue view 2287 --json projectItems`, which reports the board membership
directly. **No `item-add` needed.**

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **185** as the
highest merged. `gh pr list --state open` returns **no open PRs at all**, so no
unmerged sibling claims 186. This spec is **186**. (The number one above
develop's highest is not automatically free — it has collided before — so it was
checked rather than assumed.)

**ADRs referenced:**

- **ADR-0108** (Playwright for browser e2e). The ADR this spec serves. It is
  the decision that makes the exposure real: *"Auth is the **real Keycloak
  login** as a seeded realm user, so the test covers OIDC redirect + token …
  not a faked token."* Real tokens are the point of the design, and a trace
  records everything the browser saw.
- **ADR-0033** (CI gates — maximum strictness on every code PR). The artifact
  upload exists to serve those gates; this spec keeps it serving them without
  exporting credentials.
- **ADR-0036** (smallest possible change; no speculative generality; surface
  assumptions).
- **ADR-0037** / **ADR-0144** (the phased workflow; the autonomous lane and its
  phase-4a colours).
- **ADR-0109** (the `[P]` disjoint-file rule).
- **ADR-0139** (rules that fail the build, not the review — and the red-first
  obligation).

**No new ADR is needed.** This spec decides nothing architectural. ADR-0108
already locked "real Keycloak login, real tokens"; this closes a hole in what
happens to those tokens **after** the run, inside CI's own artifact retention.
It does not amend the constitution, does not change the auth model, and does
not alter what any e2e test asserts.

**Latency budget (§IV): N/A.** The diff lands in `playwright.config.ts`,
`.github/workflows/ci.yml`, and `scripts/`. No production assembly changes, so
none of the six legs of `event arrival → overlay rendered` is touched and no
leg's measurement status moves.

---

## 1. The finding, re-checked on this tree

The issue was filed 2026-09-13 by the whole-project security review and is
explicit that its central claim was **inferred**: *"Config verified; trace
contents inferred from Playwright's documented behaviour, not from opening a
trace."*

**This spec opens a trace.** Everything in §1.3 below was produced by running a
real headless Chromium under real Playwright 1.62.1 tracing on this worktree
(`origin/develop` @ `e818acf8`) on 2026-09-19 and unzipping the result. The
inference was correct, and it was also **incomplete** — there are more leaking
surfaces than the issue names, and one of them the issue does not mention at
all.

### 1.1 Every cited line number has drifted but one

Checked file by file, because this repository's citations drift and a spec that
copies stale line numbers forward teaches the next reader to trust them.

| Issue citation | Actual on `e818acf8` | Verdict |
|---|---|---|
| `playwright.config.ts:15` — `retries: 2` on CI | line **15**, `retries: isCI ? 2 : 0` | **holds** |
| `playwright.config.ts:20-21` — `trace`, `video` | lines **23-24** | drifted |
| `.github/workflows/ci.yml:278-286` — artifact upload | lines **506-514** | drifted |
| `.github/workflows/ci.yml:271` — 300-line `apphost.log` tail | lines **494-499** | drifted |
| `src/LayoutComposition/Api/Program.cs:26-31` — `?access_token=` | lines **18-35**; the query read is line **30** | drifted |

**The substance of every citation holds.** Nothing has been fixed in the
meantime, and no earlier spec has touched this path. The drift is ordinary file
growth, not a stale premise.

The two surviving facts that make the exposure real, quoted exactly:

```ts
// playwright.config.ts:21-26
use: {
  baseURL: 'http://localhost:5173',
  trace: 'on-first-retry',
  video: 'retain-on-failure',
  ignoreHTTPSErrors: true,
},
```

```yaml
# .github/workflows/ci.yml:506-514
      - name: Upload Playwright report
        if: always()
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: playwright-report
          path: |
            playwright-report
            test-results
          retention-days: 14
```

### 1.2 Playwright 1.62.1 has no redaction API — checked, not assumed

`package.json:25` pins `"@playwright/test": "^1.62.1"`; `pnpm-lock.yaml:14-16`
resolves it to exactly **1.62.1**. The installed type surface was searched:

```
$ grep -niE "redact|sanitiz|scrub|maskSensitive|filterHeader" \
    node_modules/.pnpm/playwright@1.62.1/.../types/test.d.ts \
    node_modules/.pnpm/playwright-core@1.62.1/.../types/types.d.ts
(only "file-system-sanitized name" in unrelated output-path docs)
```

The complete `trace` option surface in 1.62.1 is one line
(`types/test.d.ts:7059`):

```ts
trace: TraceMode | /** deprecated */ 'retry-with-trace'
     | { mode: TraceMode, snapshots?: boolean, screenshots?: boolean,
         sources?: boolean, attachments?: boolean };
```

**There is no header filter, no URL exclusion, no body redaction and no
`maskSensitiveData` knob.** The issue's first suggested fix — *"`trace:
'retain-on-failure'` with a redacting `contextOptions`"* — **cannot be built as
written.** `contextOptions` carries browser-context settings (viewport, locale,
storage state); nothing in it touches what tracing records. This is the same
class of finding as ADR-0128 against ADR-014: the suggested mechanism does not
exist, so the spec names a different one rather than approximating it.

Two knobs in that line *do* bear on the problem, and §2 weighs them.

### 1.3 What a real trace actually contains — six surfaces, observed

A standalone probe (reproduced verbatim in §6.1) drove headless Chromium
against a throwaway local server that mimics the shapes this stack really
produces: a Keycloak token response, a bearer-authenticated API call, a SignalR
negotiate carrying `?access_token=`, and a Keycloak-style password form. Every
secret was a planted sentinel, so absence is provable and nothing real leaked
into this document.

The trace zip is **6,199 bytes** and unzips to nine entries:

```
resources/022d91f2….json   resources/1950dd42….json   resources/633cc158….dat
resources/65acf0a7….txt    resources/749f4bb8….html   resources/src@7c308544….txt
trace.network              trace.stacks               trace.trace
```

Grepping each sentinel across the unzipped tree:

| Sentinel | Found in |
|---|---|
| `SSE_FAKE_ACCESS_TOKEN_PAYLOAD` | `trace.network`, `resources/1950dd42….json`, `resources/src@….txt` |
| `SSE_FAKE_REFRESH_TOKEN_VALUE` | `resources/1950dd42….json`, `resources/src@….txt` |
| `SSE_FAKE_CLIENT_SECRET` | `trace.network`, `trace.trace`, `resources/633cc158….dat`, `resources/src@….txt` |
| `SSE_FAKE_PASSWORD_VALUE` (second probe) | `trace.trace`, `resources/src@….txt` |

Read out as the six distinct surfaces a fix must cover:

**S1 — `trace.network`, request headers.** NDJSON, one HAR-shaped
`resource-snapshot` per request:

```json
{"name":"Authorization","value":"Bearer eyJhbGciOiJSUzI1NiJ9.SSE_FAKE_ACCESS_TOKEN_PAYLOAD.sig"}
```

**S2 — `trace.network`, the URL *and, separately*, the parsed query.** The
SignalR negotiate appears twice in the same record:

```json
"url":"http://127.0.0.1:60286/hubs/layout/negotiate?access_token=eyJ…SSE_FAKE_ACCESS_TOKEN_PAYLOAD.sig"
…
"queryString":[{"name":"access_token","value":"eyJ…SSE_FAKE_ACCESS_TOKEN_PAYLOAD.sig"}]
```

**This is load-bearing for the fix.** A scrubber that rewrites `request.url`
and stops leaves the token intact in `queryString`, and the trace viewer shows
`queryString`. A single naive regex over one field is a fix that looks done and
is not.

**S3 — `resources/<sha1>.json`, the token endpoint's response body, verbatim:**

```json
{"access_token":"eyJhbGciOiJSUzI1NiJ9.SSE_FAKE_ACCESS_TOKEN_PAYLOAD.sig","refresh_token":"SSE_FAKE_REFRESH_TOKEN_VALUE","token_type":"Bearer"}
```

The **refresh** token appears here and nowhere else. It is the longest-lived
credential in the run and the one the issue is most right to care about.

**S4 — `resources/<sha1>.dat`, the request post body, verbatim:**

```
grant_type=password&client_id=sse-management&client_secret=SSE_FAKE_CLIENT_SECRET&username=u&password=p
```

**S5 — `trace.trace`, the action log.** Three sub-places, and the third is the
one the issue does not mention:

```json
{"class":"Frame","method":"fill","params":{"selector":"#password","value":"SSE_FAKE_PASSWORD_VALUE",…}}
{"type":"log","callId":"call@12","message":"  fill(\"SSE_FAKE_PASSWORD_VALUE\")"}
…["INPUT",{"__playwright_value_":"SSE_FAKE_PASSWORD_VALUE","id":"password","type":"password"…
```

That last one is a **DOM snapshot**: Playwright records the typed value of an
`<input type="password">` into the snapshot so the viewer can replay it. The
browser masks it on screen; the trace does not mask it on disk.

**S6 — `resources/src@<sha1>.txt`, the e2e source files themselves**, embedded
because `sources` defaults to on. That matters here because this repository's
e2e sources carry literal credentials:

```
e2e/support/sign-in.ts:15          .fill('Operator1234')
e2e/support/kiosk-session.ts:22    .fill('Operator1234')
e2e/kiosk-reconciliation.spec.ts:70 .fill('Operator1234')
e2e/wall-authority.spec.ts:16      const WALL_PASSWORD = 'Wall-munich-1234';
e2e/wall-withdrawal.spec.ts:43     ?? 'dev-only-keycloak-admin'
```

So S5 and S6 export the **seeded realm passwords** on every traced run,
independently of any token.

### 1.4 The upload reaches the traces by two paths, and one of them hides them

`ci.yml:511-513` uploads two trees. Both carry traces, and they name them
differently.

- **`test-results/`** — Playwright's output dir:
  `test-results/<project>-<test>-<retry>/trace.zip`, plus `video.webm`,
  failure screenshots, and `error-context.md`.
- **`playwright-report/data/`** — the HTML reporter **copies every attachment
  in**, verified in the installed reporter
  (`playwright/lib/runner/index.js`):

  ```js
  fs.mkdirSync(path.join(this._reportFolder, "data"), { recursive: true });
  fs.writeFileSync(path.join(this._reportFolder, "data", sha1), buffer);
  ```

  and the name is built two ways:

  ```js
  sha1 = calculateSha1(attachment.path + this._salt);        // no extension
  sha1 = calculateSha1(buffer) + path.extname(a.path);       // extension kept
  ```

  **One of those two spellings has no `.zip` suffix.** A scrubber that walks
  `playwright-report/` filtering on `*.zip` will therefore skip a real copy of
  a real trace and report success. Detection must be by content
  (`PK\x03\x04`), not by filename.

### 1.5 The JSON report is a seventh leak, and this repo already wrote that down

`playwright.config.ts:19` writes `test-results/e2e-report.json` on CI, and
`test-results` is uploaded whole. That file is not a trace and no scrubber
aimed at zips will touch it.

This is not a new theory. `scripts/summarise-e2e-retries.test.mjs:17-19`, from
spec 145 / #2077, already says it:

> FR-004 / the spec's *Auth* note: the summariser must never render `stdout`,
> `stderr`, an error message or an attachment body into the summary, because
> the e2e stack mints real Keycloak tokens and any of those fields can carry
> one. T007 asserts that with a sentinel string planted in all four.

Spec 145 protected the **rendered summary**. The **raw source of those same
four fields** is uploaded verbatim to the same artifact. The repository already
holds the judgement that these fields can carry a token; only the second
consumer was ever guarded.

### 1.6 What is *not* wrong, stated so it is not fixed by accident

- **`apphost.log`.** The issue cites the 300-line tail (`ci.yml:494-499`) and
  then reports its own negative result: the 546 KB working-tree log had **zero**
  matches for JWTs, `client_secret=` or `password=`. Re-checked in §6.2; still
  zero. It is a console tail, not a retained artifact, and ASP.NET does not log
  bearer tokens by default. **Out of scope** (§3).
- **Videos.** `video: 'retain-on-failure'` records the rendered page. The
  browser masks password fields on screen and tokens are never painted into the
  DOM by either app. Residual risk is real but second-order, and removing video
  would cost the debuggability the artifact exists for. **Out of scope**, named
  as a marked assumption in §8.
- **The realm is throwaway today.** The issue is right that the value is
  structural, not immediate. Nothing in this spec claims a live credential has
  leaked.

---

## 2. The mechanism, chosen against the issue's two suggestions

The issue offers two. One does not exist; the other is right but under-specified.
Four candidates were weighed.

**(A) `trace: { mode: …, snapshots: false }` — rejected.** The installed docs
say `snapshots` controls two things at once:

> If this option is true tracing will · capture DOM snapshot on every action ·
> **record network activity**

So it *would* close S1–S4 in one token. It would also delete the DOM snapshots
and the network tab — which is the entire reason a trace is worth uploading. A
fix that makes the artifact useless is indistinguishable from deleting the
artifact, and the debuggability it buys is what ADR-0033's gates depend on.

**(B) A redacting `contextOptions` — cannot be built.** §1.2. Not available in
1.62.1, and not a thing `contextOptions` does in any version.

**(C) `sources: false` — adopted, but only as one half.** One token in
`playwright.config.ts` removes S6 entirely: the embedded `src@*.txt` copies of
the e2e sources, and with them the literal `Operator1234` /
`Wall-munich-1234` / `dev-only-keycloak-admin`. The cost is near zero — the
sources are in the repository the reader already has. It does **not** touch
S1–S5.

**(D) A fail-closed scrubbing step before upload — adopted as the primary.**
The issue's second suggestion, made specific. A Node script in `scripts/`,
run from `ci.yml` **between** the e2e step and the upload step, that walks
`test-results/` and `playwright-report/`, rewrites every trace zip's
`trace.network`, `trace.trace` and `resources/*` entries, redacts
`e2e-report.json`, and **deletes any artifact it could not process**.

**Why a CI step and not a Playwright global teardown:** the exposure is the
upload, so the guard belongs adjacent to the upload where it is visible in the
job log and cannot be bypassed by running Playwright any other way. It also
keeps the scrubber a pure function of a directory — which is what makes it
testable without a browser (§5, SC-002).

**Why fail-closed is not optional.** The upload step is `if: always()`. If the
scrubber throws, dies, or is skipped, the *unscrubbed* artifacts upload anyway
and the job's only signal is a red step nobody reads before the artifact is
already published. So: on any per-file failure the script **deletes that file**
and continues; on a fatal failure it deletes both trees and exits non-zero.
**Losing a debugging artifact is the correct trade against publishing a
credential.**

**Adopted: (D) + (C).** Rejected explicitly: (A), (B).

**Also rejected: switching `on-first-retry` → `retain-on-failure`.** The issue
names it, but it is a *debuggability* change, not a security one — it makes
Playwright produce **more** traces (every failing test, not only first retries),
which on its own widens the exposure. Changing it here would mix two intents in
one diff (ADR-0036). `on-first-retry` stays.

---

## 3. Scope

**In scope**

- `playwright.config.ts` — `sources: false` on the trace option.
- `scripts/scrub-playwright-artifacts.mjs` — the scrubber.
- `scripts/scrub-playwright-artifacts.test.mjs` — its guard, run by the
  existing `pnpm test:guards` (`package.json:15`,
  `node --test "scripts/**/*.test.mjs"`), which `pnpm test` runs in CI's
  `frontend` job (`ci.yml:171`).
- `scripts/fixtures/trace-redaction/` — a **real** trace zip fixture and the
  committed generator that reproduces it.
- `.github/workflows/ci.yml` — one new step before the upload.
- `specs/186-a-trace-that-keeps-its-secrets/**`.

**Out of scope, each with its reason**

- **`apphost.log`** — §1.6; measured zero matches, and it is a console tail,
  not a retained artifact.
- **Videos and screenshots** — §1.6; second-order, and removing them costs the
  artifact's purpose. Marked as an assumption (§8, A-2).
- **Changing `trace` mode or `video` mode** — §2; a different intent.
- **Removing the literal passwords from `e2e/**`** — a real smell, but the
  seeded realm credentials are dev-only fixtures and rotating them is its own
  change against its own realm. `sources: false` removes them from the
  *exported artifact*, which is this issue's concern. Filing a follow-up is a
  phase-6 judgement, not this slice.
- **Any `src/` change** — `LayoutComposition/Api/Program.cs` is cited only to
  explain *why* a token appears in a URL. The `?access_token=` pattern is the
  documented Microsoft SignalR approach and is correct; it is not a defect and
  is not touched.
- **Secret scanning across other workflows or artifacts** — speculative
  generality (ADR-0036).

---

## 4. User stories

### US1 (P1) — A failing e2e run uploads no credentials

**As** anyone who can read this repository's Actions artifacts,
**I want** the Playwright artifacts published by a red e2e run to contain no
bearer token, refresh token, client secret or realm password,
**so that** the first non-throwaway realm an e2e job touches does not export
live credentials to everyone with read access for fourteen days.

**Independently shippable and observable end to end:** force an e2e failure,
run the scrubber, unzip the trace, grep for `access_token`. That is the issue's
own acceptance test, and it is US1's. Nothing else in this spec is needed for
it to be true.

### US2 (P2) — The scrubber cannot silently do nothing

**As** the next engineer to change Playwright's version or the CI upload,
**I want** the scrubber's guard to fail if the scrubber stops finding anything
to scrub,
**so that** a trace-format change, a renamed directory, or an
extension-filtering bug does not turn the guard into a green square that
asserts over an empty set.

This exists because this repository has been bitten by exactly that
(spec 185's whole subject), and because the guard's subject is a **binary
format owned by a third party** that will change shape without telling us.

---

## 5. Acceptance scenarios (Gherkin)

### AS-1 — the finding as filed (US1, the happy path)

```gherkin
Given a CI e2e run in which at least one test failed and was retried
  And Playwright wrote a trace zip under test-results/
  And the HTML reporter copied that trace into playwright-report/data/
When the scrub step runs before the upload step
Then no file under test-results/ or playwright-report/ contains a JWT,
     a refresh token, a client secret, or a realm password
 And the trace still opens in `playwright show-trace`
 And each redacted value is replaced by a visible marker, not deleted
```

### AS-2 — the positive control, which must fail before the fix (US1)

```gherkin
Given the committed leaky trace fixture, produced by a real Chromium run
When the fixture is unzipped and searched, before any scrubbing
Then every planted sentinel is present
 And each sentinel is found in the surfaces spec §1.3 names for it
```

**AS-2 is the guard on the guard.** Without it, a scrubber that deleted the
fixture, or a fixture that never carried a secret, would satisfy AS-1 while
proving nothing. An assertion must be able to fail on its own subject.

### AS-3 — the multi-field case that a naive fix misses (US1)

```gherkin
Given a trace.network record for a SignalR negotiate carrying ?access_token=
When the scrubber rewrites it
Then request.url carries no token
 And request.queryString's access_token entry carries no token
 And both were checked, because rewriting only the first leaves the second
```

### AS-4 — the extension-less copy (US1, the bad-input case)

```gherkin
Given playwright-report/data/ holds a trace copied under a name with no
      .zip suffix, as the HTML reporter writes it
When the scrubber walks the tree
Then that file is recognised as a zip by its PK magic bytes and scrubbed
 And a scrubber that filtered on "*.zip" would have skipped it
```

### AS-5 — the JSON report (US1)

```gherkin
Given test-results/e2e-report.json whose stdout, stderr, error message and
      attachment body each carry a token, as spec 145 recorded they can
When the scrubber runs
Then none of those four fields contains a token
 And the report's structure is still valid JSON that the summariser can read
```

### AS-6 — fail closed (US1, the conflict case)

```gherkin
Given a file under test-results/ that is corrupt, truncated, or a zip the
      scrubber cannot rewrite
When the scrubber processes it
Then that file is deleted rather than left for the upload
 And the script says on stdout which file it deleted and why
 And the step's exit code is non-zero so the job is red
```

```gherkin
Given the scrub step itself fails or is skipped
Then no unscrubbed artifact is uploaded
```

**The second half is the one to get right.** The upload step is `if: always()`,
so "the scrub step went red" does not by itself stop the upload. §7 SC-005
requires this be demonstrated, not assumed.

### AS-7 — the guard cannot pass over nothing (US2)

```gherkin
Given the scrubber is pointed at a directory containing no artifacts
When the guard runs
Then the guard fails, reporting that it scrubbed zero files
 And it does not report success
```

### AS-8 — auth/scope

**N/A to the product's auth model, and that is worth saying precisely.** This
change adds no endpoint, no scope, no fab check and no token validation. It
changes nothing about who may call what. Its entire subject is the *disposal*
of credentials the e2e run legitimately minted — a CI data-handling concern,
not an authorization one. `security-reviewer` is on the phase-6 list (tasks.md)
because the *asset* is credentials, not because the auth model moved.

---

## 6. Independent end-to-end test procedure

Two mechanisms, because either alone is a half-truth. The first proves the
scrubber works on a real Playwright artifact without needing the stack; the
second proves it works on **real Keycloak tokens**, which is the only thing that
finally answers the issue's "confirm by opening one trace".

### 6.1 The probe — reproducible without the stack

This is the script that produced §1.3, committed as the fixture generator. It
needs only an installed Chromium; no Aspire, no Keycloak, no database.

```js
// drives a throwaway local server that mimics the four real shapes:
//   POST /realms/sse/protocol/openid-connect/token  -> {access_token, refresh_token}
//   GET  /api/layouts            with Authorization: Bearer <token>
//   GET  /hubs/layout/negotiate?access_token=<token>
//   a Keycloak-shaped login form, filled via page.locator('#password').fill(...)
await ctx.tracing.start({ screenshots: true, snapshots: true, sources: true });
…
await ctx.tracing.stop({ path: 'leaky-trace.zip' });
```

Then:

```sh
unzip -o -q leaky-trace.zip -d x
grep -rl SSE_FAKE_ACCESS_TOKEN_PAYLOAD x/   # must hit trace.network + resources/*.json
grep -rl SSE_FAKE_REFRESH_TOKEN_VALUE x/    # must hit resources/*.json
grep -rl SSE_FAKE_CLIENT_SECRET x/          # must hit trace.network + trace.trace + *.dat
grep -rl SSE_FAKE_PASSWORD_VALUE x/         # must hit trace.trace (params, log, DOM snapshot)
```

**Every one of those four greps must hit before the fix** — that is AS-2 — and
**none may hit after the scrubber runs.**

### 6.2 The live counterfactual — real tokens, the issue's own test

This is phase 5's verification note and it is not optional. It is the step that
converts "inferred" into "observed" against credentials that actually exist.

1. Boot the stack: `dotnet run --project src/AppHost/…` and wait for
   `scripts/wait-for-e2e-stack.sh`.
2. **Deliberately fail one test that signs in.** Temporarily break an assertion
   in a spec that calls `signInAsOperator` (e.g. `e2e/layouts.spec.ts`), run
   `CI=true pnpm test:e2e --project=chromium -g "<that test>"` so `retries: 2`
   and `trace: 'on-first-retry'` both engage. **Revert the broken assertion
   afterwards** — it is a probe, not a change.
3. **Before scrubbing**, unzip the produced `test-results/**/trace.zip` and
   confirm the defect on real data:
   ```sh
   grep -c "access_token" x/trace.network      # expect > 0
   grep -o '"refresh_token":"[^"]\{0,12\}' x/resources/*.json   # expect a real prefix
   grep -c "Operator1234" x/trace.trace        # expect > 0
   ```
   Quote the counts. **This is the observation the issue asked for and never
   had.** Do not quote a whole token into the note — the prefix and the count
   carry the finding.
4. Run `node scripts/scrub-playwright-artifacts.mjs`.
5. Re-unzip and re-run the same three greps. **All must be zero.**
6. Confirm the trace still opens: `pnpm exec playwright show-trace <path>`, and
   that the network tab lists the requests with redaction markers in place of
   values.
7. Re-check `apphost.log` for the negative control (§1.6):
   ```sh
   grep -cE "eyJ[A-Za-z0-9_-]{10,}|client_secret=|password=" apphost.log   # expect 0
   ```
8. Record every figure in `verification.md`. A figure reported only to the
   orchestrator is invisible to every later grep and reviewer.

---

## 7. Success criteria

- **SC-001 —** Given the committed leaky fixture, the scrubber's output
  contains **zero** occurrences of each of the four sentinels, across all nine
  zip entries. (AS-1, AS-2)
- **SC-002 —** The guard runs under `pnpm test:guards` with **no browser
  installed**, in under five seconds, as the existing `scripts/*.test.mjs`
  do. It therefore runs in CI's `frontend` job, not only the 40-minute e2e job.
- **SC-003 —** The scrubbed trace still opens in `playwright show-trace`, and
  its network list still shows every request. A scrubber that produces an
  unreadable zip has replaced one defect with another.
- **SC-004 —** `test-results/e2e-report.json`'s four token-bearing fields are
  redacted and the file is still valid JSON that
  `scripts/summarise-e2e-retries.mjs` parses without error. (AS-5)
- **SC-005 —** With the scrubber made to fail deliberately, **no unscrubbed
  artifact reaches the upload.** Demonstrated, not argued. (AS-6)
- **SC-006 —** The guard fails when it scrubs zero files. (AS-7)
- **SC-007 —** On the live counterfactual (§6.2), the three "before" greps are
  non-zero and the three "after" greps are zero, with the figures quoted in
  `verification.md`.
- **SC-008 —** `resources/src@*.txt` is absent from traces produced after the
  `sources: false` change, so the literal realm passwords in `e2e/**` are not
  exported. (§1.3 S6)

---

## 8. Assumptions, marked

- **A-1 — The sentinel probe's shapes match the real stack's.** The probe
  mimics Keycloak's token response, a bearer API call, a SignalR negotiate and
  a password fill. It is a *model* of the real traffic. **Mitigated, not
  assumed away:** §6.2 runs the same greps against a real trace from the real
  stack, and SC-007 requires both halves. If the live run finds a surface the
  probe did not, the fixture is regenerated and the guard extended — that is a
  finding, not a failure.
- **A-2 — Video and screenshots do not carry credentials.** Based on: the
  browser masks `input[type=password]`, and neither app renders a token into
  the DOM. **Not verified by opening a video.** If phase 5 sees a token painted
  on screen, this assumption is void and the scope widens.
- **A-3 — Redaction in place is preferable to deletion.** A trace whose token
  values are replaced by `[REDACTED]` keeps its debugging value; a deleted
  trace has none. The assumption is that no debugging workflow needs the token
  *value*. If one does, it needs the live stack anyway, not a 14-day-old
  artifact.
- **A-4 — The trace zip's internal layout is stable for 1.62.1.** Observed
  directly on the pinned version, not read from documentation. US2 exists
  precisely because this will not stay true across upgrades.
- **A-5 — Artifacts already uploaded are out of scope.** Fourteen-day retention
  means existing artifacts expire on their own. Against a throwaway dev realm
  (§1.6) the issue itself judges this acceptable. If a reviewer disagrees, the
  remedy is deleting old artifacts, which is an operations task, not a code
  change.

---

## 9. Locked tech choices (nothing new)

| Concern | Choice | Source |
|---|---|---|
| Browser e2e | Playwright `@playwright/test` 1.62.1 | ADR-0108 |
| CI script language | Plain Node ESM `.mjs` in `scripts/` | existing: `summarise-e2e-retries.mjs`, `wait-for-e2e-stack.test.mjs` |
| Script test runner | `node --test` via `pnpm test:guards` | `package.json:15` |
| Script test style | sentinel planting + real child-process invocation | existing: `summarise-e2e-retries.test.mjs` |
| Fixture location | `scripts/fixtures/<topic>/` | existing: `scripts/fixtures/settle-rule/` |
| CI gates | maximum strictness, all blocking | ADR-0033 |

**One dependency question is deliberately left to the plan, not decided here:**
Node has no built-in zip reader, and the repository has no zip library in
`pnpm-lock.yaml`. `plan.md` §3 decides how the scrubber reads and rewrites a
zip. It is an implementation mechanism, not a product decision, and naming it
in the spec would be deciding it in the wrong artifact.
