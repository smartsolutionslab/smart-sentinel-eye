# Spec 339: The failure status the framework chooses

**Feature Branch**: `feat/2571-health-check-registration-guard` (cut from `origin/develop` at `54c83c09`)

**Created**: 2026-10-12

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Q1 (§8) was answered
on 2026-10-12: **option A**, a hand-written capture using the same hosting hook. Questions 2 and 3
of the issue are resolved in §5 and §6 from evidence, not by assumption.

**Input**: Issue [#2571](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2571),
*A composed health-check-registration guard needs a design decision before it can be written*.
Decision comment (repository owner, 2026-10-10): *"add HostFactoryResolver as a test dependency
and build the stronger guard that reads the composed DI model, rather than a weaker source-scan."*

**Lane.** Autonomous (ADR-0144). The lane implements the owner's decision; it may not substitute
a different one. §8 explains why the decision, as worded, could not be implemented literally, and
records the substitute that was approved (option A, 2026-10-12, relayed by the lane's coordinator).

**Spec number.** 339. Checked on 2026-10-12 against `origin/develop` (highest: 338), every remote
branch, every local branch, and the sibling worktrees `sse-1545` and `sse-2867`. None claims 339.
**Check again before opening the PR.**

**ADRs and constitution sections referenced:**

- **ADR-0154**, readiness is liveness by choice. Clause 1 says `/health` answers `200` unless the
  process cannot serve at all. Its Consequences section names this exact gap: *"A tenth check
  registered with the framework's default `failureStatus` (`Unhealthy`) … nothing in CI catches it
  today."*
- **ADR-0103**, no Testcontainers. The guard composes hosts in-process and boots nothing.
- **ADR-0139** and constitution §Testing. Covers red tests for new behaviour and the counterfactual
  for a guard whose subject already holds.
- **ADR-0144**. The lane may not make design decisions. That is why §8 exists.
- **ADR-0109**. Contention files. Under the chosen option A, neither `Directory.Packages.props`
  nor `tests/Architecture.Tests/*.csproj` is touched.
- **ADR-0052** and **ADR-0053**. xUnit with Shouldly, and sentence-style test names.
- **ADR-0105**. Argument guards use `Ensure.That`.

**No new ADR is needed.** The rule this spec enforces is ADR-0154
clause 1. The exemption in §5 follows from that clause and needs no new policy. Q1 picks how a
*test* captures a host. It is not an architectural decision. It went to a human only because the
owner named a specific dependency, and that dependency cannot be added (§8). It is now answered.

---

## 1. What was measured

Every figure was taken on `54c83c09` on 2026-10-12. The probe in §1.2 ran in a scratch project
outside the repository. It referenced the ten host projects in this worktree and was not
committed.

### 1.1 Registration sites (source)

| Site | Name | `failureStatus` | Tags | Reached by |
|---|---|---|---|---|
| `ServiceDefaults/Extensions.cs:129` `AddDefaultHealthChecks` | `self` | *none given → framework default `Unhealthy`* | `live` | `AddServiceDefaults`, so every host |
| `ServiceDefaults/WolverineDefaults.cs:209` | `outbox-{module}` | `Degraded` | `ready` | `AddWolverineForContext`, so the nine contexts |
| `ServiceDefaults/AuthenticationDefaults.cs:216` | `revocation-snapshot` | `Degraded` | `ready` | `AddBearerAuthentication`, so the nine contexts |
| Aspire client integrations | — | — | — | All are switched off: `PostgresTelemetry.cs:97` and `AuditObservabilityInfrastructureModule.cs:89` set `DisableHealthChecks = true` |

**The issue's premise is partly stale.** It names `WolverineDefaults.cs` as the only explicit
non-default `failureStatus`. Spec 270 later added `revocation-snapshot`, also with
`Degraded`. The comment in `Extensions.cs:140-142` lists only `self` and `outbox-{module}`, so it
is stale too. Updating that comment is out of scope (FR-009).

### 1.2 Composed registrations (the real `Program.cs` of each host, captured at `Build()`)

A scratch probe ran each host's real entry point on its own thread. It subscribed to the
`Microsoft.Extensions.Hosting` `DiagnosticListener` and took `IHost.Services` from the
`HostBuilt` event. It then threw, so `Program.cs` never reached `Run`, no hosted service started
and no connection opened. It read `IOptions<HealthCheckServiceOptions>` and instantiated each
registration's factory. HostFactoryResolver uses the same mechanism internally.

| Host | `self` (`Unhealthy`, `live`) | `revocation-snapshot` (`Degraded`, `ready`) | `outbox-{module}` (`Degraded`, `ready`) |
|---|---|---|---|
| ApiGateway | yes | — | — |
| AuditObservability.Api | yes | yes | `outbox-audit-observability` |
| Automation.Api | yes | yes | `outbox-automation` |
| CameraCatalog.Api | yes | yes | `outbox-camera-catalog` |
| EventIngestion.Api | yes | yes | `outbox-event-ingestion` |
| Identity.Api | yes | yes | `outbox-identity` |
| LayoutComposition.Api | yes | yes | `outbox-layout-composition` |
| OverlayDesigner.Api | yes | yes | `outbox-overlay-designer` |
| StreamDistribution.Api | yes | yes | `outbox-stream-distribution` |
| SystemVariables.Api | yes | yes | `outbox-system-variables` |

The ten hosts have **28 registrations in total**. `self` is the **only** registration with the
default `Unhealthy`, and it appears once per host. Every other registration is `Degraded`. All
timeouts are `Timeout.InfiniteTimeSpan`. The `self` factory produces
`Microsoft.Extensions.Diagnostics.HealthChecks.DelegateHealthCheck`.

### 1.3 Can every host be built standalone? Yes, with no live infrastructure.

- **With placeholder configuration, all ten built** in one process, one after another. The probe
  supplied the configuration as command-line arguments to `CreateBuilder(args)` and set
  `--contentRoot` to each host's own project directory. The values were syntactically valid and
  never dialled. The keys are the same set `PostgresComposition.ConfigurationFor` already uses:
  `rabbitmq`, `keycloak`, `mediamtx-api`, `mediamtx-whep`, `Mosquitto:Endpoint`, `blobs`, and the
  nine `*-db` connection strings.
- **With no configuration, nine of ten failed fast and no host hung.** Each threw
  `AddBearerAuthentication`'s *"Keycloak base URL not found"*. ApiGateway built. So a missing key
  surfaces as a loud, named build failure, not a vacuous pass or a timeout.
- **No host needs a reachable database, broker or identity provider to build its `IHost`.**
  Nothing blocks the guard here.
- `tests/Architecture.Tests` **already references all ten host projects**
  (`SmartSentinelEye.Architecture.Tests.csproj:30-63`). The guard needs no new project reference.

### 1.4 Host scope

There are twelve `Program.cs` files under `src/`. Ten call `app.MapDefaultEndpoints()` and so
serve `/health`: ApiGateway and the nine context APIs. **MigrationRunner** and
**ScenarioSimulator** call `AddServiceDefaults`, which registers `self`. They are worker hosts.
They map no endpoint, so no `/health` exists for ADR-0154 to govern. They are out of scope
(FR-007 covers the case where that changes).

### 1.5 The package the decision names is not obtainable

See §8 for the full finding. `Microsoft.Extensions.HostFactoryResolver.Sources` is not on
nuget.org, and this repository has no `nuget.config`, so nuget.org is its only feed.

---

## 2. User story

### US1 (P1): A health check that can turn `/health` into a 503 fails the build

**As** the repository owner, **I want** CI to fail when any host that serves `/health` registers
a health check whose failure status would be `Unhealthy`. **So that** nobody can quietly undo
ADR-0154's decision with an `AddCheck` that leaves out `failureStatus:`.

**Why P1, and why it is the only story.** It is the whole of #2571. The slice ships alone and is
observable end to end: the guard runs in `dotnet test`, and a planted violation turns it red.

**Independent test.** Run `dotnet test tests/Architecture.Tests --filter
FullyQualifiedName~HealthCheckFailureStatusGuard`. All ten host cases and the self-test should
pass. Then, in one host's real composition root or in `ServiceDefaults`, add
`AddCheck("planted", () => HealthCheckResult.Healthy())` with no `failureStatus`. Re-run. The
case for every host that composes it must fail, and the message must name the host and
`"planted"`. Revert, re-run, and confirm green again.

#### Acceptance scenarios

```gherkin
Feature: The failure status of every /health check is not the framework default

  Background:
    Given each of the ten hosts that map /health is composed from its real Program.cs
    And composition stops at Build(), so no hosted service starts and no connection opens

  # Happy path
  Scenario: Today's registrations satisfy the rule
    When the guard reads each host's composed HealthCheckServiceOptions
    Then every registration other than the exempt "self" has a failure status other than Unhealthy
    And each of the ten host cases passes

  # Conflict — the violation the guard exists for
  Scenario: A check registered with the default failure status is reported
    Given a host composition that also registers a check "planted" with no failureStatus argument
    When the guard reads that host's composed registrations
    Then the guard fails
    And the failure message names the host, "planted", its failure status Unhealthy,
      and ADR-0154 clause 1

  Scenario: An explicit Unhealthy is reported the same way as an omitted one
    Given a host composition that registers a check with failureStatus: HealthStatus.Unhealthy
    When the guard reads that host's composed registrations
    Then the guard fails and names that check

  # The exemption cannot be widened by naming
  Scenario: A second check cannot borrow the exemption
    Given a host composition whose "self" registration carries a tag other than exactly "live"
    When the guard reads that host's composed registrations
    Then the guard fails and names "self" as not matching the exempt shape

  # Bad request — the harness cannot produce a host
  Scenario: A host whose composition throws fails loudly, not vacuously
    Given a host whose required configuration key is missing from the placeholder set
    When the guard tries to capture that host
    Then the case fails with the host's own build exception
    And it does not pass with zero registrations

  Scenario: A host whose entry point never builds within the timeout fails loudly
    Given a host whose Program.cs does not reach Build() within the capture timeout
    When the guard tries to capture that host
    Then the case fails with a timeout naming the host

  # Coverage — a new host cannot slip in unguarded
  Scenario: A host that maps /health but is missing from the guard's list fails the build
    Given a Program.cs under src/ that calls MapDefaultEndpoints
    And its assembly is not in the guard's host list
    When the completeness check runs
    Then it fails and names that Program.cs

  # Auth
  # N/A — the guard is a build-time architecture test. It reads no HTTP surface, no token,
  # and no scope. /health stays anonymous by design (ADR-0154, spec 078), and this spec
  # does not change that.
```

---

## 3. Functional requirements

- **FR-001** For each of the ten hosts in §1.4, the guard composes the host's **real** entry
  point (`Program.cs`) in-process. It does not copy the DI setup. It stops at `Build()`, so
  hosted services never start.
- **FR-002** The guard reads `IOptions<HealthCheckServiceOptions>.Value.Registrations` from the
  captured `IServiceProvider`. It asserts that every registration has `FailureStatus !=
  HealthStatus.Unhealthy`, except the exempt registration in FR-003.
- **FR-003** The **only** exemption is a registration named exactly `self` that meets all of:
  tags exactly `{ "live" }`, a factory that produces `DelegateHealthCheck`, and exactly one such
  registration per host. If a registration named `self` fails any of these, it is a violation.
  The exemption must not widen by name alone. §5 gives the rationale.
- **FR-004** If a host's capture throws, the case fails with the host's exception. If it does not
  build within a bounded timeout, the case fails with a timeout. In both cases the host must be
  named. A host that yields zero registrations fails too, because every host composes at least
  `self` (§1.2).
- **FR-005** There is one case per host. A violation in one host does not hide the others. The
  failure message lists every offender with its host, name, `FailureStatus` and tags, and cites
  ADR-0154 clause 1.
- **FR-006** A self-test proves the guard has bite. It does so without editing production code.
  It captures a real host and adds a planted default-status registration through the capture
  mechanism's builder hook. It then asserts that the guard's evaluator reports the planted
  registration and nothing else. This self-test is phase 4a's red test (§7).
- **FR-007** Completeness: a test enumerates every `src/**/Program.cs` that calls
  `MapDefaultEndpoints`. It asserts each one's assembly is in the guard's host list, and that
  every listed host maps `/health`. Paths are slash-normalised (memory: *source-scanning tests
  need slash normalising*). This is a **coverage** check only. The rule itself is never checked
  from source text.
- **FR-008** The placeholder configuration lives in one place. It reuses
  `PostgresComposition.ConfigurationFor`'s key set where the keys overlap, and does not duplicate
  it. Every value must be syntactically valid and never dialled.
- **FR-009** No production code changes. The stale comment at `Extensions.cs:140-142` is noted
  and not fixed. Fixing it would be a separate, comment-only change (§9).

## 4. Success criteria

- **SC-001** On `develop` with this branch applied, all ten host cases pass, and so do the
  self-test and the completeness test.
- **SC-002** The self-test (FR-006) was **observed failing** before the evaluator existed. The
  failure is quoted verbatim in the PR (ADR-0139).
- **SC-003** Counterfactual run against production code: locally change
  `WolverineDefaults.cs:211` to `HealthStatus.Unhealthy`. **Exactly the nine context-host cases
  fail**, because ApiGateway does not compose the outbox. Revert, and all ten pass. Quote the
  output in the PR, following spec 282's `T031-counterfactual.md` precedent.
- **SC-004** Counterfactual for the exemption: locally change `Extensions.cs:129`'s tag to
  `"ready"`. All ten cases fail and name `self`. Revert.
- **SC-005** The guard opens no socket. It runs with no Aspire stack and no Docker, and finishes
  in under 30 s on a dev machine (§1.3: the probe built all ten in seconds).

## 5. Question 2, resolved: what "otherwise accounted for" means

**The rule:** *every check registered on a host that serves `/health` declares a failure status
other than `Unhealthy`, except the framework's own liveness check, `self`.*

**This is a derived rule, not a judgment call.** The probe (§1.2) found exactly one registration
that would need an exemption, and that registration is the same in all ten hosts. Exempting it
follows from three facts:

1. **Its `Unhealthy` is unreachable.** `failureStatus` applies only when a check throws or times
   out. `self` is `() => HealthCheckResult.Healthy()`, a constant lambda that cannot throw, and
   its timeout is infinite.
2. **If it were reached, `Unhealthy` would be correct.** ADR-0154 clause 1: *"`/health` … answers
   `200` unless the process cannot serve at all."* A process that cannot run a constant lambda
   cannot serve at all. `self` is the one check whose failure *should* fail liveness, and its
   `live` tag is what `/alive` selects on.
3. **It names and reaches nothing**, so ADR-0154 clause 2 is not engaged (spec 078's exposure
   analysis already accepts it by name).

FR-003 pins the exemption to `self`'s **shape** as well as its name (`live` tag only,
`DelegateHealthCheck`, one per host). That way a future check cannot claim the exemption by
calling itself `self`.

**What would make this a real policy question again**, so the lane stops instead of extending
the rule: a second registration in any host that genuinely needs `Unhealthy`. One example is a
per-replica dependency, which ADR-0154 clause 4 names as the condition for reversing the
decision. Such a case must not be added as a second exemption here. It reopens ADR-0154.

## 6. Question 3, resolved: ADR-0154 clause 2 is out of scope here, and already has a guard

**#2571 covers the `failureStatus` guard only.** The issue says clause 2 *"can't be checked from
a registration's shape at all — that's a different kind of check entirely."* That reads as a
scope exclusion, and this spec treats it as one.

**One correction to the issue's framing.** Clause 2 *does* already have a heuristic guard.
`tests/Architecture.Tests/Persistence/DependencyHealthCheckGuardTests.cs` (spec 282, #1140)
checks it with a name vocabulary plus the producing assembly's prefix (`HealthChecks.`,
`Aspire.`, `Npgsql`, EF's health-check assembly). The 2026-10-11 comment on #2571 confirms this
too. That guard has two known gaps, and both are recorded in its own doc comment:

- It composes `Add{Context}Infrastructure` **only**. It does not compose `AddServiceDefaults` or
  the rest of `Program.cs`. A check registered in a `Program.cs` or in `ServiceDefaults` is
  invisible to it. Its doc comment says so explicitly and attributes the gap to *"#2571's open
  question 1"*.
- A bespoke check that has a name outside the vocabulary and a type from an unlisted assembly
  passes it. That risk is accepted in spec 282's plan §7.

**Follow-up for the orchestrator to consider filing (not filed by this spec).** Once this spec's
host-capture harness lands, re-point `DependencyHealthCheckGuardTests` at the **composed** host
instead of `Add{Context}Infrastructure` only. This closes the first gap. It needs no new policy,
because the rules are unchanged and only the composition becomes more complete. The second gap
is inherent to clause 2 and stays out of scope.

## 7. Phase 4a colour: **red** (behaviour-changing)

This adds new guard behaviour. Nothing is being preserved.

- **The red test** is FR-006's self-test. It captures a real host (e.g. ApiGateway, the smallest
  at one registration) with a planted `AddCheck("planted", () => HealthCheckResult.Healthy())`
  added through the capture hook, and asserts that the evaluator's offenders are exactly
  `["planted"]`. It is written against an evaluator that does not yet report anything, for
  example a stub that returns no offenders. It must fail **on the assertion**, with Shouldly's
  "should be … but was empty". A compile error does not count. The test-writer quotes that output
  verbatim.
- **The ten host cases arrive green, and that is correct.** The property they guard already holds
  (§1.2). They are the safety net, not the new behaviour (precedent:
  `DependencyHealthCheckGuardTests`' own doc comment). Their bite is proven by SC-003 and SC-004,
  not by a red test. The PR must say this, so a reviewer does not read a green host case as a
  4a breach.
- **The completeness test (FR-007) also arrives green.** Its bite is proven by a counterfactual
  that removes one host from the list.

## 8. Clarification (resolved 2026-10-12)

### Q1: `HostFactoryResolver` cannot be added as a package. Which substitute should the guard use?

**Answer: option A, the hand-written capture.** Approved on 2026-10-12 and relayed to this phase by
the lane's coordinator, in place of the unavailable `HostFactoryResolver.Sources` package.
**Consequence:** `Directory.Packages.props` and the test `.csproj` are **not** touched. The capture
is about 60 lines of repository code in `tests/Architecture.Tests` (plan §3). The finding and the
options that were weighed are kept below, so a later reader can see why the package is absent.

**Finding.** The owner's decision names `HostFactoryResolver` as a test dependency. Its package,
`Microsoft.Extensions.HostFactoryResolver.Sources`, is **not published to nuget.org**:

- `api.nuget.org/v3-flatcontainer/microsoft.extensions.hostfactoryresolver.sources/index.json`
  returns `BlobNotFound`. The registration index returns `404`. A search for
  `packageid:Microsoft.Extensions.HostFactoryResolver.Sources` returns `totalHits: 0`.
- The dnceng public feeds `dotnet-public`, `dotnet10` and `dotnet9` do not carry it. It exists
  only on internal **transport** feeds as servicing prereleases (`dotnet10-transport`:
  `10.0.14-servicing.*`, `dotnet-eng`: `11.0.0-preview.*`, `dotnet8`: one `8.0.0-rc.2`). It is a
  non-shipping, build-infrastructure package.
- The repository has no `nuget.config`, so nuget.org is its only feed.

So the decision cannot be implemented as written. The owner's intent is clear and unaffected:
compose the real hosts in-process, never a source scan. The **vehicle** must change, and that is
the owner's call, not the lane's.

| Option | What it is | Cost | Risk |
|---|---|---|---|
| **A. Hand-written capture (chosen)** | About 60 lines in `tests/Architecture.Tests`. It runs `assembly.EntryPoint` on a dedicated thread, subscribes to the `Microsoft.Extensions.Hosting` `DiagnosticListener`, injects placeholder configuration on `HostBuilding`, captures `IHost.Services` on `HostBuilt`, then throws to stop `Program.cs`. This is the mechanism the issue's own Q1 calls *"an equivalent in-process host-capture mechanism"*. **The §1.2 probe *is* this mechanism, and it worked on all ten hosts.** | No new package or feed. Written under the repo's own conventions, analyzers and §II carve-outs for tests. | It depends on the `HostBuilding`/`HostBuilt` event names. These are a de-facto contract: WebApplicationFactory and EF Core tooling both depend on them, and they have been unchanged since .NET 6. If they change, every case fails loudly (FR-004), not vacuously. |
| B. Vendor `HostFactoryResolver.cs` | Copy the MIT-licensed file from `dotnet/runtime` into the test project with attribution. This is what a `.Sources` package would do. | About 370 lines of third-party code. It is unlikely to pass Release analyzers (`TreatWarningsAsErrors`, S138/S107 metrics, IDE1006 naming) without edits or **new suppressions**. ADR-0144 bars the lane from adding suppressions. | It drifts from upstream with no update path. |
| C. Add `dotnet10-transport` to a new `nuget.config` | Pin a servicing prerelease of the real package. | A new, unstable package source for the whole repository. | Supply chain: the transport feeds are internal build plumbing and have no stability promise. **Not recommended.** |
| D. `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 (on nuget.org) | `WebApplicationFactory<T>`. It uses HostFactoryResolver internally but does not expose it. | It **starts** the host, so Wolverine's hosted services dial RabbitMQ and Postgres. It cannot run without the live stack, which defeats SC-005. | **Unsuitable.** Listed so it is not rediscovered. |

**Why A.** It is the smallest change, it is already proven on all ten hosts, and the issue named
it as an acceptable equivalent. The owner's yes was needed because the decision on record named
the package, and that yes was given.

**The answer does not change §2–§7.** Every requirement holds as written.

## 9. Out of scope

- ADR-0154 clause 2 (§6). Its follow-up is a suggestion only, not filed.
- MigrationRunner and ScenarioSimulator, because they serve no `/health` (§1.4).
- **Other `Unhealthy` paths the registration's shape cannot show**, recorded so they are not
  assumed covered:
  - A check whose code returns `HealthCheckResult.Unhealthy(...)` directly. ADR-0154 recorded none
    in `src/`. A grep on `54c83c09` finds none outside comments.
  - A `HealthCheckOptions.ResultStatusCodes` override at `MapHealthChecks`. Endpoint options are
    not in DI, and capture stops before `MapDefaultEndpoints` runs.
  
  Either would need a differently shaped guard and its own issue.
- The stale comment at `Extensions.cs:140-142`. It omits `revocation-snapshot`. That is a
  comment-only follow-up.

## 10. Latency budget impact (constitution §IV)

**N/A.** This is a build-time architecture test. No production code changes and no leg is
touched.

## 11. Locked tech choices

xUnit + Shouldly (ADR-0052). Sentence-style names (ADR-0053). The test project is
`tests/Architecture.Tests`, which already references every host. No Testcontainers and no Aspire
boot (ADR-0103). Configuration goes in as command-line arguments, or through the capture hook,
never as process-wide environment variables, which would leak across parallel tests. The ten
captures run in one **non-parallel** xUnit collection, because the `DiagnosticListener`
subscription is process-global.
