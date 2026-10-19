# Tasks 418: list reads stop at twenty pages and five seconds; a configured page without items warns

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3021 (feature-level;
already on Project #13; no per-task issues).

**Phase 4a colour: RED** (behaviour-changing, ADR-0139). Every red test below must be observed
failing on unchanged code (`398e61a0`) for the stated reason; a red test arriving green is a phase-4
failure (stop and report). Characterisation tests (listed under T004) must be observed **green** on
unchanged code and pass **unmodified** after. The verbatim outputs go in the PR body.

**Owner-authorised test edits.** The cap assertions (T001) and the one silence assertion (T002) are
edited because the owner changed the behaviour they pin (spec header). No other existing test file
is edited, and no assertion outside those listed is touched.

**Roles:** test-writer T001-T004 (tests only; runs them; returns verbatim output). backend-engineer
T010-T012 (may not edit anything under `tests/`). Phase 5 T030. Phase 6 `/code-review`
(backend-reviewer). No security review.

**Commit order (ADR-0087, every commit builds on its own; red/green pairs):**
red T001 → green T010 → red T002 → green T011 → red T003 → green T012. The test-writer may hand all
three red sets back at once, but each red set is committed immediately before its own fix. A single
red commit that leaves three suites failing across three fixes is not acceptable.

**Forbidden in every task:** a configuration option or `IOptions` for the cap or the deadline; a new
throttle, interval or gate class; editing `GetAllPathsHealthAsync`, `CompleteHealthOrThrow`,
`StreamHealthWatcher`, `MediaMtxReconciler`, `MediaMtxSourceRepair*`, `SourceRepairFailureLog`,
`WarningRepeatGate`, `ServiceDefaults`; adding timers to the existing
`Fakes/ManualTimeProvider.cs`; any suppression or `#pragma`; spec 002 FR-008; ADRs, the
constitution, CLAUDE.md.

Legend: `[P]` = owns disjoint files (ADR-0109). **No foundational task and no useful
parallelism**: every production change lands in `MediaMtxRtspGateway.cs` / `.PageReading.cs` /
`Log.cs` / `PageReadEnd.cs`, and the stories share those files. Strictly sequential.

**Test filter (all tasks):**

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~Gateways.MediaMtxRtspGateway|FullyQualifiedName~HealthWatcher.StreamHealthWatcherCutShortReadTests|FullyQualifiedName~MediaMtxSourceRepairOverRealGatewayTests|FullyQualifiedName~MediaMtxProbeLogVolumeTests"
```

## Phase 4a: red and characterisation (test-writer, on `398e61a0`)

- [x] **T001 [US1] red: cap 20.** Edit only these assertions, test names and their summaries
  (replace "one thousand" / "1,000" with "twenty" / "20"; leave the stubs' fail-fast guards as they
  are):
  - `Gateways/MediaMtxRtspGatewayListReadersBoundsTests.cs`:
    `The_health_read_fails_after_twenty_pages_rather_than_returning_part_of_the_list` (message contains
    `"20"`, `RequestedPages.Count == 20`, `Max() == 19`);
    `The_configured_paths_read_stops_at_twenty_pages_and_warns_once` (20 requests, 20 paths, one
    Warning containing `"20"`).
  - `Gateways/MediaMtxRtspGatewayConfiguredSourcesBoundsTests.cs`:
    `The_read_stops_at_twenty_pages_and_warns_once` (20 / 19 / 20 sources / message `"20"`).
  - `Gateways/MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests.cs`:
    `The_page_cap_warns_only_when_mediamtx_still_reports_more_pages` data `(19,19,0)`, `(20,20,0)`,
    `(21,20,1)`.
  - `Gateways/MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests.cs`:
    `The_page_cap_warning_says_after_twenty_pages_with_the_source_repair_text` (`"after 20 pages"`).
  - `Gateways/MediaMtxRtspGatewayHealthReadCharacterisationTests.cs`:
    `Reaching_the_page_cap_reports_the_cap_in_the_error` (`"still reported more pages after 20 pages"`).
  - `HealthWatcher/StreamHealthWatcherCutShortReadTests.cs`:
    `A_health_read_that_reaches_the_page_cap_fails_the_tick_and_dispatches_no_camera`
    (`RequestedPages.Count == 20`).

  Expected red: each fails with 1,000 requests / "1000" in the message; the `(21,20,1)` row fails
  with 21 requests and 0 Warnings; `(19,…)` and `(20,…)` rows are green on both sides.

- [x] **T002 [US3] red: items-unreadable warns.**
  - Edit `Gateways/MediaMtxRtspGatewayPageShapeTests.cs`: replace
    `An_object_page_without_items_still_ends_the_configured_reads_normally` with
    `An_object_page_without_items_ends_the_configured_reads_with_one_warning_naming_the_page`
    (theory `configured` / `sources`: 0 read, `RequestedPages == [0]`, exactly one Warning,
    event `MediaMtxConfiguredPathsItemsUnreadable` / `MediaMtxConfiguredSourcesItemsUnreadable`,
    message contains `"page 0"`). Red: no Warning today.
  - New file `Gateways/MediaMtxRtspGatewayItemsUnreadableWarningTests.cs`:
    1. `Items_that_are_absent_or_not_an_array_warn_once_and_keep_what_was_read` — theory over the four
       malformed page-1 bodies of spec US3; the one page-0 source returned; one Warning
       `MediaMtxConfiguredSourcesItemsUnreadable` containing `"page 1"`; the message contains none of
       the body's distinctive text (`"name"`, `"x"`, `"[]"`). **Red.**
    2. `The_startup_read_warns_once_per_read_without_a_throttle` — `ListConfiguredPathsAsync` on
       `{"pageCount":5}`: one Warning `MediaMtxConfiguredPathsItemsUnreadable` `"page 0"`. **Red.**
    3. `The_source_repair_warning_repeats_at_most_every_ten_minutes` — mirror
       `MediaMtxRtspGatewayWarningRepeatIntervalTests` (three-argument constructor, shared
       `MediaMtxConfiguredSourcesReadWarnings` over `ManualTimeProvider`): reads at 0, 9m59s, 10m →
       levels `[Warning, Debug, Warning]`, Debug event `MediaMtxConfiguredSourcesItemsStillUnreadable`. **Red.**
    4. `A_complete_read_rearms_the_warning` — unreadable, complete, unreadable within a minute →
       two Warnings. **Red.**
    5. `Changing_from_the_page_cap_to_unreadable_items_warns_at_once` — cap read then unreadable read
       within a minute → Warnings `PageCapReached`, `ItemsUnreadable` in that order. **Red.**
    6. `An_empty_items_array_still_ends_both_configured_reads_silently` — guard, **green** on both sides.

- [x] **T003 [US2] red: 5 s whole-read deadline.**
  - New fake `Fakes/SchedulingTimeProvider.cs`: minimal timer-capable copy of
    `tests/Identity.Infrastructure.Tests/Fakes/ManualTimeProvider.cs` (`CreateTimer` records due
    times; `Advance` fires due timers). Do not touch the existing `Fakes/ManualTimeProvider.cs`.
  - New file `Gateways/MediaMtxRtspGatewayListReadDeadlineTests.cs`. The gateway is resolved from
    `services.AddSingleton<TimeProvider>(fake)` + `AddMediaMtxGateway().ConfigurePrimaryHttpMessageHandler(...)`
    (mirror `MediaMtxProbeLogVolumeTests`; no resilience handler), so the file compiles on
    `398e61a0`. A stub answers page 0 and holds page 1 open on its request token.
    1. `A_read_still_running_after_five_seconds_fails_as_an_invalid_response` — theory over `health`,
       `configured`, `sources`: advance 5 s → `HttpRequestException`, `InvalidResponse`, `StatusCode`
       null, `InnerException` null, message contains the endpoint path and `"5 s"`. **Red** (today the
       read never ends; the test bounds its own wait with `WaitAsync(TimeSpan.FromSeconds(30))` and
       fails on that timeout).
    2. `The_deadline_covers_the_whole_read_not_each_page` — each page's handler advances the fake by
       2 s before answering, pageCount 10 → throws `InvalidResponse`; `RequestedPages.Count <= 4`. **Red**
       (today 10 pages, no throw).
    3. `A_read_that_finishes_before_five_seconds_returns_every_page` — advance 4.9 s, then release
       page 1 as the last page → both pages' paths, no throw. Guard, **green** both sides; it is what
       stops a deadline shorter than 5 s.
    4. `A_caller_cancellation_before_the_deadline_stays_an_operation_cancellation` — cancel the
       caller's token while page 1 is held, then advance 5 s → `OperationCanceledException`, not
       `HttpRequestException`. Guard, **green** both sides.

  Because every case resolves the gateway through `AddMediaMtxGateway`, case 1 is also spec US2's
  "the deadline reaches the gateway through the DI registration" scenario; no separate test.

  Prove the fake by counterfactual: show that without `Advance` the held read is still pending after
  the stub's own bound, so a green after the fix is caused by the deadline.

- [x] **T004 characterisation (green before, unmodified after).** Run and record green on
  `398e61a0`: `MediaMtxRtspGatewayPageShapeTests` (all but the one test T002 replaces, including
  `Cancelling_while_the_body_is_read_is_still_a_cancellation`),
  `MediaMtxRtspGatewayReasonPhraseTests`, `MediaMtxRtspGatewayHealthReadCharacterisationTests`
  (all but the one T001 edits, including the "no readable items array" cases),
  `MediaMtxRtspGatewayListReadersCharacterisationTests`, `MediaMtxRtspGatewayPaginationTests`,
  `MediaMtxRtspGatewayWarningRepeatIntervalTests`, `MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests`
  (all but the one T001 edits, including `Items_that_are_absent_or_not_an_array_end_the_read_at_that_page`
  and the cancellation case), `MediaMtxSourceRepairOverRealGatewayTests`,
  `StreamHealthWatcherCutShortReadTests` (all but the one T001 edits), `MediaMtxProbeLogVolumeTests`,
  and `tests/Architecture.Tests` `StreamDistributionRegistrationCharacterisationTests` (the
  registration does not change).

**Gate before Phase 4b:** verbatim outputs of T001-T003 showing every **Red** case failing for the
stated reason, and T004 green.

## Phase 4b: implementation (backend-engineer; may not edit `tests/`)

- [x] **T010 [US1]** (after T001's red commit) `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs`:
  `MaxListPages = 20`. Commit `fix(3021): list reads stop at twenty pages`.
- [x] **T011 [US3]** (after T002's red commit) `PageReadEnd.cs` (+`ItemsUnreadable`),
  `MediaMtxRtspGateway.PageReading.cs` (`HasItemsArray`; classify the `FoldPage == false` stop;
  explicit `ItemsUnreadable` arms in `WarnOnUnfinishedRead` before `default`),
  `MediaMtxRtspGateway.cs` (`ListConfiguredPathsAsync` case only), `Log.cs` (three events, plan
  §2.4). Health code untouched. Commit `fix(3021): a configured-paths page without an items array warns`.
- [x] **T012 [US2]** (after T003's red commit) `MediaMtxRtspGateway.cs` (field + four-argument
  `[ActivatorUtilitiesConstructor]`; three-argument chains with `TimeProvider.System`),
  `MediaMtxRtspGateway.PageReading.cs` (`ListReadDeadline` constant; linked CTS; the filtered
  mapping, plan §2.2). Commit `fix(3021): a list read that outlives five seconds fails`.

## Phase 4c: green

- [x] **T020** Full `tests/StreamDistribution.Infrastructure.Tests` and `tests/Architecture.Tests`
  green in Release; `dotnet format --verify-no-changes` clean on the touched projects; the Release
  build has no new warning. T001-T003 red cases now green, T004 unmodified and green. Quote both.

## Phase 5: verify

- [ ] **T030** Unit-level evidence is the observable here (no real MediaMTX misbehaves on demand).
  Record in the PR: the request counts at the cap (20), the deadline test's thrown message verbatim,
  the three new events' rendered text, the rate table of plan §4, and
  `grep -rnE "1000|1,000|thousand" tests/StreamDistribution.Infrastructure.Tests/Gateways tests/StreamDistribution.Infrastructure.Tests/HealthWatcher`
  showing no remaining reference to the old cap. Optional, if a stack is free (one machine, one
  Aspire stack): boot, confirm the health sweep still dispatches every stream each tick with a
  normal MediaMTX (3 pages or fewer, well under both bounds). Latency: N/A.

## Phase 6-7

- [ ] **T040** `/code-review` (backend-reviewer). Findings resolved or accepted in writing.
- [ ] **T041** PR to `develop` (`--base develop`), body: closing keyword for #3021 only if items 2-4
  are the last open ones (item 1 spec 405, item 5 spec 410 are delivered; Q4 is not an item of the
  issue — say it stays open as an owner question elsewhere), verbatim red and green outputs, the
  owner-authorised test edits listed, Q4 reported out.
