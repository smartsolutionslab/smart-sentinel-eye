# Feature Specification: A timeout names the unhealthy resource

**Feature Branch**: `test/2146-fixture-timeout-snapshot`

**Created**: 2026-10-21

**Status**: Draft — phases 1-3, autonomous lane (ADR-0144).

**Issue**: #2146 (*A resource nothing waits on can still kill the boot, and the
fixture will name the wrong one*)

**ADRs**: ADR-0103 (integration tests boot the real Aspire stack, no
Testcontainers), ADR-0068 (the fixture), ADR-0037 (phases and gates), ADR-0139 /
constitution §Testing (new behaviour starts red), ADR-0144 (the lane; it may not
write an ADR), ADR-0052 / ADR-0053 (Shouldly, sentence-style test names).

**Predecessors**: spec 084 (#2066, the end-of-boot sweep
`ThrowIfAnyGatedResourceDiedAsync`) · spec 098 (#1930, the `ended` arm of the
cause line) · #2061 (the cause line exists) · #1929 (exit codes in the state
list) · #2038 (the snapshot, not the console tail, is the instrument).

> **Every construct is named, not line-numbered**, for the reason spec 098 gives.

---

## 1. The binding decision

The owner decided on 2026-10-21 (comment on #2146):

> option (c): when the AspireFixture boot times out, the catch dumps a snapshot
> of every resource's state so the dying resource is named (no continuous
> watchdog, no ADR; a watchdog (a) only if the flake rate hurts, and it would
> need a settle-and-re-read lifetime because healthy boots pass through
> Finished).

So this spec **does not** build the supervised boot the issue body proposes,
**does not** add a background `WatchAsync`, **does not** cancel the shared token
from anywhere new, and **does not** touch the twelve waits. It changes what the
existing timeout `catch` in `AspireFixture.InitializeAsync` *says*. Because the
decision is a diagnostic inside the existing catch, it applies ADR-0103/0068
rather than amending them, and **no ADR is needed** — the reasoning spec 084 used
for its own sweep, and the owner's explicit "no ADR".

---

## 2. Premise, re-verified against `origin/develop` (`7909dd20`)

The issue body was written on 2026-09-07 and has been corrected twice in its own
comments. Re-read against today's fixture
(`tests/Integration.Tests/Fixtures/AspireFixture.cs`):

### 2a. Most of option (c) already exists

The timeout catch —
`catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)`
at the end of `InitializeAsync` — already:

| Owner asked for | Standing today |
|---|---|
| a snapshot of **every** resource | **Yes.** `CaptureResourceStateMapAsync` runs a bounded three-second `ResourceNotificationService.WatchAsync`, which replays the last snapshot of every tracked resource (decompiled `WatchAsync` on Aspire.Hosting 13.6.1 yields `LastSnapshot` for every entry of `_resourceNotificationStates` first). `rabbitmq`, `postgres`, `mosquitto` and the databases are in it although nothing waits on them. |
| name | **Yes.** `FormatResourceStates`, one line per resource. |
| state | **Yes.** |
| exit code if any | **Yes** (#1929): `name: State (exit code N)`. |
| **health** | **No.** Neither `CustomResourceSnapshot.HealthStatus` nor `HealthReports` is read anywhere on the timeout path. Only the public `ResourceDiagnosticsAsync` (a per-test helper, not the timeout path) prints health. |
| **so the dying resource is named** | **Partly.** `FormatLikelyCause` names a resource that exited non-zero, or that reached `Finished`/`Exited`/`Terminated` (spec 098). It deliberately names **nothing** for `FailedToStart` (#2061: those are the dependents, not the cause) and **nothing** for `RuntimeUnhealthy` — and `EndedStates`' own doc comment says, of that second exclusion, "**#2146 owns that case**". |

So the issue title's "the fixture will name the wrong one" is imprecise today:
the timeout message names **no** resource for the shapes below, rather than the
wrong one. The state list is printed; the reader must find the cause in ~45
undifferentiated lines, which is #2061's complaint.

### 2b. The two shapes that stay silent

**Shape 1 — a resource nothing waits on is up but unhealthy.** Nine services
declare `.WaitFor(rabbitmq)` in `src/AppHost/AppHost.cs`; Aspire's `WaitFor`
waits for **healthy**, not `Running`
(`ResourceNotificationService.WaitForDependenciesAsync` matches
`re.Snapshot.HealthStatus == HealthStatus.Healthy`). A `rabbitmq` container that
is `Running` with a failing health check holds all nine services in `Waiting`.
At the timeout the report says `rabbitmq: Running` — identical to a healthy
broker — and no cause line. The resource that killed the boot is printed and
indistinguishable from the forty-four that are fine.

**Shape 2 — the 2026-09-07 sighting.** `mediamtx` went `RuntimeUnhealthy`,
`stream-distribution` reached `FailedToStart`, the fixture timed out at 8
minutes. The state list printed both; the cause line was empty, because
`FailedToStart` and `RuntimeUnhealthy` are both excluded from it.

### 2c. Why the end-of-boot sweep does not help

`ThrowIfAnyGatedResourceDiedAsync` is the **last statement of the `try`**. Any
wait that blocks until the shared eight-minute `cts` expires throws
`OperationCanceledException` first; the `catch` runs; the sweep is never
reached. Spec 084 recorded this limit; #2146's 2026-09-08 comment confirmed it
covers all twelve gated resources, not only the four with no wait. Nothing here
changes the sweep.

### 2d. What the Aspire API exposes (pinned 13.6.1, decompiled)

- `CustomResourceSnapshot.HealthStatus` is **derived**, with a private setter:
  `ComputeHealthStatus(reports, state)` returns **`null` for every state other
  than `Running`**, `Healthy` when there are no reports, otherwise the minimum
  report status. **A report whose `Status` is `null` (the check has not run yet)
  makes the aggregate `Unhealthy`** — `MinBy` puts the null first and
  `GetValueOrDefault()` maps it to `Unhealthy` (enum value 0).
- `HealthReports` is `ImmutableArray<HealthReportSnapshot>`; its `init` is
  `internal`, but `CustomResourceSnapshotExtensions.WithHealthReports` is public.
- `HealthReportSnapshot(string Name, HealthStatus? Status, string? Description,
  string? ExceptionText)` has a public constructor, so tests can build one by
  hand.
- `ResourceNotificationService` has a public constructor
  `(ILogger<ResourceNotificationService>, IHostApplicationLifetime)`, and
  `PublishUpdateAsync` does **not** recompute `HealthStatus` beyond what the
  snapshot's own setters do — so an in-process instance can be driven without
  Docker.
- `StartTimeStamp` / `StopTimeStamp` exist; this spec does not use them (§6).

### 2e. Reusable pieces

`FormatTimeoutMessage`, `FormatResourceStates`, `FormatLikelyCause` and
`UnhealthyResources` are already `internal static` pure functions with a
`Category=FixtureLogic` test class (`AspireFixtureReportSelectionTests`, 23
facts) that the `backend` CI job runs without Docker. The `exitCodes` instance
field is the precedent for a per-resource datum captured alongside the state.
This spec extends those; it introduces no new helper family.

---

## 3. User stories

### US1 — The timeout report shows each running resource's health (P1)

**As** the person reading a red CI integration job, **I want** the state list in
the startup-timeout message to carry each resource's health and its failing
health reports, **so that** a resource that is `Running` but unhealthy is
distinguishable from one that is fine.

**Why P1**: it is the datum the owner's decision names and the one that is
missing entirely.

**Independent test**: `Category=FixtureLogic` facts over `FormatResourceStates`
and `FormatTimeoutMessage`, fed hand-built `HealthReportSnapshot`s. No Docker.

```gherkin
Scenario: A running resource with a failing health check shows it
  Given rabbitmq is Running with health Unhealthy
    And its report "rabbitmq_check" is Unhealthy with description "Connection refused"
    And nine services are Waiting
  When the startup-timeout state list is formatted
  Then the rabbitmq line reads "rabbitmq: Running (health Unhealthy)"
    And beneath it the report "rabbitmq_check" is listed as Unhealthy with "Connection refused"

Scenario: A healthy running resource carries its health and no report lines
  Given keycloak is Running with health Healthy
  When the state list is formatted
  Then the keycloak line reads "keycloak: Running (health Healthy)"
    And no report line follows it

Scenario: A resource with no health captured renders exactly as before   # characterisation
  Given camera-catalog is FailedToStart and no health was captured for it
  When the state list is formatted
  Then the line is byte-for-byte what it is today: "camera-catalog: FailedToStart"

Scenario: Exit code and health share one parenthetical
  Given automation is Running with exit code 137 and health Unhealthy
  Then the line reads "automation: Running (exit code 137, health Unhealthy)"

Scenario: A health check that has not run is shown as pending, not as unhealthy   # bad input
  Given identity is Running with one report whose Status is null
  Then its report line says "pending" and never "Unhealthy"

Scenario: The dump stays bounded   # bad input
  Given a resource with five non-healthy reports, one with a 2 000-character description
  Then at most three report lines are printed, followed by "(+2 more)"
    And no printed description exceeds 200 characters
```

### US2 — The cause line names an unhealthy resource (P1)

**As** the same reader, **I want** the `Likely cause:` line to name a resource
that is unhealthy at the timeout, **so that** shapes 1 and 2 (§2b) are named at
the top of the message rather than buried in the state list.

**Independent test**: `Category=FixtureLogic` facts over `FormatLikelyCause`.
Shape 2 needs no health data at all and is red on today's code.

```gherkin
Scenario: The 2026-09-07 sighting names mediamtx            # happy path, red today
  Given mediamtx is RuntimeUnhealthy
    And stream-distribution is FailedToStart
    And every other resource is Running
  When the likely cause is formatted
  Then it starts with "Likely cause: "
    And it names mediamtx and its state
    And it does not name stream-distribution

Scenario: A running resource that fails its health check is named   # shape 1
  Given rabbitmq is Running with health Unhealthy from a report with non-null status Unhealthy
    And nine services are Waiting
  Then the likely cause names rabbitmq and "Unhealthy"
    And it names none of the nine Waiting services

Scenario: A check that has not run is not a cause           # conflict with Aspire's aggregate
  Given identity is Running and its only report has Status null
    And so Aspire's aggregate HealthStatus reads Unhealthy
  Then the likely cause does not name identity

Scenario: Death still outranks unhealth                     # conflict
  Given migrations exited with code 134
    And rabbitmq is Running with health Unhealthy
  Then the likely cause names migrations first, then rabbitmq
    And names each exactly once

Scenario: Existing cause-line behaviour is unchanged        # characterisation
  Given any input from the 23 existing AspireFixtureReportSelectionTests facts
  Then every one of them passes unmodified
```

**Auth scenario**: N/A — test infrastructure; no endpoint, no token, no scope.

### US3 — The captured snapshot carries the health (P2)

**As** the maintainer, **I want** the capture that feeds the formatter to record
each resource's health from the live `ResourceNotificationService`, **so that**
US1/US2 are not formatting data the timeout path never collects.

**Why P2, and separable**: US1/US2's formatter tests cannot tell whether the
capture populates the health — deleting the capture line leaves them all green
(the same honest limit spec 084's plan recorded for its own call site). US3
closes that with a Docker-free test against an in-process
`ResourceNotificationService` (§2d), at the cost of extracting the capture into
a static function. If the in-process service does not behave as §2d's decompile
says, US3 is deferred and the limit is recorded instead — US1/US2 still ship.

```gherkin
Scenario: The capture records health from published snapshots
  Given an in-process ResourceNotificationService
    And rabbitmq has published Running with an Unhealthy report "rabbitmq_check"
    And camera-catalog has published Waiting
  When the startup-timeout capture runs
  Then the captured health for rabbitmq is Unhealthy with the report "rabbitmq_check"
    And no health is captured for camera-catalog   # HealthStatus is null off Running
    And the captured states and exit codes are what they are today
```

---

## 4. Functional requirements

- **FR-001** On the startup-timeout path, the state list MUST carry, for every
  resource whose captured `HealthStatus` is non-null, `health <Status>` inside
  the existing parenthetical; a resource with both an exit code and a health
  shows `(exit code N, health H)`.
- **FR-002** Beneath such a line, every report whose `Status` is not `Healthy`
  MUST be listed as `    <report name>: <Status or "pending"> — <text>`, where
  `<text>` is `Description`, else the first line of `ExceptionText`, else
  nothing.
- **FR-003** Bounded output: at most **three** report lines per resource,
  followed by `(+N more)` when more exist; each `<text>` truncated to **200**
  characters. `ExceptionText` is never printed beyond its first line.
- **FR-004** A resource with no captured health MUST render byte-for-byte as it
  does today. Every existing `Category=FixtureLogic` test MUST pass
  **unmodified**.
- **FR-005** `FormatLikelyCause` MUST gain a third, disjoint arm naming every
  resource that is (a) in state `RuntimeUnhealthy`, or (b) `Running` with at
  least one report whose `Status` is `Unhealthy` (non-null). Pending-only
  (`null` status) reports MUST NOT qualify. `-rebuilder` resources are excluded
  from the sentence, as in the `ended` arm.
- **FR-006** The new arm's sentence MUST assert only what its predicate
  checked — the resource was unhealthy when the timeout fired — and MUST NOT
  claim it died or exited. It comes after the existing two arms, and no resource
  is named twice across arms.
- **FR-007** `FailedToStart` remains excluded from the cause line (#2061).
  `FatalStartupStates`, `EndedStates`, `IsHealthy`, `SelectResourcesToReport`
  and the end-of-boot sweep are unchanged.
- **FR-008** The diagnostic lives in the `TimeoutException` message thrown by
  the existing catch — the text xUnit prints for every test in the collection
  and therefore the text a CI reader sees. No new log sink, no file.
- **FR-009** (US3) The capture MUST record `HealthStatus` and `HealthReports`
  from the same `WatchAsync` events it already reads, last event wins, exactly
  as it records the exit code today. No new subscription, no new wait, no change
  to the three-second window.
- **FR-010** No change to `InitializeAsync`'s waits, their order, the shared
  `cts`, `StartupTimeout`, or the catch's `when` filter.

## 5. Success criteria

- **SC-001** For shapes 1 and 2 of §2b, the formatted timeout message's first
  two lines name the unhealthy resource (verified by FixtureLogic facts built
  from those shapes).
- **SC-002** All 23 existing `AspireFixtureReportSelectionTests` facts, and every
  other `Category=FixtureLogic` test, pass unmodified.
- **SC-003** For the 45-resource fixture of `StatesFromTheRunThatMotivatedThis`
  with a health entry for every `Running` resource, the state-list section grows
  by no more than ~20 characters per line plus at most three bounded report lines
  per unhealthy resource.

## 6. Out of scope (recorded, not forgotten)

- **The watchdog (option a)** and the supervised boot — deferred by the owner
  until flake rate justifies it; it would need a settle-and-re-read lifetime,
  because a healthy boot passes through `Finished` (spec 098 §6).
- **#1930** (`automation` exits during startup) — open, not touched. Its shape
  is already named by spec 098's `ended` arm.
- **Naming the wait that was blocked** when the timeout fired. Not in the
  owner's decision; the cause line, not the wait, is what answers "what broke".
- **`Last camera-catalog logs:`** — still hard-coded to one resource. Unchanged.
- **The three 60-attempt probes** (`WaitForKeycloakRealmAsync`,
  `WaitForMediaMtxAsync`, `WaitForServiceHealthAsync`) throw `TimeoutException`,
  which the catch's `when` filter does not match, so none of them produces the
  snapshot. A real gap, separate from this decision; a follow-up issue is filed
  at phase 7 rather than widened into this one (FR-010).
- **`StartTimeStamp` / `StopTimeStamp`** — would order deaths, but the owner's
  list does not include them and FR-005's arm does not need them.

## 7. Latency, security, constitution

- **Latency budget (§IV)**: N/A. Test fixture only; no leg of the
  event→overlay path is touched.
- **Security**: none. No endpoint, auth, secret, or trust boundary. Health
  descriptions come from the AppHost's own checks inside the test process.
- **§II primitives**: N/A — test infrastructure, not a domain model.
- **ADR**: none needed (§1).

## 8. Assumptions

- **A1** The public `WithHealthReports` extension and the public
  `HealthReportSnapshot` constructor are enough to hand-build inputs. Verified by
  reading the pinned 13.6.1 XML docs and decompile, not by compiling a test.
- **A2** (US3) An in-process `ResourceNotificationService` constructed with a
  null logger and a hand-written `IHostApplicationLifetime` fake, driven with
  `PublishUpdateAsync(resource, s => (s with { State = … }).WithHealthReports(…))`,
  replays those snapshots through `WatchAsync`. Read from the decompile; **not
  executed**. Phase 4a confirms it or defers US3.
- **A3** The 200-character and three-report limits are chosen, not measured;
  they bound the message, and nothing downstream depends on the exact figures.
