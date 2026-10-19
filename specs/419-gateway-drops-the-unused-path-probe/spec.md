# Spec 419: The MediaMTX gateway drops its unused single-path health probe

**Feature Branch**: `refactor/3013-remove-unused-path-health-probe` (cut from `origin/develop` at `28bcec96`)

**Created**: 2026-10-19

**Status**: Specified, planned and tasked. No `[NEEDS CLARIFICATION]` remains.

**Input**: Issue [#3013](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3013), owner
decision of 2026-10-19: "remove the unused GetPathHealthAsync and its NotRegistered, together with
the four spec-331 probe-driven log-volume tests and the two PerPathCalls.ShouldBe(0) assertions that
exist only for it; those deletions are authorised for this change. The HealthSweepFailed throttle and
the page byte limit remain undecided and out of scope. Enrolling for US1 only."

This is **spec 415's deferred US1**, re-specified against current develop. Spec 415
([spec](../415-gateway-drops-the-unused-probe-and-the-reason-phrase/spec.md) §1 P1/P2/P4, §2 US1, §4
FR-101..FR-106, §6 A1; [plan](../415-gateway-drops-the-unused-probe-and-the-reason-phrase/plan.md)
§2, §5) holds the reasoning and is referenced rather than copied. What this spec adds: the premise
re-verified after specs 416-418, current line numbers, the R9 row (US2 landed a test on the probe),
and the owner's authorisation, which removes 415's veto point A1.

**ADRs**: ADR-0144 (autonomous lane; the deleted-test prohibition is lifted by the owner for exactly
the set in §4, nothing else), ADR-0139 (characterisation for a behaviour-preserving change), ADR-0036
(smallest change), ADR-0052 / ADR-0053 / ADR-0103 (hand-written stubs, sentence-style names, no
Testcontainers), ADR-0171 (the per-path probe's hook-rate cost, the reason the list read exists).
Neighbours: specs 331 (the probe's log volume), 409, 410 (the sweep never probes per path), 415.

**No ADR is needed.** No ADR or constitution section mandates a single-path probe:
`grep -rn "GetPathHealth" docs .specify CLAUDE.md` finds nothing. The one ADR naming
`/v3/paths/get` (ADR-0163:33) is about ScenarioSimulator's camera-sim client
(`src/ScenarioSimulator/CameraSim/CameraSimPathClient.cs`), a different context with its own
`HttpClient`, untouched here. `IRtspGateway` is a StreamDistribution Domain port, not a
`Shared.Contracts` type and not a constitution §IX strategy interface (its "ADR-0061" citation is
stale: ADR-0061 is *Moq confirmed*; corrected in passing).

**Latency budget (constitution §IV)**: **N/A.** Control plane only; the removed method is not called.

---

## 1. Premise check (develop at `28bcec96`, after specs 405/410/415/416/417/418)

| # | Claim | Verified | Holds? |
|---|---|---|---|
| P1 | No production code calls `GetPathHealthAsync` | `grep -rn GetPathHealthAsync src`: the declaration `IRtspGateway.cs:46`, two doc `cref`s (`:50`, `:69`), the implementation `MediaMtxRtspGateway.cs:171-188`. Nothing else. The three resolvers of `IRtspGateway` (`StreamHealthWatcher.cs:70`, `MediaMtxReconciler.cs:139`, `MediaMtxSourceRepairSweep.cs:64`) call `GetAllPathsHealthAsync`, `ListConfigured*` and the add/repoint/remove members only. No reflection, no frontend, no `.yaml`/`.json` route allowlist names `/v3/paths/get` for StreamDistribution | **Yes** |
| P2 | `NotRegistered` is dead with it | `MediaMtxRtspGateway.cs:280-284`; sole reader `:179` (inside the probe). The watcher builds its own "path not registered" (`StreamHealthWatcher.cs:150`) | **Yes** |
| P3 | `ParsePathHealth` / `TryReadIsoTimestamp` / `DetectTranscodeMode` stay | Still read by `AddPageToHealth` (`:274`) | **Yes**: they stay |
| P4 | DI and architecture tests do not depend on the member | `StreamDistributionRegistrationCharacterisationTests` (Architecture.Tests) pins the registration (`Transient IRtspGateway -> factory`, `RetryEveryMethod` marker, the named client), not members. No NetArchTest rule names it | **Yes** |
| P5 | Test-only callers and implementers | Callers: `MediaMtxProbeLogVolumeTests.cs:74, :111, :140, :218` (spec 331); `MediaMtxRtspGatewayReasonPhraseTests.cs:167` (spec 415 R9). Implementers: ten test doubles (plan §2). Counters: `PerPathCalls` in two watcher stubs, asserted at `StreamHealthWatcherScopeTests.cs:78-81` and `StreamHealthWatcherAbsenceHoldTests.cs:162` | **Yes** |
| P6 | Removal changes no observable behaviour | No caller means no HTTP request, log record, metric, hook call or health outcome comes from it in any running service. The list read, the sweep, the source repair and the reconciler are unchanged | **Yes**: behaviour-preserving |

**Verdict.** Premise holds. **Behaviour-preserving (dead code) → characterisation** (ADR-0139).
Production behaviour does not move; only tests that can no longer be written are removed, and only
the owner-authorised set (§4).

## 2. User story

### US1 (P1): the gateway carries no unused single-path probe

A method nothing calls cannot be reviewed by use, and this one carries the parse leak spec 409 closed
for the list readers (spec 415 P2). Removing it removes the leak and turns "the health sweep never
probes per path" (spec 410's guard, today two `PerPathCalls == 0` assertions) into something the
compiler enforces: the call can no longer be written.

**Independent test.** Release build of the solution; StreamDistribution unit projects and
Architecture.Tests green; the spec-331 log-volume properties pinned through `GetAllPathsHealthAsync`
by facts that were green on develop before the removal and are byte-identical after it.

## 3. Acceptance scenarios

```gherkin
Feature: the MediaMTX gateway has no single-path health probe

  # characterisation: green on develop before the removal, unmodified after
  Scenario Outline: a fab-scale run of healthy health sweeps stays quiet
    Given the shipped appsettings for <environment> and the real resilience handler
    When GetAllPathsHealthAsync reads a one-page healthy list 250 times
    Then the stub saw 250 requests to /v3/paths/list
    And no Information record is written by the IRtspGateway HttpClient or Polly categories
    Examples:
      | environment |
      | Production  |
      | Development |

  Scenario: a sweep answered not-ready stays quiet
    Given a one-page list whose only item is not ready
    When GetAllPathsHealthAsync runs
    Then no Information record is written by those categories

  Scenario: a list read that needs retries still reports them (failure path)
    Given MediaMTX answers 503 on every attempt
    When GetAllPathsHealthAsync runs
    Then it throws HttpRequestException
    And Polly logs a handled attempt and a retry at Warning and the last attempt at Error

  Scenario: an operator override brings the exchange back
    Given Logging:LogLevel overrides for the gateway client (Information) and Polly (Debug)
    When GetAllPathsHealthAsync reads a one-page healthy list
    Then the four HttpClient handler records appear at Information and the ExecutionAttempt at Debug

  # structural (build)
  Scenario: the probe no longer exists
    Then IRtspGateway declares no single-path health read
    And grep -rn GetPathHealthAsync over src and tests finds nothing
```

Auth: N/A (no endpoint, no scope, no caller change). Bad request / conflict: N/A (no input surface;
the 503 row is the failure path). The 404 row of spec 331 has no twin: `GetAllPathsHealthAsync`
answers a 404 by throwing, so "a 404 probe writes no Information record" belongs to the removed
method alone and goes with it (owner-authorised, §4 D2).

## 4. Functional requirements

- **FR-001** Remove `IRtspGateway.GetPathHealthAsync` and `MediaMtxRtspGateway.GetPathHealthAsync`,
  and `MediaMtxRtspGateway.NotRegistered`. Nothing else in `src` changes except doc text (FR-005).
- **FR-002** Every test double drops the member (ten, plan §2); a double's counter that only that
  member incremented (`PerPathCalls`, two stubs) goes with it. No double gains anything.
- **FR-003** Before the removal, four new facts (C1-C4, plan §4) pin the spec-331 properties through
  `GetAllPathsHealthAsync`; observed green on develop, then unmodified by the removal commit.
- **FR-004 (owner-authorised deletion set; nothing outside it may be deleted or edited)**:
  - **D1** `MediaMtxProbeLogVolumeTests.A_fab_scale_sweep_of_healthy_streams_writes_no_information_record` (Theory, 2 cases) - drives the probe 250 times; replaced by C1.
  - **D2** `MediaMtxProbeLogVolumeTests.A_probe_answered_not_ready_or_not_found_writes_no_information_record` (Theory, 2 cases: 200 not-ready, 404) - replaced by C2 for not-ready; the 404 case has no twin (§3).
  - **D3** `MediaMtxProbeLogVolumeTests.A_probe_that_needs_retries_still_logs_them_at_warning_and_its_failure_at_error` - replaced by C3.
  - **D4** `MediaMtxProbeLogVolumeTests.An_environment_override_brings_the_probe_exchange_back_without_a_rebuild` - replaced by C4.
  - **D5** `MediaMtxRtspGatewayReasonPhraseTests.A_refused_single_path_health_probe_carries_no_reason_phrase` (spec 415 R9) - its subject is the removed `/v3/paths/get` site; listed for deletion by spec 415 plan §2 and in the orchestrator's authorisation. Its twin R7 (`A_refused_health_list_page_carries_no_reason_phrase`) stays.
  - **D6** the assertion `sweep.PerPathCalls.ShouldBe(0, ...)` in `StreamHealthWatcherScopeTests.A_sweep_of_250_cameras_issues_exactly_one_batch_call` (`:78-81`). The fact and its other assertions stay.
  - **D7** the assertion `gateway.PerPathCalls.ShouldBe(0)` in `StreamHealthWatcherAbsenceHoldTests.A_sweep_with_a_held_camera_makes_one_batch_call_and_no_per_path_call` (`:162`). The fact, its name and `BatchCalls.ShouldBe(2)` stay (the name stays true: a per-path call is now unwritable).
  Each exists only because the member exists: D1-D5 call it, D6-D7 count calls to it.
- **FR-005** Doc text naming the removed member is reworded to the behaviour it meant:
  `IRtspGateway.cs:6` (stale ADR-0061 citation), `:50-51`, `:68-70`;
  `StreamHealthWatcherScopeTests.cs:31-35` (class doc) and `:296-302` (stub doc, `cref` to the
  removed `PerPathCalls`); `ProvisionRetireRaceIntegrationTests.cs:49-50` and
  `StreamRepositoryVersionAssertionIntegrationTests.cs:100-102` (point at `StreamHealthWatcher`'s own
  "path not registered", `StreamHealthWatcher.cs:150`). Comments only; no constant or assertion
  changes (`PathNotRegistered` / `PathNotReady` stay correct, spec 415 plan §2).
- **FR-006** No behaviour change: every test outside FR-004 passes, and is unmodified except the
  mechanical member/counter/doc removals of FR-002/FR-005 (plan §3 lists each with its reason).

## 5. Non-goals

The `HealthSweepFailed` throttle and a byte limit on one page body (owner: undecided, out); the
reason phrase outside the MediaMTX gateway (spec 415 §5); a read deadline / `MaxListPages` changes;
renaming any surviving fact; ScenarioSimulator's own `/v3/paths/get` client.

## 6. Assumptions

- **[ASSUMPTION] B1.** The orchestrator's brief lists R9 (D5) as authorised "if listed"; it is listed
  in spec 415 plan §2's table as deleted with the method. The owner comment names the four spec-331
  facts and the two assertions; D5 is the same kind (a test whose only subject is the removed
  member). If the owner reads the comment as excluding R9, the change cannot compile without it, so
  the lane would block rather than keep it.
- **[ASSUMPTION] B2.** Helpers in `MediaMtxProbeLogVolumeTests` left unused by D1-D4 (e.g.
  `RandomPath`) are removed with them; helpers C1-C4 or the surviving
  `Another_client_in_the_same_service_still_logs_its_exchanges_at_information` use stay byte-identical.

## 7. Success criteria

- **SC-1** `grep -rn GetPathHealthAsync src tests` is empty; `grep -rn "PerPathCalls\|NotRegistered = " src/StreamDistribution tests/StreamDistribution.*` is empty.
- **SC-2** Release build of the solution clean (no new warning, no suppression).
- **SC-3** C1-C4 green on develop before the removal commit; `git diff <C-commit>..HEAD -- MediaMtxProbeLogVolumeTests.cs` shows deletions only (D1-D4 and B2 helpers).
- **SC-4** `git diff <C-commit>..HEAD -- tests` touches only the files of plan §2, and within them only FR-002/FR-004/FR-005 lines.
- **SC-5** StreamDistribution.Infrastructure.Tests, StreamDistribution.Application.Tests and Architecture.Tests green; Integration.Tests builds (CI runs it).
