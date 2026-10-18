# Tasks 409: A non-object MediaMTX page fails the read as an invalid response, and the source repair's whole-read Warnings repeat at most every 10 minutes

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3013 (feature-level;
items 2 and 3; no per-task issues).

**Phase 4a colour: RED** (both stories behaviour-changing). Every R-test compiles against today's code
and must be seen **failing on assertions** against unchanged production code, except **R10** and
**R15**, which need new surface: they are written in the working tree, their compile error is
quoted, and they land with the fix (spec 388 R4 precedent). Guards G1-G5 (plan §6) and every test
marked **guard** below pass today and must keep passing. Any other new test arriving green **is** a
phase-4 failure.

**Engineer:** backend-engineer (T010-T013). **Test-writer:** T001-T008. **Adversarial pass:**
recommended (T020; a shared singleton and a new exception path). **Security review:** recommended,
light (T021; new exception messages built from a dependency's response, FR-104).

**Docker:** everything but T008 (R18) runs without it. R18 boots the Aspire fixture; CI is
authoritative if Docker is unavailable locally. Keep local builds to the touched test projects.

**Commands**

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~Gateways.MediaMtxRtspGateway|FullyQualifiedName~Gateways.MediaMtxSourceRepairOverRealGateway|FullyQualifiedName~HealthWatcher.StreamHealthWatcherCutShortRead|FullyQualifiedName~Reconciler.MediaMtxSourceRepairSweep|FullyQualifiedName~WarningRepeatGate"
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~StreamDistributionRegistrationCharacterisationTests"
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~StreamDistribution.MediaMtxSourceRepairLogVolumeIntegrationTests"
```

**Forbidden in every task:** any existing assertion other than C1-C3 (plan §6), edited **only** as
plan §6 describes and named in the PR body; an edit to anything else is a **BLOCK**. Also forbidden:
`IRtspGateway.cs`, `MediaMtxSourceRepair.cs`, `SourceRepairFailureLog.cs`, `StreamHealthWatcher.cs`,
`MediaMtxReconciler.cs`, `IStreamSourceSnapshot.cs`, `StreamSourceSnapshot.cs`; every **existing**
`Log.cs` template (three are added, none reworded); `ListConfiguredPathsAsync`'s Warnings and
`HealthSweepFailed` (FR-209); a body-size limit, a read deadline, `MaxListPages` (spec §5);
`Shared.Contracts`, other contexts, `AppHost`; ADRs, the constitution, CLAUDE.md; suppressions,
thresholds, `.editorconfig`, analyzer configs; a `static` mutable field; `FakeTimeProvider` (ADR-0052:
use the hand-written `ManualTimeProvider`).

**New test classes**: unit classes need no shard-filter entry. R18 goes in the **existing**
integration class, so no `ci-shards/shard-N.filter` change is needed; if a new integration class is
created anyway it **must** be added to a shard filter or CI fails deterministically.

---

## Phase 1: Tests first (test-writer)

### US1 — malformed pages

- **T001 [P] [US1] R1-R5** new `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayPageShapeTests.cs`,
  using the `PagesHandler` stub pattern of `MediaMtxRtspGatewayListReadersCharacterisationTests`
  (copy it privately, as each gateway test class does) and the two-argument gateway constructor.
  Each theory is parameterised over the reader (`health` → `GetAllPathsHealthAsync`, `configured` →
  `ListConfiguredPathsAsync`, `sources` → `ListConfiguredPathSourcesAsync`).
  - **R1** `A_page_body_that_is_not_a_json_object_fails_the_read_as_an_invalid_response`: bodies
    `null`, `[]`, `5`, `"x"`. Assert `HttpRequestException` with `HttpRequestError ==
    InvalidResponse`, `RequestedPages == [0]`, no gateway entry ≥ Warning. Red today:
    `InvalidOperationException`.
  - **R2** `An_items_element_that_is_not_a_json_object_fails_the_read_as_an_invalid_response`:
    `{"pageCount":1,"items":[<e>]}` for `e` in `1`, `null`, `"cam-x"`, `[]`. Same assertions. Red today.
  - **R3** `A_malformed_second_page_fails_the_whole_read`: page 0 well-formed with `pageCount` 2 and
    one canonical item; page 1 is `[]`. Assert the typed exception and `RequestedPages == [0, 1]`.
    Red today.
  - **R4** `A_body_that_is_not_json_fails_the_read_as_an_invalid_response_wrapping_the_parse_error`:
    bodies `<html>proxy</html>` and the empty string. Assert the typed exception and
    `InnerException is JsonException`. Red today: `JsonException` escapes. (FR-103, [ASSUMPTION A3].)
  - **R5** `A_shape_failure_carries_no_part_of_the_body`: body `["rtsp://admin:SECRET@10.0.8.1/x"]`,
    an element `"rtsp://admin:SECRET@10.0.8.1/x"`, and the unparseable body
    `rtsp://admin:SECRET@10.0.8.1/x`. Assert the typed exception (red today) **and** that no
    `Message` in the exception chain contains `SECRET`.
  - **Guards (pass today, must keep passing)** in the same class:
    `An_object_page_without_items_still_ends_the_configured_reads_normally` (configured, sources) and
    `An_object_element_without_a_string_name_is_still_skipped` (all three). Declared guards, not red.
- **T002 [P] [US1] R6** in the **existing** `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherCutShortReadTests.cs`:
  `A_malformed_page_is_a_lost_tick_logged_as_a_health_sweep_failure`: the class's real-gateway
  arrangement with `/v3/paths/list` answering `[]`. Assert `DispatchAsync` does not throw, no health
  command is handled, exactly one `HealthSweepFailed` Warning whose `Exception` is
  `HttpRequestException`, nothing at Error. Red today: `InvalidOperationException` escapes
  `DispatchAsync`.
- **T003 [P] [US1] R7** in the **existing** `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxSourceRepairOverRealGatewayTests.cs`:
  `A_malformed_config_list_fails_the_repair_pass_with_the_typed_exception`: the config list answers
  `[]`; `RepairAsync` throws `HttpRequestException` with `InvalidResponse`; no PATCH was sent. Red today.
- **T004 [US1] C1** apply plan §6 C1 to `MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests`.
  Run it against **unchanged** production code: it fails (quote it). Not `[P]` with T001 only by
  convention; it owns a different file and may run alongside.

### US2 — repeating Warnings

- **T005 [P] [US2] R10 (compile-red)** new `tests/StreamDistribution.Infrastructure.Tests/WarningRepeatGateTests.cs`
  over `WarningRepeatGate<int>` (or a local `record struct`) and `ManualTimeProvider`:
  first → true; same at +9m59s → false; same at +10m00s → true; different outcome → true at once;
  `Clear()` then same → true; `StepWallClock(+1 h)` alone → false (monotonic). Quote the compile
  error; it lands with T012.
- **T006 [US2] R11-R15** in the **existing** `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests.cs`:
  - **R11 = C2** apply plan §6 C2 (rename; 3 → 1 Warning; two Debug entries containing "repeats at
    most every 10 minutes"). Red today (3 Warnings, no Debug).
  - **R12** `A_persistently_reached_page_cap_warns_once_then_logs_debug`: every page
    `pageCount 2147483647` + one canonical item; three reads on one gateway. Assert 1 Warning with
    event `MediaMtxConfiguredSourcesPageCapReached`. Red today (3).
  - **R13** `A_complete_read_resets_the_unreadable_page_count_warning`: reads ending unreadable,
    unreadable, complete, unreadable, unreadable on one gateway. Assert 2 Warnings. Red today (4).
    **Guard** alongside it: `A_change_from_unreadable_to_page_cap_warns_at_once` (2 Warnings for
    unreadable then cap) — passes today, must keep passing.
  - **R14** `Gateways_resolved_from_the_registration_in_two_scopes_share_one_warning_state`: a
    `ServiceCollection` with `Configure<MediaMtxOptions>` (`ManagementUrl = "http://mediamtx.test"`),
    `Configure<MediaMtxApiOptions>` (a non-empty `Secret`), `AddSingleton(TimeProvider.System)`, a
    `CapturingLogger<MediaMtxRtspGateway>` registered as `ILogger<MediaMtxRtspGateway>`, then
    `AddMediaMtxGateway().ConfigurePrimaryHttpMessageHandler(() => handler)`; resolve `IRtspGateway`
    in two scopes; `pageCount 1.5` on every read; one read per gateway. Assert exactly 1 Warning.
    Red today (2). Compiles today (`AddMediaMtxGateway` is internal, visible to this project).
  - **R15 (compile-red)** `The_unreadable_page_count_warning_repeats_after_ten_minutes`: three-argument
    constructor with `new MediaMtxConfiguredSourcesReadWarnings(manualTime)`; reads at t=0, +9m59s,
    +10m00s. Assert Warning, Debug, Warning. Quote the compile error; lands with T011.
- **T007 [P] [US2] R16-R17** in the **existing** `tests/StreamDistribution.Infrastructure.Tests/Reconciler/MediaMtxSourceRepairSweepTests.cs`:
  - **R16** `A_pass_that_keeps_failing_the_same_way_warns_once_then_logs_debug`: `ScopeProbe` (throws
    `InvalidOperationException` every pass), 50 ms interval, wait for ≥ 3 creations. Assert exactly 1
    `SourceRepairSweepFailed` Warning and ≥ 2 Debug entries containing `InvalidOperationException` and
    "repeats at most every 10 minutes". Red today (≥ 3 Warnings).
  - **R17** `A_change_of_failure_warns_at_once`: a private scope-factory fake throwing A, A, B, B
    (`InvalidOperationException`, then `HttpRequestException(…, statusCode: ServiceUnavailable)`),
    then blocking or repeating B. Assert exactly 2 Warnings after 4 passes, the second carrying the
    `HttpRequestException`. Red today (4).
- **T008 [US2] R18 (integration)** in the **existing**
  `tests/Integration.Tests/StreamDistribution/MediaMtxSourceRepairLogVolumeIntegrationTests.cs`:
  `A_successful_pass_resets_the_sweep_failure_warning`: a gateway double whose
  `ListConfiguredPathSourcesAsync` throws `HttpRequestException(503)` on passes 1, 2 and 4 and returns
  an empty dictionary on pass 3 (extend the private `ListFailingGateway` with a per-call sequence,
  defaulting to today's always-throw, nothing else). Real sweep, real `StreamSourceSnapshot`. Assert
  exactly 2 `SourceRepairSweepFailed` Warnings after 4 passes. Red today (3).

**Gate before Phase 2:** the verbatim test output, showing R1-R7, R11-R14, R16-R18 and C1 failing on
assertions, R10/R15 failing to compile (quoted, then set aside), and every guard plus every existing
test passing, is handed to the engineer.

## Phase 2: Implementation (backend-engineer)

- **T010 [US1]** `MediaMtxRtspGateway.cs`: plan §3.1 (body kind, unparseable JSON, element kind).
  Makes R1-R7 and C1 green.
- **T011 [US2]** `MediaMtxConfiguredSourcesReadWarnings.cs` (new), `MediaMtxRtspGateway.cs` (three-argument
  `[ActivatorUtilitiesConstructor]` constructor, the sources switch), `Log.cs` (two page Debug
  templates), `StreamDistributionInfrastructureModule.cs` (the singleton), C3 in
  `StreamDistributionRegistrationCharacterisationTests.cs`. Depends on T012's gate type: land T012
  first within the same commit or the commit before.
- **T012 [US2]** `WarningRepeatGate.cs` (new, plan §2); `MediaMtxSourceRepairSweep.cs` (plan §3.3);
  `Log.cs` (`SourceRepairSweepStillFailing` Debug template). Brings R10 and R15 in from the working tree.
- **T013** `dotnet format --verify-no-changes` on the touched files; Release build clean
  (`var`, collection expressions, no `_` fields, IDE0005).

**Commit order** (each commit builds on its own, ADR-0087; red/green pairs adjacent, ADR-0139):
1. `test(3013): …` US1 red tests (T001-T004).
2. `fix(3013): a non-object MediaMTX page fails the read as an invalid response` (T010).
3. `test(3013): …` US2 red tests that compile (T006 except R15, T007, T008).
4. `fix(3013): the source repair's whole-read Warnings repeat at most every 10 minutes`
   (T011 + T012 + R10 + R15 + C3).

US1 and US2 are independent; if the orchestrator prefers two PRs, cut after commit 2.

**Gate:** the three commands green; every other test in the touched projects green unmodified;
format and Release build clean.

## Phase 3: Verify / QA

- **T020 (recommended)** test-adversary: two scopes reading concurrently through the shared
  singleton (no lost Warning, no exception); a malformed page on page 999; an element that is an
  object in one page and a number in the next; a sweep failure whose `HttpRequestException` has a
  null `StatusCode` vs. a 503 (different outcomes).
- **T021 (recommended)** security-reviewer over FR-104: no response content in the new messages or
  their inner exceptions; `[ActivatorUtilitiesConstructor]` does not change the client's credentials
  or handler chain (C3 is the only registration diff; `RetryEveryMethod` marker test green).
- **T022** `/verify`: run the unit filters; cite R18 from CI (or a local Aspire run); quote one
  `HealthSweepFailed` line caused by a `[]` page (R6 output) and one sweep Warning followed by its
  Debug repeat (R16 output). No latency citation (spec §10: N/A).
- **PR body**: name C1-C3 with plan §6's reasons; disclose [ASSUMPTION A1-A4]; list spec §5's
  non-goals. Use **`Refs #3013`** unless the orchestrator has first filed the follow-up for the
  non-goals (body-size bound, `HealthSweepFailed` throttle, `ParsePathHealth` wrong-kind fields) —
  then `Closes #3013`, since items 1-3 are all delivered.

## Dependencies

- T001, T002, T003, T005, T007 `[P]`: disjoint files.
- T004 owns the fold characterisation file; may run alongside T001-T003.
- T006 owns the bounds-adversarial file (R11-R15 sequential within it). T008 owns the integration file.
- Phase 2 blocks on the Phase 1 gate. T010 is independent of T011/T012. T012's gate type precedes T011.
- Foundational: none outside this context; no Shared.Kernel / Contracts / AppHost task.
