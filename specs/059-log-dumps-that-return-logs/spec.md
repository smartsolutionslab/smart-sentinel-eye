# Feature Specification: A log dump returns logs

**Feature Branch**: `fix/2053-log-dumps-that-return-logs`

**Created**: 2026-09-03

**Status**: Draft — **scope grown 2026-09-03, see "The scope grew" below**

**Issues**: #2053 (original), **#2054 (added 2026-09-03)**

**Input (original, #2053)**: "Four services' diagnostic log dumps return a
placeholder instead of logs. `AspireFixture.RecentLogs(resourceName)` answers
with `(not tailed — add '<name>' to AspireFixture.TailedResources)` for any
resource absent from a hand-maintained four-element array, and seven call sites
ask for a resource that is not in it."

**Input (added, #2054)**: "Every log tail in `AspireFixture` subscribes to an
empty stream. `ResourceLoggerService.WatchAsync` is keyed by the DCP resource id
(`stream-distribution-jewwwgxq`), not the app-model name, and both capture paths
pass the name. Watching by name returns nothing and throws nothing."

## Context

`AspireFixture.RecentLogs` exists for one situation, and its own XML doc names
it: *"Use it when a test gets a status it cannot explain — a 500 tells you
nothing on its own, and CI has no other route to the service's stack trace."*

The method serves a resource only if that resource is being tailed, and the
tailed set is a hand-maintained literal (`AspireFixture.cs:30`):

```csharp
private static readonly string[] TailedResources = ["camera-catalog", "automation", "identity", "event-ingestion"];
```

A request for anything else returns a self-describing placeholder
(`AspireFixture.cs:420-423`). The placeholder is good writing, and that is
precisely why the gap survived: on the CI failure where it matters, the
assertion message reads like output rather than like an omission.

### Verified inventory (2026-09-03, re-counted from source, not inherited)

`RecentLogs` has **20 invocations**, all under `tests/`. Nineteen pass a string
literal; one (`RestartLosesNothingIntegrationTests.cs:154`) passes a variable
that is always `"event-ingestion"` — reached only from
`RestartAsync("event-ingestion")` at line 63 — so it is served.

**Seven literal call sites name a resource that is not tailed**, across **four
distinct resources**. This matches the issue exactly.

| Call site | Asks for | Tailed |
|---|---|---|
| `Identity/TokenAttributionIntegrationTests.cs:107` | `overlay-designer` | no |
| `LayoutComposition/LayoutFabScopingIntegrationTests.cs:328` | `layout-composition` | no |
| `StreamDistribution/StreamFabAttributionIntegrationTests.cs:314` | `stream-distribution` | no |
| `StreamDistribution/StreamFabDerivationIntegrationTests.cs:104` | `stream-distribution` | no |
| `StreamDistribution/StreamFabScopingIntegrationTests.cs:180` | `stream-distribution` | no |
| `StreamDistribution/StreamFabScopingIntegrationTests.cs:194` | `stream-distribution` | no |
| `SystemVariables/VariableFabResolutionIntegrationTests.cs:212` | `system-variables` | no |

The remaining twelve literal call sites name `automation` (4),
`event-ingestion` (5), `camera-catalog` (2, one of them inside the fixture at
`AspireFixture.cs:161`) and `identity` (1) — all served.

All four missing names are real Aspire resources declared in
`src/AppHost/AppHost.cs` (lines 266, 302, 336, 344), so tailing them cannot
silently subscribe to nothing. `audit-observability` is also untailed but has no
`RecentLogs` call site, so it is out of scope.

### Nothing is failing today

No assertion depends on `RecentLogs`. Every one of the seven sites builds an
**assertion message**. No test is wrong and nothing passes that should not. What
is lost is the diagnostic on the day it is needed — and the fab-scoping tests
are where that bites hardest, because a fab-resolution `403` is exactly a status
that tells you nothing on its own.

## The scope grew — 2026-09-03

**This section is the record of a spec that was wrong, and it is kept as one.**
Everything above was written before phase 5, and everything above is still true;
it was just not the whole reason a log dump returns no log.

Phases 1–4 shipped what this spec asked for: `TailedResources` went 4 → 8 and
`LogTailCoverageTests` went red → green. **Phase 5 then booted the real fixture
and the dump still carried no logs** — a *different* placeholder:

```
stream-distribution log:
(tail subscribed but the resource emitted nothing)
```

### The second mechanism, which this spec did not know about

`ResourceLoggerService.WatchAsync` is keyed by the **DCP resource id**
(`stream-distribution-jewwwgxq`), not by the app-model **resource name**
(`stream-distribution`). Both of the fixture's capture paths pass the name:

- `tests/Integration.Tests/Fixtures/AspireFixture.cs:476` — `loggers.WatchAsync(resourceName)`
  in `TailResourceLogsAsync`, which feeds `RecentLogs`
- `tests/Integration.Tests/Fixtures/AspireFixture.cs:411` — `loggers.WatchAsync(name)`
  in `CaptureOneResourceLogAsync`, which feeds the startup-timeout report

**Watching by name returns an empty stream and throws nothing.** That is why it
was invisible for as long as it was: the tail records no failure, so `RecentLogs`
answers *"(tail subscribed but the resource emitted nothing)"* — which reads like
a quiet service, not a broken subscription.

Direct probe, two separate boots (differing DCP suffixes, so not a one-off):

```
SCRATCH-DIRECT[stream-distribution]: ids=[stream-distribution-jewwwgxq] byName=(no logs captured) byId=60 line(s)
SCRATCH-DIRECT[camera-catalog]:      ids=[camera-catalog-thwaubpm]      byName=(no logs captured) byId=60 line(s)
SCRATCH-DIRECT[stream-distribution]: ids=[stream-distribution-duxwbenb] byName=(no logs captured) byId=60 line(s)
SCRATCH-DIRECT[camera-catalog]:      ids=[camera-catalog-wazgehsy]      byName=(no logs captured) byId=60 line(s)
```

**It is pre-existing on `develop` and affects all eight tails**, including the
four — `camera-catalog`, `automation`, `identity`, `event-ingestion` — that were
tailed long before this branch. So `RecentLogs`' own promise, *"CI has no other
route to the service's stack trace"*, has never once been kept.

### What a human decided

Grow #2053 to include #2054, rather than merge the narrow fix under a narrower
title. This spec now closes only when **a log dump returns logs**. Both issues
stay open until the one PR closes both.

### What that costs this spec, honestly

- **US-1 as written was not achievable by US-2's fix.** AS-1 asserted the
  returned string "is the service's recent console output"; the commit that made
  the guard green does not make AS-1 true. AS-1 is kept and is now served by
  US-3.
- **The commits already on the branch stay.** `1217c32` (the guard, observed
  red) and `5ff65d0` (the array) are correct and necessary — a resolved DCP id
  is no use for a resource nobody subscribes to. They are just not sufficient.
- **`LogTailCoverageTests` is green today while every tail is empty.** It is a
  true statement about the source that says nothing about delivery — a live
  instance of the failure this repo keeps recording: a guard that reads the
  design artefact instead of asking the running system. It is not deleted; it
  guards what it guards. It **cannot** be this scope's red test, and no
  source-scanning replacement can be either (see US-3 and AS-6).
- **Assumption A1 is not measured**, and two attempts to measure it measured
  something else. See A1 below.

## User Stories

### US-1 (P1) — An engineer reading a CI failure gets the service's logs

As an engineer reading a failed integration-test run in CI, when a test in
StreamDistribution, LayoutComposition, OverlayDesigner or SystemVariables fails
with an unexplained status, I want the assertion message to contain that
service's recent log lines, so that I can diagnose the failure from the run
output without reproducing it locally.

### US-2 (P1) — The mismatch cannot silently come back

As a maintainer, I want a build-time guard that fails when a `RecentLogs` call
site names a resource that is not tailed, so that the next call site added for
an untailed resource is caught by the build rather than discovered on the CI
failure where the log was needed.

US-2 is the same priority as US-1 deliberately. Fixing the four names without
the guard fixes today's instance of a defect whose defining property is that it
re-occurs invisibly; the repo already answers this class of problem with a
source-scanning guard (`HandlerDeconstructionTests`, `StaleCodeConventionTests`,
`GuardBanWiringTests`, `PrimitiveBoundaryTests`).

### US-3 (P1) — the tail actually delivers, and keeps delivering across a restart

**Added 2026-09-03 with the grown scope (#2054).**

As an engineer reading a failed integration-test run, when I read the log
section of an assertion message, I want it to contain the service's real log
lines rather than a placeholder — including after that service has been
restarted mid-run, which is precisely when the diagnostic is most likely to be
the only account of what happened.

US-3 is what makes US-1 true. US-1 names the *four resources* that could not be
asked; US-3 is that asking any of the eight returns an answer. They are separate
stories because they have separate defects, separate fixes and separate
evidence — collapsing them would hide that the first fix shipped and did not
deliver the feature.

## Acceptance Scenarios

Written against the guard and the fixture. **Auth and bad-request scenarios are
N/A** — this change has no HTTP surface, no caller, and no trust boundary; the
whole of it lives in the test harness. Saying so is more honest than
manufacturing a scenario.

**AS-1 — happy path (US-1)**

```gherkin
Given the Aspire fixture has started and every resource is running
When a test calls RecentLogs("stream-distribution")
Then the returned string is the service's recent console output
And it is not the "(not tailed — …)" placeholder
```

**AS-1 was not satisfied by the US-2 fix.** It removed the `(not tailed — …)`
line and left `(tail subscribed but the resource emitted nothing)` — which is
still not "the service's recent console output". AS-6 is the scenario that makes
AS-1 true, and states the distinction the original wording was too loose to
force.

**AS-2 — the guard names the violations (US-2, the red artifact)**

```gherkin
Given TailedResources holds only ["camera-catalog", "automation", "identity", "event-ingestion"]
When the log-tail coverage guard runs
Then it fails
And the failure names each of the 4 untailed resources with its call sites
```

**AS-3 — the guard passes once the names are added (US-2)**

```gherkin
Given TailedResources also holds "stream-distribution", "overlay-designer", "layout-composition" and "system-variables"
When the log-tail coverage guard runs
Then it passes
```

**AS-4 — conflict case: the guard must not go green by finding nothing (US-2)**

```gherkin
Given the guard's scan matches zero RecentLogs call sites
When the guard runs
Then it fails, reporting that it found no call sites
```

This is the failure mode every source-scanning guard in this repo has to defend
against and the reason `HandlerDeconstructionTests` carries a `checkedCount`. A
guard that silently matches nothing is indistinguishable from a guard that
passes.

**AS-5 — a non-literal argument is skipped, not failed (US-2)**

```gherkin
Given RestartLosesNothingIntegrationTests.cs:154 calls RecentLogs(resourceName) with a variable
When the guard runs
Then that call site is not reported as a violation
```

The guard checks literals. A variable argument cannot be checked by reading
source, and failing it would force a rewrite of correct code.

**AS-6 — the tail delivers a line the test itself caused (US-3, the red artifact)**

```gherkin
Given the Aspire fixture has started
And the test has invented a camera name no other test uses
When it registers that camera against camera-catalog over real HTTP
And it re-reads RecentLogs("camera-catalog") until the line arrives or 30 s elapse
Then the returned string contains that invented camera name
And it is not "(not tailed — …)", "(tail subscribed but the resource emitted nothing)" or "(log tail failed: …)"
```

**"Contains real log content" is defined by the positive half of that
assertion, not the negative half.** All three placeholders are non-empty
strings, so `ShouldNotBeNullOrEmpty` passes on every one of them — that is the
trap, and it is the assertion an unwary test would write. The load-bearing
assertion is that the tail contains **a token the test invented seconds earlier
and caused this specific service to log**. That single check rules out, at once:
the resource not being tailed; the subscription being empty; the tail having
faulted; content that is stale rather than live; and content that came from a
different resource's stream. The three negative assertions are kept anyway,
because they turn a failure into a diagnosis instead of a diff of two long
strings.

`camera-catalog` writes `Registered camera {CameraIdentifier} with name
{CameraName}.` at `Information`
(`src/CameraCatalog/Application/Log.cs`), and `Logging:LogLevel:Default` is
`Information`. **A request alone is not enough**: `Microsoft.AspNetCore` is
pinned to `Warning` in `appsettings.json`, so ASP.NET's own per-request lines are
never written — the marker has to be a line the application code emits
deliberately.

**AS-7 — every tailed resource delivers, not just the one that was driven (US-3)**

```gherkin
Given the Aspire fixture has started and every resource is running
When a test calls RecentLogs(name) for each of the eight names in TailedResources
Then no returned string is any of the three placeholders
```

The id is resolved **per resource**, so a resolution that works for
`camera-catalog` does not prove one for a name whose snapshot never appears
under that spelling. AS-7 is the cheap generalisation — no request, no extra
boot; AS-6 is the expensive proof of identity. Each covers what the other
cannot: AS-7 cannot tell whose logs it is reading, AS-6 reads only one.

**AS-8 — a restarted resource is still tailed (US-3)**

```gherkin
Given the fixture is running and event-ingestion has been tailed since startup
When event-ingestion is restarted
And the test registers a webhook integration with an invented name against the restarted service
Then RecentLogs("event-ingestion") contains that invented name
```

**This is the scenario the whole re-subscribe loop exists for**, and it is the
regression test for #2038. AS-6 and AS-7 both read a tail whose process has run
undisturbed since `StartAsync`, so a subscription that dies at the one event it
exists to survive satisfies both. Nothing else here covers a restart.

**It does not discriminate a resolve-once implementation, and this paragraph
used to claim it did.** The DCP instance id was *observed stable* across a full
restart on Aspire 13.5.3 (Windows) — one `event-ingestion-gxkpyqjx` through
`Running → Stopping → Finished → Starting → Running`, seen independently at
phase 5 and at phase 6 — so a resolve hoisted above the loop passes AS-6, AS-7
and AS-8 alike. Re-resolving every turn is still the right shape, because id
stability is a property of that DCP build and not a published contract; it is
defensive code **this spec does not exercise**, and a green suite is not
evidence that hoisting would fail. Linux is unverified.

**AS-9 — the guard from US-2 is not the evidence for US-3 (US-3)**

```gherkin
Given LogTailCoverageTests passes
When every tail in the fixture is subscribed to an empty stream
Then LogTailCoverageTests still passes
```

Stated as a scenario because it is a fact about today's `develop` and the reason
AS-6 must be a runtime observation. A source scan cannot fail here, so a source
scan cannot be the red test.

## Independent end-to-end test procedure

Executable by a human who did not write the change, without reading the diff.

1. `dotnet test tests/Architecture.Tests` on `develop` — the guard does not
   exist, so nothing to see. Apply the guard commit alone and re-run: it must
   **fail**, naming the four resources. Capture that output verbatim (ADR-0139).
2. Apply the fixture commit. Re-run `dotnet test tests/Architecture.Tests` — the
   guard passes, unmodified.
3. Boot the fixture and observe a real dump. In
   `tests/Integration.Tests/StreamDistribution/StreamFabScopingIntegrationTests.cs`,
   temporarily change one expected status so the assertion fails, run that test,
   and read the message: it must contain `stream-distribution` log lines, not the
   placeholder. Revert the temporary change.
4. Record the fixture's `InitializeAsync` wall-clock duration before and after
   the change (step 3's run against `develop` and against the branch). See the
   tailing-cost decision in `plan.md` — this is the measurement that turns
   "probably fine" into an observation. Per the standing note that a first run
   after machine churn looks like a regression, run each side twice and take the
   second. **Attempted 2026-09-03 and again after the id fix; neither attempt is
   a measurement of A1, which stays *not measured*. See A1.**

### Steps added with the grown scope

Steps 1–3 were executed at phase 5 and step 3 **failed**, which is how #2054 was
found. Re-run step 3 after the id fix; it is expected to pass this time. Then:

5. On `develop`, run the new delivery test alone
   (`dotnet test tests/Integration.Tests --filter LogTailDelivers`). It must
   **fail**, and the failure message must show the placeholder it got instead of
   the invented camera name. Capture that output verbatim (ADR-0139) — it is the
   red artifact, and it is a *runtime* one.
6. Apply the id-resolution commit and re-run the same filter unmodified: all
   three tests pass.
7. Read the restart test's own output: it must show the tail carrying a line
   written **after** the restart, not merely a non-empty tail.
8. Confirm `dotnet test tests/Architecture.Tests --filter LogTailCoverage` still
   passes, unmodified. The guard is untouched by this scope and must stay that
   way — if it needed changing, something in the fixture's shape moved that
   nobody asked to move.

## Locked tech choices

- Integration tests are Aspire-only; the fixture boots the production AppHost
  (**ADR-0103**, superseding the Testcontainers guidance in ADR-0033/0052/0068).
  This change works inside that decision and does not revisit it.
- Rules that matter fail the build, not the review (**ADR-0139**). The guard is
  an instance of that decision, not a new one.
- xUnit + Shouldly, sentence-style underscore test names (**ADR-0052/0053**).
- Guards that must read source or build configuration live in
  `tests/Architecture.Tests` and read files from disk, locating the repository
  root by walking up to `SmartSentinelEye.slnx` — the pattern used by
  `HandlerDeconstructionTests`, `StaleCodeConventionTests` and
  `GuardBanWiringTests`.

### One record that is stale, and is not this spec's to fix

**ADR-0083** ("Architecture.Tests Scope — Boundary Rules Only") says the project
is scoped to NetArchTest boundary rules and *not* convention linting. It has
never been superseded or amended, and `tests/Architecture.Tests` now holds
**20 test classes**, most of them convention and record guards. ADR-0083 itself
provided the escape hatch — *"Reconsider expansion if convention drift becomes
visible"* — and ADR-0139 later made build-failing rules the general answer
without citing ADR-0083 by number.

So the expansion happened and the record did not follow. **This spec adds a
21st guard on that established precedent and does not decide anything new** —
but the ADR-0083 discrepancy is exactly the kind of stale record this repository
has had to correct twice before, and it deserves its own issue rather than being
settled silently here.

## Latency budget impact (constitution §IV)

**N/A — the change is confined to the integration-test harness
(`tests/Integration.Tests/Fixtures/AspireFixture.cs`, plus a new test class
beside it) and a new guard in `tests/Architecture.Tests`. No production code path
is touched, so no leg of the event→overlay budget is affected.**

**Still N/A after the scope grew.** The id resolution lives in the fixture; the
grown scope adds test files and touches no `src/` project. It does, however,
change what a *future* latency measurement can be trusted on: a run whose
service logs were unreadable is a run whose anomalies could not be explained.

## Out of scope

- Tailing `audit-observability`, `api-gateway`, `scenario-simulator`,
  `migrations` or any container resource. None has a `RecentLogs` call site.
- Changing `RecentLogs`' signature, its 120-line default, or the 400-line queue
  bound.
- Reconciling ADR-0083 (see above).
- Deleting any of the seven call sites. The issue rules this out and it is
  right to: removing a diagnostic someone deliberately reached for is the
  opposite of the fix.

### Out of scope, added with the grown scope

- **Observing `CaptureOneResourceLogAsync` (`:411`) through a real startup
  timeout.** It carries the same defective call and gets the same fix, but
  provoking a genuine eight-minute startup timeout in a test is not something to
  build for this. It is covered **by construction** instead: both call sites are
  required to go through one shared resolver (`plan.md`, design decision 3), so
  the runtime test on `:476` exercises the code `:411` depends on. What stays
  unobserved is the five-second bounded read around it, and this spec says so
  rather than implying coverage it does not have.
- **Linux/CI observation.** The delivery tests will run in CI's `integration`
  job, but the phase-5 evidence and the phase-5 re-run are Windows. The DCP
  suffix scheme is not platform-specific; nobody has watched it on Linux.
- **Deleting or weakening `LogTailCoverageTests`.** It stays exactly as
  committed at `1217c32`.
- **Replaying a backlog on subscribe.** Whether Aspire hands a late subscriber
  earlier lines is not relied on and not investigated; every delivery assertion
  is about a line written *after* the subscription exists.

## Assumptions

- **A1.** `ResourceLoggerService.WatchAsync` is a subscription over log output
  the Aspire host already produces, so the marginal cost of a tail is a
  subscription plus a bounded queue, not extra log production. Reasoned from
  `AspireFixture.TailResourceLogsAsync` (`:437-489`).

  **Status: NOT MEASURED.** Two attempts, and neither of them measured A1.

  *First attempt (phase 5, before the id fix.)* `InitializeAsync` timed four
  times with 8 tails and twice with 4:

  | Tails | Runs (s) |
  |---|---|
  | 8 | 137.87, 166.60, 154.29, 135.95 |
  | 4 | 140.26, 143.00 |

  The 8-tail spread alone is 30.7 s — 22% of its own minimum — and both 4-tail
  figures sit inside it. **No signal at this resolution.** And it was not the
  comparison A1 is about: with every tail subscribed to an empty stream, this
  timed *eight idle loops against four*, not eight live subscriptions against
  four. Kept here because it is the record of work actually done, and it is
  labelled for what it is.

  *Second attempt (after the id fix.)* **Confounded, and its figures are
  deliberately not recorded.** The 4-tail side was timed before the fix, when
  the tails enqueued nothing; the 8-tail side after, when they carry real
  traffic. That is "4 versus 8" confounded with "0 delivering versus 8
  delivering", and the drift between boots on one side is the same order as the
  difference between the sides. A number written down here is a number someone
  will later quote as clearance, so none is.

  **What an honest measurement would take**, if it is ever wanted: roughly eight
  interleaved boots per side, alternating, against a bring-up of two to three
  minutes each — with both sides on the *same* build, after the fix. It was not
  done, and nothing in this feature depends on the answer.

- **A2 (added with the grown scope).** `ResourceNotificationService.TryGetCurrentState(name, out ResourceEvent)`
  resolves an app-model name to the current DCP instance's `ResourceId`. The
  Aspire 13.5.3 XML doc states the contract directly: *"A resource id can be
  either the unique id of the resource or the displayed resource name … The
  resource name can also be used … but it must be unique. If there are multiple
  resources with the same name, then this method will not return a match."*
  **No resource in `TailedResources` is replicated**, so every one of the eight
  resolves. Reasoned from the published contract plus the phase-5 probe, which
  read exactly one id per name; **not yet exercised through this API** — the
  probe read ids by watching `ResourceNotifications` directly.

- **A3 (added with the grown scope).** A log line the service writes reaches the
  fixture's queue with some delay — DCP is between them. The delivery tests
  therefore **poll to a timeout** rather than assert immediately; an immediate
  assertion would be flaky in exactly the direction that reads as "the fix does
  not work".

## Artifacts deliberately not produced

`research.md`, `data-model.md`, `contracts/`, `quickstart.md` and `checklists/`
are omitted. There is no data model, no contract, no new API surface and nothing
to research beyond what is recorded above. Producing them would be ceremony.
