# Tasks 388: The source repair logs a failing path on change, bounds its list read, and reports its own startup failure

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2994 (feature-level; on
Project #13; no per-task issues). **Split out**: #3010 (on Project #13, not `agent:ready`).

**Phase 4a colour: RED** (behaviour-changing). R1-R3 compile against today's code and must be seen
**failing on assertions** on unchanged production code. R4 needs the new
`MediaMtxSourceRepair(ILogger, TimeProvider)` constructor. It is written in the working tree, its
compile error is quoted, and it lands with the fix (spec 378 precedent). Tests marked **guard**
pass today and must keep passing. They pin the part of today's behaviour the decision keeps, and
they are declared here so their arriving green is not mistaken for a phase-4 failure. Every other
new test arriving green **is** a phase-4 failure.

**Adversarial pass: required** (T006, outage and flood). **Security review: required** (T021,
stream attribution and the control-API budget).

**Engineer:** backend-engineer (T010-T012). **Test-writer:** T001-T004. **Test-adversary:** T006.

**Docker:** R1, R2 and R4 need none. R3 boots the Aspire fixture, so CI is authoritative if Docker
is down locally.

**Forbidden in every task:** any existing assertion (plan §8.1 lists them all, and none changes; an
edit is a **BLOCK** to report, not an adjustment). Also forbidden: `ListConfiguredPathsAsync`,
`GetAllPathsHealthAsync`, `AddPageToPaths`, `AddPageToHealth` (#3010); `IRtspGateway.cs`;
`StreamDistributionInfrastructureModule.cs`; `SourceRepairSweepFailed`, `ReconcilerStartupPassFailed`
and every other existing `Log.cs` template; `Shared.Contracts`; any other context; `AppHost`; ADRs,
the constitution and CLAUDE.md; suppressions, thresholds, `.editorconfig` and analyzer configs; a
`static` mutable field; `IMemoryCache`; `FakeTimeProvider` (ADR-0052: hand-written fakes).

**Existing test files that may be edited, additively only:** `tests/StreamDistribution.Infrastructure.Tests/Fakes/SourceRepairFakes.cs`
(add `StopFailingPatchesFor(MediaMtxPath)` to `SourceRepairGateway`; nothing else changes) and
`tests/Integration.Tests/ci-shards/shard-2.filter` (append one class clause).

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 (all five `Log.cs` messages, plus `SourceRepairFailureLog`) blocks T011 and
T012. After it, T011 (gateway) and T012 (reconciler) own disjoint files and are `[P]`.

---

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1] R1: new `tests/StreamDistribution.Infrastructure.Tests/Reconciler/MediaMtxSourceRepairLogVolumeTests.cs`.
  Uses the existing `SourceRepairGateway`, `SourceRepairSnapshot` and `CapturingLogger<MediaMtxReconciler>`.
  **One** `MediaMtxSourceRepair` instance (`new(logger)`, the existing constructor) is **reused
  across every pass in a fact**. Passes run back to back in real time, well inside 10 minutes.
  Count Warnings per path. Facts:
  (a) `A_permanently_refused_patch_warns_once_across_three_passes_and_logs_debug_in_between`: 400 on
  every pass. Exactly 1 Warning, with `Exception` the thrown instance and the existing message text
  `Reconciler failed to repair MediaMTX path {path}; the next sweep retries.`, plus ≥2 Debug entries
  containing the path and `HttpRequestException 400`. **Red** (3 Warnings).
  (b) `A_change_of_status_warns_again_at_once`: 400, 401, 401. Gives 2 Warnings, the second carrying
  the 401 exception. **Red** (3).
  (c) `A_change_of_exception_type_warns_again_at_once`: `HttpRequestException` 400, then
  `TimeoutRejectedException` twice. Gives 2. **Red** (3).
  (d) `A_repair_that_succeeds_resets_the_path`: 400, success (`StopFailingPatchesFor` plus
  re-diverge via `Configure`), 400, 400. Gives 2. **Red** (3).
  (e) `A_404_from_a_retire_race_is_path_gone_at_debug_never_a_warning`: the existing retire-race
  setup (`MediaMtxSourceRepairAdversarialTests` line 25ff.). No Warning; exactly one Debug containing
  the path and `path gone`; PathB repaired. **Red** (1 Warning).
  (f) `A_path_pruned_after_leaving_the_database_warns_again_when_it_returns`: 400 (Warn); retire for
  one pass; re-commit, diverge, 400 (Warn); 400 (Debug). Gives 2. **Red** (3).
  (g) `A_cancelled_pass_leaves_the_state_untouched`: 400 (Warn); pass with caller cancelled mid-PATCH
  throws OCE and logs nothing ≥ Warning; 400. Gives 1 Warning in total. **Red** (2).
  (h) **guard** `No_debug_line_carries_the_source`: persisted `rtsp://cam/s?token=SECRET`, 400 over
  three passes. No entry's message contains `SECRET`.
  Run `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxSourceRepairLogVolumeTests"`.
  Return the verbatim output: (a)-(g) fail on assertions; (h) passes.
- [x] **T002** [P] [US2] R2: new `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayConfiguredSourcesBoundsTests.cs`.
  It uses the stub-handler pattern of `MediaMtxRtspGatewayConfiguredSourcesTests`, with a
  `CapturingLogger<MediaMtxRtspGateway>`. The handler **throws `InvalidOperationException("unbounded
  read")` on its 1,101st request**, so today's loop fails fast instead of hanging. Facts:
  (a) `An_empty_page_ends_the_read_whatever_page_count_claims`: page 0 has `pageCount=2147483647`
  and 1 canonical item; every later page has `pageCount=2147483647` **and** `items: []`. The
  `pageCount` on the later pages is load-bearing: without it today's code stops at page 1 and the
  fact would arrive green. Requested pages `[0,1]`; 1 source returned; no Warning. **Red.**
  (b) `The_read_stops_at_one_thousand_pages_and_warns_once`: every page has
  `pageCount=2147483647` and 1 distinct canonical item. 1,000 requests (pages 0..999); 1,000
  sources; exactly one Warning, whose message contains `1000`. **Red.**
  (c) `[Theory] An_unreadable_page_count_ends_the_read_with_one_warning_not_an_exception`, with
  `1.5`, `3e9`, `2147483648`, `-2147483649` and `1e400` written raw into the JSON. No throw;
  requested `[0]`; page 0's item returned; exactly one Warning, whose message does not contain the
  raw token. **Red** (`FormatException`).
  (d) **guard** `A_page_of_only_non_canonical_paths_is_not_an_empty_page`: page 0 has
  `pageCount=2` and only `manual-x`; page 1 has a canonical item. Requested `[0,1]`; 1 source.
  (e) **guard** `[Theory] A_page_count_that_is_not_a_number_still_reads_one_page_without_a_warning`,
  with `"5"` and `null`. Requested `[0]`; no Warning.
  Run `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayConfiguredSourcesBoundsTests"`.
  Return the verbatim output: (a)-(c) fail; (d) and (e) pass.
- [x] **T003** [P] [US1][US3] R3: new `tests/Integration.Tests/StreamDistribution/MediaMtxSourceRepairLogVolumeIntegrationTests.cs`
  (`[Collection(AspireCollection.Name)]`; seed and harness copied from
  `MediaMtxReconcilerPassCharacterisationTests`: `ResetStreamDistributionAsync`, a real Npgsql
  `AddDbContextFactory`, an in-file recording gateway and an `EventLogger`). Append
  `|FullyQualifiedName~SmartSentinelEye.Integration.Tests.StreamDistribution.MediaMtxSourceRepairLogVolumeIntegrationTests.`
  to `tests/Integration.Tests/ci-shards/shard-2.filter`. Facts:
  (a) `The_sweep_warns_once_for_a_patch_mediamtx_keeps_refusing`: one seeded live stream at
  `rtsp://10.0.5.2/h264`. The gateway reports `rtsp://10.0.5.9/h264` and answers every PATCH with
  `HttpRequestException(..., HttpStatusCode.BadRequest)`. Run the real `MediaMtxSourceRepairSweep`
  (`Interval = 50 ms`, `TimeProvider.System`, a lifetime whose `ApplicationStarted` fires) until the
  gateway has seen ≥4 PATCHes (bounded at 30 s), then stop. Exactly 1 Warning with the existing
  template, plus ≥3 Debug lines. **Red** (≥4 Warnings).
  (b) `A_failed_repair_list_at_startup_is_logged_as_its_own_failure_after_the_summary`: no seed;
  `ListConfiguredPathsAsync` returns `[]`; `ListConfiguredPathSourcesAsync` throws
  `HttpRequestException("list-boom")`. `ReconcileOnceAsync` does not throw. The events end with
  `log Information MediaMtxReconciler startup pass complete. Configured=0, expected=0, removed=0, readded=0.`
  followed by `log Warning MediaMtxReconciler source repair after the startup pass failed; the periodic sweep retries. [HttpRequestException: list-boom]`.
  **Red** (it throws).
  (c) **guard** `A_shutdown_during_the_startup_repair_still_propagates`: the gateway cancels the
  passed CTS and throws `OperationCanceledException(token)` from `ListConfiguredPathSourcesAsync`.
  `ReconcileOnceAsync` throws OCE, and no event at Warning appears.
  Run `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~MediaMtxSourceRepairLogVolumeIntegrationTests"`
  (Docker). Return the verbatim output. If Docker is down, the orchestrator pushes the Phase 1
  commit alone and quotes the job log's assertion failure in the PR, or the PR says it was not
  observed.
  **Commit T001-T003:** `test(2994): source-repair log volume and bounded list read (red)` (de5db5aa).
- [x] **T004** [US1] R4 (compile-red): new `tests/StreamDistribution.Infrastructure.Tests/Fakes/ManualTimeProvider.cs`
  (minimal, hand-written; overrides `GetUtcNow`, `GetTimestamp` and `TimestampFrequency`
  (= `TimeSpan.TicksPerSecond`); `Advance(TimeSpan)` moves both; `StepWallClock(TimeSpan)` moves
  only `GetUtcNow`; no timers. Copy the doc-comment reasoning of
  `tests/Identity.Infrastructure.Tests/Fakes/ManualTimeProvider.cs`). Also new
  `tests/StreamDistribution.Infrastructure.Tests/Reconciler/MediaMtxSourceRepairRelogIntervalTests.cs`
  using `new MediaMtxSourceRepair(logger, clock)`:
  (a) a 400 at t0 (Warn), t0+9:59.999 (Debug), t0+10:00 (Warn: the boundary is inclusive),
  t0+15:00 (Debug), t0+20:00 (Warn). Exactly 3 Warnings, each with its exception.
  (b) the wall clock steps −1 h while the timestamp advances 10 min, which Warns.
  (c) the wall clock steps +1 h while the timestamp advances 1 s, which is Debug only.
  (d) a Debug at t0+9 min does not move the window: t0+10:00 Warns.
  Run the same filter command and return the verbatim compile error (CS1729 on the two-argument
  constructor). **Do not commit**; hand to T010.

## Phase 1b: Adversarial (test-adversary)

- [x] **T006** [US1][US2] Outage and flood. Hunt with the fakes and with the real gateway over a
  stub handler, using new files under `tests/StreamDistribution.Infrastructure.Tests/Reconciler/`
  or `Gateways/` only:
  - a control-API outage starting with 250 diverged paths, all 401: exactly 250 Warnings on pass 1
    and 0 on passes 2..N inside 10 minutes; recovery then failure again Warns;
  - flapping outcomes (400/timeout alternating), to confirm one Warning per change and nothing
    more;
  - a 404 on the **re-patch** (spec 378 FR-003's second PATCH);
  - a list read cut short by an unreadable `pageCount` followed by a full read;
  - cap boundaries: `pageCount` 999, 1000 and 1001 with full pages;
  - `items` absent, `null` or an object;
  - duplicate names across pages;
  - a 10,000-digit `pageCount` literal (no raw token in the log, no throw);
  - 10,000 distinct failing paths then all retired, to confirm none Warns as "suppressed" when it
    returns (L4).
  Report findings. Tests are in `d3cb328f` (`test(2994): adversarial cases for the source-repair log volume and bounded list read`).

## Phase 2: Green (backend-engineer; may not edit any test written above)

- [x] **T010** [US1] Foundational: `Infrastructure/Log.cs` gets all five messages (plan §5, exact
  templates and levels). New `Reconciler/SourceRepairFailureLog.cs` (plan §2/§3; monotonic
  `GetTimestamp`/`GetElapsedTime`; private `Outcome` record struct; retain-only-failed on
  `PassCompleted`). `MediaMtxSourceRepair`: `(ILogger, TimeProvider)` primary constructor plus
  `(ILogger) : this(logger, TimeProvider.System)`; the catch calls `Failed`; success calls
  `Succeeded`; `PassCompleted()` is the last statement of `RepairAsync`; the catch filter is
  unchanged. `MediaMtxSourceRepairSweep`: a `repair` field built from `timeProvider`, reused by
  `RunPassAsync`; the constructor is unchanged.
- [x] **T011** [P] [US2] `Gateways/MediaMtxRtspGateway.cs`: `ListConfiguredPathSourcesAsync` and
  `AddPageToSources` only (plan §4; `private const int MaxConfiguredSourcePages = 1000`;
  `TryGetInt32`). Confirm the diff shows no hunk in `ListConfiguredPathsAsync`,
  `GetAllPathsHealthAsync`, `AddPageToPaths` or `AddPageToHealth`.
- [x] **T012** [P] [US3] `Reconciler/MediaMtxReconciler.cs`: try/catch around the trailing repair
  (plan §3), `ReconcilerStartupSourceRepairFailed`. Nothing else in the file changes.
  **Commits (three, one per task):** T010 `fix(2994): the source repair warns on a path's failure once per change or ten minutes` (286bc9e5); T011 `fix(2994): the configured-source list read stops on an empty page and at 1,000 pages` (811f11e3); T012 `fix(2994): a failed source repair after the startup pass is logged as its own failure` (b9c92717).

## Phase 2b: Review fixups

- [x] **T013** [US1] Red: `MediaMtxSourceRepairCancelledPassLeakFindingTests` and
  `MediaMtxSourceRepairTransportErrorOutcomeTests` fail 'should be 2 but was 1' on the unfixed
  code. Commit `test(2994): a leaked pass state and a changed transport error must not stay silent (red)` (8f894058).
- [x] **T014** [US1] Fix: `SourceRepairFailureLog.PassStarting()` (called first in `RepairAsync`)
  clears `failedThisPass`; `PassCompleted` only prunes; the `Outcome` gains `HttpRequestError`;
  `RepairSourcesAfterStartupAsync` extracted from `ReconcileOnceAsync`. `Succeeded` is kept
  (pinned by `A_success_before_a_cancelled_pass_still_resets_the_path`). Commit
  `fix(2994): a pass that was cut short no longer leaks into the next, and a changed transport error warns` (874c9aa1).

## Phase 3: Verify

- [ ] **T020** Local: `dotnet build -c Release` clean (no new warnings; S-rule metric advisories
  noted, ADR-0084). Then run each of these:
  - `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release`, which runs R1, R2, R4,
    T006 and the whole unmodified plan §8.1 set (`MediaMtxSourceRepair*`, `MediaMtxRtspGateway*`,
    `MediaMtxReconcilerStartupOrderingTests`), all green;
  - `dotnet test tests/StreamDistribution.Application.Tests -c Release`;
  - `dotnet test tests/Architecture.Tests -c Release` (registration characterisation, primitive
    boundary, handler deconstruction, integration selection);
  - `dotnet test tests/Integration.Tests -c Release --filter "Category=FixtureLogic"`.

  Unmodified-test proof: `git diff origin/develop --stat -- tests/` lists only new files,
  `SourceRepairFakes.cs` (additive) and `shard-2.filter` (one clause), and
  `git diff origin/develop -- tests/ | grep '^-' | grep -v '^---'` prints nothing. CI is
  authoritative for R3 and for C7, `MediaMtxReconcilerIntegrationTests`,
  `ProvisionRetireRaceIntegrationTests`, `StreamHealthTransitionTests` and the
  `integration-shard-coverage` job. Coverage gates hold (ADR-0065). Phase 5 note: on a running
  stack, repoint a stream to an `RTSP://` upper-case source and read `stream-distribution`'s
  structured logs: one Warning, then Debug only.
- [ ] **T021** Phase 6: `/code-review` + `security-reviewer`. Check stream attribution (the repair
  still writes only the database's value; a Warning suppressed for up to 10 minutes after a first
  Warning; no URL, raw token or exception message in a new template), the control-API budget
  (stop-on-empty, the 1,000-page cap and its worst case against the 2,000/min `api` partition,
  ADR-0171), and boundedness (no growth keyed on input from outside the database).

## Dependencies

T001, T002, T003 [P] → commit 1. T004, T006 (uncommitted) → T010 → (T011 [P], T012 [P]) → commit 2
→ T020 → T021. T001 and T006 may both want `SourceRepairFakes.cs`. Only T001 edits it (additive),
and T006 consumes it.
