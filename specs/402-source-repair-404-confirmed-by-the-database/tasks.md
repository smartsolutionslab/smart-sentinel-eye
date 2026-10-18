# Tasks 402: A 404 on the source-repair PATCH is "path gone" only when the database agrees

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3013 (feature-level;
item 1 only; no per-task issues).

**Phase 4a colour: RED** (behaviour-changing). R1-R5, R7 and R8 compile against today's code (no new
public surface: the tests drive `MediaMtxSourceRepair.RepairAsync` with the existing fakes) and must
be seen **failing on assertions** against unchanged production code; quote the output. R6 is a
declared **guard**: it passes today and must keep passing. Any other new test arriving green **is**
a phase-4 failure.

**Engineer:** backend-engineer (T010-T011). **Test-writer:** T001-T005. **Adversarial pass:**
optional (T020); the change is one branch of one catch. **Security review:** not required (no auth,
input, secret or log-content change; the Warning template already redacts via spec 373).

**Docker:** T001-T004 need none. T005 (R8) boots the Aspire fixture; CI is authoritative if Docker
is unavailable locally. Keep local builds to the one test project.

**Commands**

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~Reconciler.MediaMtxSourceRepair"
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~StreamDistribution.MediaMtxSourceRepairLogVolumeIntegrationTests"
```

**Forbidden in every task:** any existing assertion other than C1-C3 (plan §6), which are edited
**only** as plan §6 describes and are named in the PR body; an edit to anything else is a **BLOCK**.
Also forbidden: `IStreamSourceSnapshot.cs`, `StreamSourceSnapshot.cs`, `IRtspGateway.cs`,
`MediaMtxRtspGateway.cs`, `MediaMtxSourceRepairSweep.cs`, `StreamDistributionInfrastructureModule.cs`,
every `Log.cs` template; #3013 items 2 and 3 (`MediaMtxConfiguredSourcesPageCountUnreadable`,
`MediaMtxConfiguredSourcesPageCapReached`, `SourceRepairSweepFailed`, `AddPageToSources`);
`Shared.Contracts`; other contexts; `AppHost`; ADRs, the constitution, CLAUDE.md; suppressions,
thresholds, `.editorconfig`, analyzer configs; a `static` mutable field; `FakeTimeProvider` or any
mocking of the snapshot (ADR-0052: the hand-written fakes already have every hook needed).

**Existing test files that may be edited:** `SourceRepairFakes.cs` (additively, only if a hook is
missing; none is expected — `CommittedReadOverride`, `AfterCommittedReadCaptured`,
`BeforePatchApplied`, `Retire`, `Commit` cover every R-test);
`MediaMtxSourceRepairAdversarialTests.cs` (C1 only); `MediaMtxSourceRepairFloodAdversarialTests.cs`
(C2, C3 only, plus new facts); `MediaMtxSourceRepairLogVolumeTests.cs` (new facts only);
`MediaMtxSourceRepairLogVolumeIntegrationTests.cs` (new fact; its private `RefusingGateway` may gain
a status parameter defaulting to today's 400, nothing else).

---

## Phase 1: Tests first (test-writer)

All new unit facts go in **existing** classes (`MediaMtxSourceRepairLogVolumeTests`,
`MediaMtxSourceRepairFloodAdversarialTests`), so no shard-filter entry is needed. If a new class is
created anyway, unit tests have no shard filter, but an **integration** class must be added to
`tests/Integration.Tests/ci-shards/shard-N.filter` or CI fails deterministically.

- **T001 [P] [US1] R1, R3, R6** in `MediaMtxSourceRepairLogVolumeTests.cs` (one instance across
  passes, as that class does):
  - **R1** `A_persistent_404_for_a_live_stream_warns_once_and_then_logs_debug_until_the_interval`:
    `Diverge(PathA)`; `FailPatchesFor(PathA, Refusal(NotFound))`; 3 passes. Assert exactly one
    Warning for PathA with the failure template text and `Exception` the same 404 instance; ≥ 2
    Debug lines containing PathA and `"HttpRequestException 404"`; **no** entry containing
    `"path gone"`. Red today: 0 Warnings, 3 "path gone".
  - **R3** `A_404_for_a_live_stream_between_two_400s_is_a_change_of_outcome_each_time`: 400 → 404 →
    400, one pass each, path live throughout. Assert 3 Warnings. Red today: 2.
  - **R6 (guard)** `A_failure_that_is_not_a_404_makes_no_confirmation_read`: `Diverge(PathA)`;
    `FailPatchesFor(PathA, Refusal(BadRequest))`; 1 pass. Assert `snapshot.CommittedReads` is empty.
    **Passes today and must keep passing** (FR-002).
- **T002 [P] [US1] R2, R4, R7** in `MediaMtxSourceRepairFloodAdversarialTests.cs`:
  - **R2** `A_404_on_the_re_patch_for_a_live_stream_is_a_warning_not_path_gone`: the arrangement C2
    had before plan §6 changed it (`CommittedReadOverride` always `Some(newer)`; 404 on the second
    patch). Assert `repaired == 0`, 2 patches, exactly one Warning carrying the 404, no
    `"path gone"`. Red today.
  - **R4** `A_live_stream_404_warns_and_its_stream_retired_next_pass_logs_nothing_more`: pass 1
    404 on a live path → 1 Warning; `snapshot.Retire(path)`; pass 2 → still 1 Warning and no new
    entry naming the path. Red today (0 Warnings in pass 1).
  - **R7** `A_cancellation_during_the_404_confirmation_read_propagates`: 404 on the PATCH;
    `AfterCommittedReadCaptured` cancels a `CancellationTokenSource` whose token the pass runs with,
    and the snapshot read must observe it — if the fake does not throw on a cancelled token, use
    `CommittedReadOverride` to `cts.Cancel()` and then `throw new OperationCanceledException(cts.Token)`.
    Assert `RepairAsync` throws `OperationCanceledException` and no entry ≥ Warning. Red today: no
    read occurs, so nothing throws.
- **T003 [P] [US1] R5** in `MediaMtxSourceRepairFloodAdversarialTests.cs`:
  `A_confirmation_read_that_fails_is_logged_as_the_paths_failure_and_the_pass_continues`: two
  diverged paths; PathA's PATCH answers 404; `CommittedReadOverride` throws
  `InvalidOperationException` for PathA only. Assert `RepairAsync` returns (does not throw), PathB
  is repaired, exactly one Warning for PathA whose `Exception` is the `InvalidOperationException`,
  no `"path gone"`. Red today: no read, a "path gone" Debug line, no Warning.
- **T004 [US1]** Apply C1-C3 exactly as plan §6 states (C1: one assertion replaced; C2, C3:
  arrangement only). Run them against **unchanged** production code: C1 fails (no read today; quote
  it), C2 and C3 pass today (a retire race is gone today too) and must pass after. Depends on
  nothing; same files as T002/T003, so not `[P]` with them.
- **T005 [US1] R8 (integration)** in `MediaMtxSourceRepairLogVolumeIntegrationTests.cs`:
  `The_sweep_warns_once_for_a_404_on_a_live_stream`: `SeedAsync()` (live stream), the
  `RefusingGateway` answering 404, same sweep loop as the existing 400 fact. Assert the Warning
  list is exactly `[FailureWarning]` and no `"path gone"` entry. Proves the real
  `StreamSourceSnapshot` read returns Some for a live row. Red today. Depends on T001-T004 only for
  ordering of the red run.

**Gate before Phase 2:** the verbatim test output, showing R1-R5, R7, R8 and C1 failing on
assertions and R6, C2, C3 plus every existing test passing, is handed to the engineer.

## Phase 2: Implementation (backend-engineer)

- **T010 [US1]** `SourceRepairFailureLog.cs`: remove the 404 branch from `Failed`; add `Gone(path)`
  with that branch's body (plan §3.1).
- **T011 [US1]** `MediaMtxSourceRepair.cs`: route the `catch` at :96 through the 404 confirmation
  (plan §3.2, spec FR-001-FR-004). Depends on T010 (same feature, consecutive commit).
- Commit order: tests (red) commit, then T010+T011 in one `fix(3013)` commit, so every commit builds
  and the red/green pair is adjacent (ADR-0087 / ADR-0139).

**Gate:** the T001-T005 commands green, every other test in the two projects green unmodified,
`dotnet format --verify-no-changes` clean on the touched files, Release build clean.

## Phase 3: Verify / QA

- **T020 (optional)** test-adversary over the `MediaMtxSourceRepair` 404 branch, for example two
  paths that both answer 404 in one pass, one retired and one live (exactly one Warning, one
  "path gone", two confirmation reads).
- **T021** `/verify`: run the unit filter, cite R8 from CI (or local Aspire run), and quote one
  persistent-404 Warning line. No latency citation (spec §8: N/A).
- PR body: list C1-C3 with plan §6's reasons; disclose [ASSUMPTION A1] and [ASSUMPTION A2]; state
  items 2 and 3 of #3013 remain open (use `Refs #3013`, **not** a closing keyword — the issue stays
  open for them).

## Dependencies

T001, T002, T003 `[P]` with each other only if split by file: T001 owns
`MediaMtxSourceRepairLogVolumeTests.cs`; T002+T003+T004 share the two adversarial files and run
sequentially; T005 owns the integration file. T010 → T011. Phase 2 blocks on the Phase 1 gate.
