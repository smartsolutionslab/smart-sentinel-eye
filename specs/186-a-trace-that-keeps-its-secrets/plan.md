# Plan 186 — A trace that keeps its secrets

**Spec:** `specs/186-a-trace-that-keeps-its-secrets/spec.md`
**Issue:** [#2287](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2287)

---

## 1. Bounded context and layers

**None.** This feature has no bounded context, no domain model, no aggregate,
no persistence and no HTTP surface. It lands entirely in the repository's
build-and-CI apparatus:

```
playwright.config.ts                                 (repo root — ADR-0108)
.github/workflows/ci.yml                             (the e2e job)
scripts/scrub-playwright-artifacts.mjs               (new)
scripts/scrub-playwright-artifacts.test.mjs          (new)
scripts/fixtures/trace-redaction/                    (new)
```

That is stated rather than skipped because the phase-2 template asks for
layers, and answering "Application/Infrastructure" here would be a fiction that
sends a reviewer looking for a handler that does not exist. §4 and §5 below
record the same for entities and messaging.

**Where it sits relative to `apps/`:** outside. ADR-0108 put `e2e/` and
`playwright.config.ts` at the repo root, *"**outside** the `apps/*` pnpm
workspace, so they are not pulled into the per-app `lint`/`typecheck`/`test`
runs"*. That is why this is infrastructure work and not frontend work, and it
is the grounding for the phase-4b agent choice in `tasks.md`.

---

## 2. The shape of the change

Three files change and three are added. In dependency order:

| # | File | Change | Story |
|---|---|---|---|
| 1 | `scripts/fixtures/trace-redaction/make-leaky-trace.mjs` | **new** — reproducible fixture generator | US1 (apparatus) |
| 2 | `scripts/fixtures/trace-redaction/leaky-trace.zip` | **new** — a real Chromium-produced trace, ~6-15 KB | US1 (apparatus) |
| 3 | `scripts/fixtures/trace-redaction/leaky-report.json` | **new** — a Playwright JSON report with planted sentinels | US1 (apparatus) |
| 4 | `scripts/scrub-playwright-artifacts.test.mjs` | **new** — the guard | US1 + US2 |
| 5 | `scripts/scrub-playwright-artifacts.mjs` | **new** — the scrubber | US1 |
| 6 | `playwright.config.ts` | `sources: false` | US1 |
| 7 | `.github/workflows/ci.yml` | one step + one `if:` on the upload | US1 |
| 8 | `package.json` / `pnpm-lock.yaml` | one devDependency | US1 |

---

## 3. The one genuinely open decision: how to read and rewrite a zip

`spec.md` §9 deferred this here deliberately. Node has no zip API — `node:zlib`
offers raw deflate but no archive format — and `pnpm-lock.yaml` contains no zip
library today (checked: no `fflate`, `yauzl`, `yazl`, `jszip`, `adm-zip`,
`unzipper` or `archiver`).

Four options were weighed.

**(a) Shell out to `unzip` / `zip` — rejected.** Present on `ubuntu-latest`,
but `zip` is **not** installed by default in Git Bash on Windows, and
`pnpm test:guards` runs on developer machines. A guard that is green on CI and
errors on a developer's machine trains people to ignore it.

**(b) Reuse `playwright-core`'s bundled zip — rejected.** It exists
(`playwright-core/lib/zipBundle`) but is private API behind a bundler, with no
type surface and no stability promise. Coupling a *security* guard to an
unversioned internal of the very library whose output format it is guarding
compounds the risk US2 exists to catch.

**(c) Hand-roll the zip format — rejected.** ~120 lines of end-of-central-
directory location, central-directory parsing and local-header skipping, plus
`zlib.inflateRawSync` / `deflateRawSync`. Doable, and it is more code to review
and more surface to get subtly wrong than the alternative — in a guard whose
failure mode is publishing a credential while reporting success.

**(d) One small, zero-transitive-dependency library — adopted.** `fflate`
(`unzipSync` / `zipSync`), added as an exact-pinned **devDependency**. It has
no dependencies of its own, so it adds exactly one node to the tree; it is
sync, which keeps the script a straight-line program; and it never ships to
production — nothing under `src/` or `apps/` imports it.

**The engineer resolves the exact version at implementation time** and pins it
without a range, matching the repository's style for everything except
`@playwright/test` and `@types/node`. This plan does not invent a version
number.

**This is the single item in the diff that warrants an explicit
`security-reviewer` sign-off beyond the redaction logic itself** — a new
dependency introduced by a security fix is worth one deliberate look
(`tasks.md`, phase 6).

---

## 4. Entities, value objects, invariants

**No entities and no value objects.** There is no domain model here, so
constitution §II's primitive ban does not apply — it scopes to domain models,
and this is a Node build script. `PrimitiveBoundaryTests` does not see these
files.

The **invariants** that do exist are properties of the scrubber, and they are
what the guard asserts:

- **I-1 — Nothing in, nothing out.** If the scrubber processed zero artifacts,
  that is a failure, not a success. (US2, AS-7)
- **I-2 — Fail closed.** Every exit path that is not "every artifact was
  successfully scrubbed" must leave no unscrubbed artifact where the upload can
  reach it. (AS-6, SC-005)
- **I-3 — Redaction preserves structure.** A scrubbed trace still unzips, its
  `trace.network` still parses line-by-line as JSON, and the report is still
  valid JSON. (SC-003, SC-004)
- **I-4 — Redaction is visible.** A removed value is replaced by a marker, not
  by an empty string. A reader must be able to tell "a token was here" from
  "there was no header".

---

## 5. Messaging (domain event → integration event)

**N/A.** No domain events, no integration events, no `Shared.Contracts`
change, no RabbitMQ queue, no Wolverine handler. Nothing crosses a bounded
context because no bounded context is involved.

---

## 6. Boundary rules

- **No cross-context project references** — trivially satisfied; no `.csproj`
  changes at all. NetArchTest sees nothing.
- **`Shared.Contracts` untouched.**
- **No production assembly changes.** `src/LayoutComposition/Api/Program.cs` is
  *read* to explain why a token appears in a URL (`spec.md` §1.1); it is not
  edited. The `?access_token=` query pattern is the documented Microsoft
  SignalR approach and is correct.
- **The new devDependency may not be imported from `apps/**` or `e2e/**`.** It
  exists for `scripts/` only. If a reviewer finds an import elsewhere, the
  scope has been misread.
- **`apps/*` workspace boundary respected** — nothing added to any app's
  `package.json`; the dependency goes in the workspace root, where
  `@playwright/test` already lives.

---

## 7. The scrubber's design

### 7.1 Interface

```
node scripts/scrub-playwright-artifacts.mjs [dir…]
```

Defaults to `test-results` and `playwright-report`, relative to the working
directory. Directories are arguments so the guard can point it at a temp copy
of the fixture rather than mutating the repository — the same shape as
`summarise-e2e-retries.mjs`, which the guard already drives as a real child
process.

Exit `0` only when at least one artifact was found **and** every one was
successfully scrubbed. Anything else is non-zero. Writes a one-line-per-file
summary to stdout and every deletion, with its reason, to stderr.

### 7.2 What it walks

Recursively, both trees. **A file is a candidate zip when its first four bytes
are `PK\x03\x04`, not when its name ends in `.zip`** — `spec.md` §1.4 shows the
HTML reporter writes one of its two attachment spellings with no extension at
all, so an extension filter skips a real trace and reports success. This is
AS-4 and it is a deliberate guard against the most likely way this fix gets
built wrong.

`e2e-report.json` (and any `*.json` at a tree's root) is handled by the JSON
path in §7.5.

### 7.3 Redaction inside a trace zip — structural pass

Read the zip; for each entry, apply the passes below; write a new zip with the
same entry names. **Entry names are preserved even though `resources/<sha1>`
is content-addressed and the content now differs** — `trace.network` references
resources by that name and the viewer resolves by name, not by re-hashing. A
renamed entry would break the trace; a stale hash does not.

**`trace.network`** — NDJSON, one JSON object per line. For each line, parse,
rewrite, re-serialise:

- `request.headers[]` and `response.headers[]` — replace the `value` of any
  entry whose `name` matches, case-insensitively: `authorization`, `cookie`,
  `set-cookie`, `proxy-authorization`, `x-api-key`.
- `request.url` — strip the value of any sensitive query parameter
  (`access_token`, `id_token`, `refresh_token`, `code`, `token`).
- **`request.queryString[]` — the same parameters again, separately.**
  `spec.md` §1.3 S2 shows the token is stored in *both* places in the same
  record; rewriting the URL alone leaves the value visible in the viewer's
  query table. AS-3 exists solely to make this failure mode a red test rather
  than a code-review catch.
- `request.postData.text` and any `params[]` — form-encoded secrets
  (`client_secret`, `password`, `refresh_token`, `code`).

**`trace.trace`** — NDJSON of actions, logs and DOM snapshots:

- action records with `"method":"fill"` — replace `params.value` when
  `params.selector` targets a password field (`#password`,
  `[type="password"]`, or a selector containing `password`).
- `{"type":"log", …}` records whose `message` contains the value just
  redacted — replace it there too. The value appears twice, once as a param and
  once as `fill("…")`.
- **DOM snapshots** — blank the `__playwright_value_` attribute on any element
  node whose attributes carry `"type":"password"`. This is the surface the
  issue does not mention (`spec.md` §1.3 S5): the browser masks the field on
  screen, the snapshot stores the typed characters.

**Why contextual and not a password list:** redacting by *where the value came
from* means the scrubber never has to hold `Operator1234`,
`Wall-munich-1234` or `dev-only-keycloak-admin` in its own source. Writing the
realm passwords into a second file in order to remove them from a first is the
kind of fix that creates the defect it closes.

### 7.4 Redaction inside a trace zip — pattern backstop

The structural pass knows the shapes we have seen. The backstop catches the
ones we have not, including response bodies in `resources/*`, which have no
schema to walk.

Over **every** entry, applied to the bytes:

| Pattern | Replaced with |
|---|---|
| `eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*` (a JWT) | `[REDACTED-JWT]` |
| `"(access_token\|refresh_token\|id_token\|client_secret)"\s*:\s*"[^"]*"` | `"$1":"[REDACTED]"` |
| `(access_token\|refresh_token\|id_token\|client_secret\|password)=[^&\s"']*` | `$1=[REDACTED]` |
| `Bearer\s+[A-Za-z0-9._~+/-]{20,}` | `Bearer [REDACTED]` |

**Binary safety:** entries are decoded and re-encoded as `latin1`, which is a
byte-preserving round trip in Node. A PNG or WebM passes through unchanged
unless it genuinely contains those byte sequences, so the backstop can run over
every entry without a per-type allowlist and without corrupting images.

**Over-redaction is the correct bias.** A trace with one extra `[REDACTED]` is
a minor annoyance; a trace with one real token is the defect.

### 7.5 `e2e-report.json`

Parse, walk the report tree, and redact the four fields spec 145 already
identified (`scripts/summarise-e2e-retries.test.mjs:17-19`): `stdout`,
`stderr`, each error's `message` and `stack`, and attachment `body`. Apply the
§7.4 patterns to each string value. Re-serialise.

The file must stay structurally valid — `scripts/summarise-e2e-retries.mjs`
reads it in the *preceding* CI step, but a reviewer or a re-run may read it
later, and SC-004 requires the summariser still parse it.

### 7.6 Fail-closed, concretely

- A per-file failure (corrupt zip, unreadable entry, write error) →
  **`fs.rmSync` that file**, log to stderr, continue, remember the failure.
- Any remembered failure at the end → exit non-zero.
- A fatal failure (a directory cannot be walked, the zip library throws at the
  top level) → **delete both trees** before exiting non-zero.
- Zero artifacts found → exit non-zero (I-1 / AS-7).

---

## 8. The CI wiring

Into `.github/workflows/ci.yml`'s `integration` job, **after** `Stop the
AppHost` (currently lines 501-504) and **before** `Upload Playwright report`
(currently 506-514):

```yaml
      - name: Scrub credentials from the Playwright artifacts
        id: scrub
        if: always()
        run: node scripts/scrub-playwright-artifacts.mjs
```

and the upload's condition becomes:

```yaml
      - name: Upload Playwright report
        if: always() && steps.scrub.outcome == 'success'
```

**Two independent guarantees, because one is not enough.**

1. The script deletes what it could not scrub (§7.6). This protects the case
   where the step runs, partially succeeds, and the upload still fires.
2. The upload is gated on the step's `outcome`. This protects the case where
   the script fails so early it cannot delete anything — a missing `node`, a
   missing module, a syntax error. `outcome` is `skipped` if the step never
   ran, which also fails the comparison, so a skipped scrub means a skipped
   upload.

**Why `always()` and not `failure()`:** traces are produced by *retried* runs,
and a run can be green overall having retried. `always()` also covers the
job-timeout cancellation that `ci.yml:485-490` records biting this exact job
before (#2137).

**Residual risk, stated:** a runner killed hard enough to skip the scrub step
also skips the upload step, so there is no window where artifacts publish
unscrubbed. A `continue-on-error` added to the scrub step later would silently
defeat guarantee 2; `tasks.md` names it as a thing the reviewer checks.

### 8.1 Where the guard runs

`scripts/scrub-playwright-artifacts.test.mjs` needs **no new CI wiring**.
`package.json:15` already declares:

```json
"test:guards": "node --test \"scripts/**/*.test.mjs\"",
"test": "pnpm -r --filter \"./apps/**\" test && pnpm test:guards",
```

and `ci.yml:171` runs `pnpm test` in the **`frontend`** job. So the guard runs
on every PR in a fast, Docker-free, browser-free job — not only in the
40-minute e2e job. That is SC-002, and it is the reason the fixture is a
committed artifact rather than something generated at test time (§9).

---

## 9. The fixture, and why it is a committed binary

The guard's subject is a binary produced by a browser. Three ways to get one:

**(a) Generate it in the test — rejected.** Requires a Chromium install. The
`frontend` job does not install Playwright browsers (only the `integration`
job does, at `ci.yml:453-454`), so the guard would either not run there or add
a browser download to the fast job.

**(b) Synthesise a zip in the test from hand-written JSON — rejected as the
*only* fixture.** It would be fast and dependency-free, and it would also be
the guard asserting against a shape the guard's own author invented. If the
synthetic shape drifts from Playwright's real one, the test stays green while
real traces leak. That is precisely the failure this repository has recorded
before: an assertion that cannot fail on its real subject.

**(c) Commit one real trace — adopted.** `leaky-trace.zip` is the output of an
actual headless Chromium run under actual Playwright 1.62.1 tracing. The probe
that produced it is **6,199 bytes** (`spec.md` §1.3), so the cost of committing
it is negligible, and every secret in it is a planted sentinel — no real
credential enters the repository.

`make-leaky-trace.mjs` is committed beside it so the fixture is reproducible
rather than a magic binary: a reviewer can regenerate it and diff the entry
list. It needs Chromium, so it is run **by hand on a version bump**, not in CI.
A comment at its head says so.

Synthetic cases are still used where a real one cannot easily be produced —
the extension-less copy (AS-4) and the corrupt file (AS-6) are made by copying
and truncating the real fixture, which keeps them anchored to real bytes.

---

## 10. Risks, and what mitigates each

| Risk | Mitigation |
|---|---|
| The scrubber misses a surface the probe did not model | `spec.md` §6.2 runs the same greps against a **real** trace from the real stack with real Keycloak tokens; SC-007 requires both halves. A-1 marks this as the assumption it is. |
| A Playwright upgrade changes the trace layout and the scrubber silently stops matching | US2 / AS-7: scrubbing zero files is a failure. Plus `make-leaky-trace.mjs` regenerates the fixture on a bump. |
| The zip rewrite produces an unopenable trace | SC-003; the guard re-unzips and re-parses, and phase 5 opens it in `playwright show-trace`. |
| Over-redaction hides something a debugger needed | Accepted bias (§7.4). I-4 keeps the marker visible so the reader knows a value was removed. |
| The new dependency is itself a supply-chain risk | Exact pin, devDependency only, zero transitive deps, never imported from `src/` or `apps/`. Explicit `security-reviewer` item. |
| The fix is bigger than the defect (ADR-0036) | Weighed in `spec.md` §2: the smaller change — deleting traces — defeats the artifact's purpose, and ADR-0033's gates depend on a 40-minute job being diagnosable from its artifacts. |
| A later PR adds `continue-on-error: true` to the scrub step | Named in `tasks.md` as a reviewer check; the script's own delete-on-failure survives it. |

---

## 11. What this plan does not decide

- **The exact `fflate` version.** §3 — resolved at implementation time and
  pinned then.
- **Whether the realm passwords in `e2e/**` should be rotated or moved to
  environment variables.** Out of scope (`spec.md` §3). `sources: false`
  removes them from the exported artifact, which is this issue's concern.
  Whether a follow-up issue is warranted is a phase-6 judgement.
- **Whether videos should be scrubbed or dropped.** Out of scope, marked as
  assumption A-2, and void if phase 5 sees a token painted on screen.
- **Anything about a future staging realm.** The issue's own framing is that
  this fix should land *before* that realm exists. It does not model it.
