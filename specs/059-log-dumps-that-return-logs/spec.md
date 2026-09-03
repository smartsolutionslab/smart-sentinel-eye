# Feature Specification: A log dump returns logs

**Feature Branch**: `fix/2053-log-dumps-that-return-logs`

**Created**: 2026-09-03

**Status**: Draft

**Issue**: #2053

**Input**: "Four services' diagnostic log dumps return a placeholder instead of
logs. `AspireFixture.RecentLogs(resourceName)` answers with
`(not tailed — add '<name>' to AspireFixture.TailedResources)` for any resource
absent from a hand-maintained four-element array, and seven call sites ask for a
resource that is not in it."

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
   second.

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
(`tests/Integration.Tests/Fixtures/AspireFixture.cs`) and a new guard in
`tests/Architecture.Tests`. No production code path is touched, so no leg of the
event→overlay budget is affected.**

## Out of scope

- Tailing `audit-observability`, `api-gateway`, `scenario-simulator`,
  `migrations` or any container resource. None has a `RecentLogs` call site.
- Changing `RecentLogs`' signature, its 120-line default, or the 400-line queue
  bound.
- Reconciling ADR-0083 (see above).
- Deleting any of the seven call sites. The issue rules this out and it is
  right to: removing a diagnostic someone deliberately reached for is the
  opposite of the fix.

## Assumptions

- **A1.** `ResourceLoggerService.WatchAsync` is a subscription over log output
  the Aspire host already produces, so the marginal cost of a tail is a
  subscription plus a bounded queue, not extra log production. Reasoned from
  `AspireFixture.TailResourceLogsAsync` (`:437-489`), **not measured** — step 4
  of the test procedure is what measures it.

## Artifacts deliberately not produced

`research.md`, `data-model.md`, `contracts/`, `quickstart.md` and `checklists/`
are omitted. There is no data model, no contract, no new API surface and nothing
to research beyond what is recorded above. Producing them would be ceremony.
