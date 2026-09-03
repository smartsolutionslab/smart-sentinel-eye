# Tasks: A log dump returns logs

**Spec**: `specs/059-log-dumps-that-return-logs/spec.md`
**Plan**: `specs/059-log-dumps-that-return-logs/plan.md`
**Issues**: #2053, **#2054 (scope grown 2026-09-03)**
**Branch**: `fix/2053-log-dumps-that-return-logs`

## Status, 2026-09-03

T001–T003 ran. **T001 and T002 are done and committed** (`1217c32`, `5ff65d0`).
**T003 failed** — the dump returned a second placeholder, not logs — which is how
#2054 was found and why this file now carries T004–T006. See `spec.md`, "The
scope grew". The PR closes both issues.

## Phase-3 declarations (ADR-0144)

| Declaration | Value |
|---|---|
| Engineer for phase 4b / 5 | **`infra-engineer`** (reviewer: `infra-reviewer`) — **re-confirmed** |
| New ADR required? | **No** — **re-confirmed for the grown scope**, `plan.md`, "No new ADR is required" |
| Change colour (phase 4a) | **Behaviour-changing → red first** — **unchanged, and now for a stronger reason** |
| Latency leg (§IV) | **N/A** — test harness only, no production path |
| Security review needed? | **No** — no trust boundary, no auth, no secret |

**Engineer, re-confirmed.** The grown scope is Aspire hosting API
(`ResourceNotificationService`, `ResourceLoggerService`, DCP instance ids) and
fixture lifecycle — `infra-engineer`'s subject, not `backend-engineer`'s. It
touches no bounded context, no domain, no `src/` project at all. The runtime test
(T004) is a fixture-boot integration test rather than domain TDD, so it stays
with the same engineer rather than going to `test-writer`; but it is still
**phase 4a work and must be observed red before T005 exists**, and the T004/T005
file split below enforces that the way a two-agent split would.

**ADR, decided here rather than inherited.** No. The fix implements the Aspire
API's actual contract — watching by name never worked, so there is no rejected
alternative to record. It changes no boundary, no context, no dependency and no
deployment. The one genuinely general lesson — *the red test for a diagnostic
must be a runtime observation* — is already required by constitution §Testing and
ADR-0139; this is their sharpest instance, not an amendment to them. The separate
question worth an issue is whether the repo wants a standing convention that
failure-only diagnostics get exercised; `plan.md` says raise it, do not settle it
here.

**Security, re-confirmed.** T004 and T006 authenticate against Keycloak to drive
their requests, but they use the existing operator helpers and assert nothing
about auth. No new trust boundary, no secret, no scope.

**Colour, re-confirmed and now unambiguous.** The original justification was
about the guard. The grown scope has its own, stronger one: T004 fails on a real
boot today, against `develop`, and passes only after T005. That is
behaviour-changing with a red artifact that no source scan can produce.

**Why behaviour-changing.** The scope includes the guard (US-2), and the guard
is new behaviour: it does not exist, it fails today, and it passes after the
fix. Had the scope been the four strings alone, the honest colour would have
been behaviour-preserving with nothing to observe red — the scope choice drove
the colour, not the other way round. ADR-0144's tie-break (*"ambiguity resolves
to behaviour-changing"*) points the same way.

**This is not a refactor-plus-fix pair.** ADR-0144 forbids mixing the two.
Nothing here changes shape while preserving behaviour; both tasks move in the
same direction — T001 states the rule, T002 satisfies it.

## Dependencies

```
T001 (guard, RED)  ──▶  T002 (four names, GREEN)  ──▶  T003 (verify) ✗ failed
                                                            │
                                                            ▼
                                        T004 (runtime tests, RED)  ──▶  T005 (id resolution, GREEN)  ──▶  T006 (verify)
```

Strictly sequential. **Nothing is `[P]`** — every task exists to make the
previous one's artifact true, and T004 and T005 deliberately touch the same
concern from opposite sides. Marking any of them parallel would be false.

**T002 before T005 was not optional.** A resolved DCP id is useless for a
resource nobody subscribes to; the array had to grow before delivery could be
observed on the four resources #2053 named. The two commits already on the branch
are a prerequisite of the grown scope, not a detour from it.

The usual foundational blockers (Shared.Kernel, Shared.Contracts, AppHost,
Aspire resources) are still untouched, and no ADR-0109 contention file is
involved.

This feature gates no other branch.

---

## User Story US-2 — the mismatch cannot silently come back

### [T001] [US-2] Write the log-tail coverage guard and observe it red — **DONE (`1217c32`)**

**Agent:** `test-writer` (phase 4a). **May not touch `AspireFixture.cs`.**

**File:** `tests/Architecture.Tests/LogTailCoverageTests.cs` (new)

Add one guard class, following the shape and the XML-doc convention of
`HandlerDeconstructionTests` / `GuardBanWiringTests`: a `<summary>` that says
what is guarded, **why a test rather than a compile error**, and the concrete
incident that motivated it (issue #2053 — seven call sites returning a
placeholder where a stack trace was needed).

Requirements, from `plan.md` "Guard mechanics":

- Locate the repo root by walking up to `SmartSentinelEye.slnx`.
- Scan `tests/Integration.Tests/**/*.cs`, excluding `obj/` and `bin/`.
- Collect every `RecentLogs(` call whose first argument is a string literal;
  skip non-literal arguments (AS-5).
- Parse `TailedResources` from `tests/Integration.Tests/Fixtures/AspireFixture.cs`.
- Fail if the parsed set is empty, or if zero call sites were found (AS-4).
- Fail listing each violation as `<path>:<line> asks for '<resource>'`, with `/`
  separators, ending by naming `AspireFixture.TailedResources`.
- Sentence-style underscore test name (ADR-0053), Shouldly assertions
  (ADR-0052).

**Done when:** `dotnet test tests/Architecture.Tests --filter LogTailCoverage`
**fails**, and the failure names all four resources — `overlay-designer`,
`layout-composition`, `stream-distribution`, `system-variables` — across all
seven call sites. **Capture that output verbatim**; it is the transported
artifact for phase 4b and the quote for the PR body (ADR-0139, ADR-0144).

**A guard that arrives green is a phase-4a failure**, not a shortcut.

---

## User Story US-1 — an engineer reading a CI failure gets the service's logs

### [T002] [US-1] Tail the four missing resources — **DONE (`5ff65d0`)**

**Agent:** `infra-engineer` (phase 4b). **May not edit `LogTailCoverageTests.cs`.**

**File:** `tests/Integration.Tests/Fixtures/AspireFixture.cs`

Add `"stream-distribution"`, `"overlay-designer"`, `"layout-composition"` and
`"system-variables"` to `TailedResources` (`:30`). All four are declared in
`src/AppHost/AppHost.cs` (lines 266, 302, 336, 344).

Extend the array's existing `<summary>` with one sentence recording **why the
list is what it is** — a resource earns a tail by having a `RecentLogs` call
site, and `LogTailCoverageTests` enforces that. The current doc explains why
tailing exists but not why these names; without that sentence the next reader
faces the same "is this complete?" question that produced #2053.

Line length after the addition exceeds what one line comfortably holds — format
as a multi-line collection expression. It remains a collection expression, so
the `dotnet_style_prefer_collection_expression` rule (warning, fails Release) is
satisfied.

**Done when:** T001's captured failure now passes, the test file unmodified, and
`dotnet build -c Release` is clean.

---

### [T003] [US-1] [US-2] Observe the dump end to end — **RAN, FAILED, superseded by T006**

**Outcome.** The forced failure returned
`(tail subscribed but the resource emitted nothing)` instead of
`stream-distribution` log lines. Cause: #2054. The timing half ran and produced
no signal (spec A1). T003 is not re-run as written — T006 replaces it and
subsumes both halves.



**Agent:** `infra-engineer` or orchestrator (phase 5).

Execute `spec.md` "Independent end-to-end test procedure" steps 3 and 4:

- Force one `StreamFabScopingIntegrationTests` assertion to fail and confirm the
  message carries `stream-distribution` log lines rather than the placeholder.
  Revert the forced failure. **This is the only step that proves the feature**
  — T001 and T002 together prove the array and the call sites agree, which is
  not the same as proving a tail delivers.
- Record fixture `InitializeAsync` duration on `develop` and on the branch,
  second run of each (a first run after machine churn reads as a regression).
  Write both figures into the verification note whatever they say.

**Done when:** the verification note records the observed dump and both timing
figures.

---

## User Story US-3 — the tail actually delivers, and keeps delivering across a restart

### [T004] [US-3] Write the three runtime delivery tests and observe them red

**Agent:** `infra-engineer` (phase 4a). **May not touch `AspireFixture.cs`.**

**File:** `tests/Integration.Tests/Fixtures/LogTailDeliversIntegrationTests.cs` (new)

One class, `[Collection(AspireCollection.Name)]`, taking `AspireFixture` — the
assembly's shared boot, so the marginal cost is HTTP and polling, not 140 s.
Class `<summary>` records the incident: the diagnostic
`RecentLogs` promises has never delivered a line, and a green
`LogTailCoverageTests` says nothing about that (#2054).

**Test A — delivery.** Register a camera on `camera-catalog` over real HTTP with
a name the test invents (`Guid.CreateVersion7()`), reusing the
`CreateAuthenticatedClientAsync` + `POST /cameras` shape from
`CameraFabResolutionIntegrationTests`. Then poll `aspire.RecentLogs("camera-catalog")`
on a short interval to a 30 s timeout, and assert:

- it **contains the invented name** — the load-bearing assertion;
- it is not `(not tailed — …)`, not `(tail subscribed but the resource emitted nothing)`,
  and does not start with `(log tail failed:`.

On timeout, report the last value read, so the failure names the placeholder it
got. **Do not write `ShouldNotBeNullOrEmpty` anywhere in this file** — all three
placeholders satisfy it, which is the entire trap.

`camera-catalog` writes `Registered camera {CameraIdentifier} with name
{CameraName}.` at `Information`. Do **not** rely on ASP.NET request logging:
`Microsoft.AspNetCore` is `Warning` in `appsettings.json`. Do **not** assert on
startup lines: the tail keeps 400 and returns 120, so a chatty service evicts
them before a late test runs.

**Test B — breadth.** For each of the eight names in `TailedResources`, assert
`RecentLogs(name)` is none of the three placeholders. No request, no waiting
beyond the fixture's own readiness. Report **every** failing resource in one
message, not the first.

**Test C — the restart.** Restart `event-ingestion` using the hardened
`RestartAsync` shape from `RestartLosesNothingIntegrationTests` (restart, then
`StartCommand` in a `finally`, then wait for healthy — copied deliberately;
a bare restart that fails on CI takes eleven unrelated tests with it). Then
`POST /webhook-integrations?fabId=munich` with an invented name — the shape in
`WebhookRegistryFabScopingIntegrationTests` — and assert the tail contains it,
polled the same way. `event-ingestion` writes `Registered webhook integration
'{Name}' ({Identifier}).` at `Information`.

C is the only test that fails against an implementation that resolves the id
once. Its `<summary>` must say so, or someone will "simplify" it away.

**Done when:** on `develop`'s fixture behaviour,
`dotnet test tests/Integration.Tests --filter LogTailDelivers` **fails**, and the
failure message shows the placeholder rather than the invented token. **Capture
that output verbatim** (ADR-0139) — it is the red artifact for the PR body, and
this time it is a runtime one.

**A test that arrives green is a phase-4a failure.** So is one whose red comes
from a compile error or a missing helper rather than from the tail.

---

### [T005] [US-3] Resolve the DCP resource id and watch by it

**Agent:** `infra-engineer` (phase 4b). **May not edit `LogTailDeliversIntegrationTests.cs`.**

**File:** `tests/Integration.Tests/Fixtures/AspireFixture.cs`

Add one private resolver over `_app.ResourceNotifications.TryGetCurrentState(name, out ResourceEvent evt)`
returning `evt.ResourceId`, and use it at **both** capture sites:

- `:476` `TailResourceLogsAsync` — `loggers.WatchAsync(resourceName)` → the id
- `:411` `CaptureOneResourceLogAsync` — `loggers.WatchAsync(name)` → the id

**One resolver, used twice.** Two copies make the spec's claim that `:411` is
covered by construction false (`plan.md`, decision 3).

**Resolve inside the re-subscribe loop, every turn.** Not once before it. The id
changes on every restart as well as every boot; resolve-once re-subscribes to a
dead instance and the resource goes permanently quiet — #2038's symptom,
reintroduced by this fix. A comment at the loop must say why the resolve is
where it is, because its position is the whole correctness argument and looks
like something to hoist.

Handle "not resolvable yet" as a **wait, not a failure**: the tails start at
`:116`, before the `WaitForResourceAsync` calls, so a snapshot may not exist. Do
not write `_logTailFailures` for it — a queue that is merely still filling,
reported as broken, is the same misleading-diagnostic defect this whole spec is
about. Keep the existing 250 ms delay on the re-resolving path; it is what bounds
the spin when a stale id resolves just after a restart.

Keep `_logTails` keyed by **app-model name**. Callers ask by name at 20 sites and
must keep doing so; the id is internal to the subscription.

**Done when:** T004's captured failure passes, all three tests, the test file
unmodified; `LogTailCoverageTests` still passes unmodified; `dotnet build -c Release`
clean.

---

### [T006] [US-1] [US-2] [US-3] Observe the dump end to end, and re-take the cost reading

**Agent:** `infra-engineer` or orchestrator (phase 5). Replaces T003.

Execute `spec.md` "Independent end-to-end test procedure" steps 3 and 5–8:

- The forced `StreamFabScopingIntegrationTests` failure now carries
  `stream-distribution` log lines. Revert the forced failure. **This is still the
  only step that proves the feature to a human**; T004 and T005 prove it to CI.
- Quote test C's output showing a line written **after** the restart.
- Re-take the `InitializeAsync` timing, second run of each side, now that eight
  tails are actually carrying lines — the phase-5 figures measured idle loops and
  clear nothing (spec A1). Write the figures down whatever they say, including
  "still no signal".
- Record what was **not** observed: `CaptureOneResourceLogAsync` through a real
  startup timeout, and any of this on Linux/CI. Do not let one green run imply
  either.

**Done when:** the verification note records the observed dump, test C's
post-restart line, both timing figures, and the two gaps.

---

## Phase-3 gate

- [x] Tasks atomic and ordered
- [x] Every task names its file and its done condition
- [x] Colour declared for phase 4a, with the reason it follows from the scope
- [x] Engineer and reviewer declared
- [x] Red test named concretely: project, fixture, driving request, assertion,
  and how it tells real log content from each of the three placeholders (T004)
- [ ] Issue #2053 on Project #13 — **already there**, status In Progress
  (verified 2026-09-03 via `gh issue view 2053`); no per-task issues, per the
  convention in force since spec 028
- [x] **Issue #2054 on Project #13** — **already there**, status Todo (verified
  2026-09-03: `gh issue view 2054` reports `projects: Smart Sentinel Eye (Todo)`).
  It is now in this feature's scope and the PR closes it alongside #2053.
