# Tasks 391: The two older MediaMTX list readers bound their page read, and a cut-short health read fails the tick

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3010 (feature-level, on
Project #13; there are no per-task issues).

**Phase 4a colour: RED** (behaviour-changing). T001 and T002 compile against today's code. They must
be seen **failing** on unchanged production code, and their verbatim output is quoted in the PR. A
test marked **guard** passes today and must keep passing. Guards pin the part of today's behaviour
that the decision keeps, and they are declared here so that a guard arriving green is not mistaken
for a phase-4 failure. Every other new test that arrives green **is** a phase-4 failure.

**Red is fail-fast, not a hang.** Today's loop is unbounded. Every stub handler in T001/T002
therefore throws `InvalidOperationException("unbounded read")` on its **1,101st** request (spec 388
precedent). A red that shows up as a test hang means the stub is wrong. Fix the stub. Do not
lengthen the timeout.

**Engineer:** `backend-engineer` (T010-T012). **Test-writer:** T001-T003. **Test-adversary:**
optional (T006). **Security review:** required (T021).

**Docker:** none of T001-T003 need it. They are all unit tests in
`tests/StreamDistribution.Infrastructure.Tests`. **No `ci-shards/shard-N.filter` entry**, because
the shard files partition `Integration.Tests` only and this slice adds no integration class.

**Forbidden in every task:** editing any existing test assertion (plan §8.1; an edit is a **BLOCK**
to report, not an adjustment). Also forbidden: `ListConfiguredPathSourcesAsync`, `AddPageToSources`
and `MaxConfiguredSourcePages` (FR-007, byte-identical); `StreamHealthWatcher.cs`;
`MediaMtxReconciler.cs`; every `IRtspGateway` signature; every existing `Log.cs` template;
`Shared.Contracts`; any other context; `AppHost`; DI modules; ADRs, the constitution and CLAUDE.md;
suppressions, thresholds, `.editorconfig` and analyzer configs; `static` mutable fields.

**Existing test files that may be edited:** none. Stub handlers and recorders are copied in-file
(ADR-0052; the existing ones are `private`).

Legend: `[P]` means the task owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 (`Log.cs`) blocks T011. T012 (doc comment) is `[P]` with T010 and T011.

---

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1][US2] New
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayListReadersBoundsTests.cs`.
  Use the real `MediaMtxRtspGateway` over an in-file `PagesHandler(Func<int, string> bodyForPage)`.
  The handler records `page` and `AbsolutePath` per request and throws on the 1,101st request. Copy
  the shape from `MediaMtxRtspGatewayConfiguredSourcesBoundsTests`. Log through a
  `CapturingLogger<MediaMtxRtspGateway>`. `HugePageCount = "2147483647"`, written raw into the
  JSON. Each fact also asserts that every request went to its reader's endpoint (`/v3/paths/list`
  for health, `/v3/config/paths/list` for configured). Facts:
  - (a) `An_empty_page_ends_the_health_read_whatever_page_count_claims`: page 0 has
    `pageCount=2147483647` and one canonical ready item. Every later page has
    `pageCount=2147483647` **and** `items: []`. The huge `pageCount` on the later pages is
    load-bearing, because without it today's loop stops at page 1 and the fact arrives green.
    Expect requested `[0,1]`, 1 entry, and no gateway entry at Warning or above. **Red**
    (`InvalidOperationException: unbounded read`).
  - (b) `The_health_read_fails_after_one_thousand_pages_rather_than_returning_part_of_the_list`:
    every page has `pageCount=2147483647` and one distinct canonical item. Expect
    `Should.ThrowAsync<HttpRequestException>`, `HttpRequestError == HttpRequestError.InvalidResponse`,
    `StatusCode` null, message contains `1000`, exactly 1,000 requests (max page 999), and no gateway
    entry at Warning or above. **Red** (it throws `InvalidOperationException` at request 1,101).
  - (c) `[Theory] An_unreadable_page_count_fails_the_health_read_rather_than_returning_part_of_the_list`,
    with `1.5`, `3e9`, `2147483648`, `-2147483649` and `1e400`. Page 0 has that `pageCount` and one
    canonical item. Expect `HttpRequestException` with `InvalidResponse`, requested `[0]`, a message
    that does **not** contain the raw token (`Case.Sensitive`) and does contain `page 0`, and no
    gateway entry at Warning or above. **Red** (`FormatException`).
  - (d) `An_empty_page_ends_the_configured_paths_read_whatever_page_count_claims`: as (a) against
    `ListConfiguredPathsAsync`. Expect `[0,1]`, 1 path, and no Warning. **Red** (unbounded read).
  - (e) `The_configured_paths_read_stops_at_one_thousand_pages_and_warns_once`: as (b). Expect no
    throw, 1,000 requests, 1,000 paths, and exactly one Warning whose message contains `1000`.
    **Red** (unbounded read).
  - (f) `[Theory] An_unreadable_page_count_ends_the_configured_paths_read_with_one_warning_not_an_exception`,
    with the same five tokens. Expect no throw, requested `[0]`, page 0's path returned, and exactly
    one Warning whose message does not contain the raw token. **Red** (`FormatException`).

  Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayListReadersBoundsTests"`.
  Return the verbatim output. All 14 cases are expected to fail: (a)/(b)/(d)/(e) with
  `InvalidOperationException: unbounded read`, and (c)/(f) with `FormatException`.

- [x] **T002** [P] [US1] New
  `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherCutShortReadTests.cs`.
  This drives the **real** `StreamHealthWatcher.DispatchAsync` (internal, already reachable from
  this project) over the **real** `MediaMtxRtspGateway`, which sits on an in-file `PagesHandler`
  (the T001 shape, with the throw on request 1,101). The other parts are in-file copies of
  `StreamHealthWatcherScopeTests`' harness: a `ServiceCollection` with an in-file recording
  `ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>`, a
  scope-counting `IServiceScopeFactory`, and a fixed `IClock`. The watcher logs to a
  `CapturingLogger<StreamHealthWatcher>`. There are two streams, cam-A and cam-B, both `Healthy`,
  built with `MediaMtxPath.For(CameraIdentifier.From(guid))`. MediaMTX lists only cam-A, ready, on
  page 0. Facts:
  - (a) `[Theory] A_health_read_cut_short_by_an_unreadable_page_count_fails_the_tick_and_dispatches_no_camera`,
    with `1.5`, `3e9`, `2147483648`, `-2147483649` and `1e400`. Expect: `DispatchAsync` does **not**
    throw; no scope opened; no command handled (so cam-B is **not** dispatched as "path not
    registered"); exactly one entry at Warning, with message exactly
    `MediaMTX health sweep failed; skipping this tick for every stream.` and an `Exception` that is
    `HttpRequestException` with `HttpRequestError.InvalidResponse`; **no entry at Error**. **Red**
    (the `FormatException` escapes `DispatchAsync`, because the inner catch takes only
    `HttpRequestException`).
  - (b) `A_health_read_that_reaches_the_page_cap_fails_the_tick_and_dispatches_no_camera`: every page
    has `pageCount=2147483647` and one distinct canonical item. Expect 1,000 requests, no scope, no
    command, one `HealthSweepFailed` Warning with `InvalidResponse`, and nothing at Error. **Red**
    (the `InvalidOperationException: unbounded read` escapes).
  - (c) `An_empty_page_ends_the_health_read_and_the_tick_dispatches_every_stream`: page 0 has cam-A
    ready with `pageCount=2147483647`, and page 1 is `items: []` with the same `pageCount`. Expect
    requested `[0,1]`; two scopes; handled cameras `[cam-A, cam-B]`; cam-A's command `IsReady`;
    cam-B's command `LastError == "path not registered"`; nothing at Warning or above. **Red**
    (unbounded read).

  Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~StreamHealthWatcherCutShortReadTests"`.
  Return the verbatim output. All 7 cases are expected to fail as stated.

- [x] **T003** [P] [US2] Guards (expected **green** today and after). New
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayListReadersCharacterisationTests.cs`.
  It uses the T001 stub shape. Theories take the reader as `[InlineData("health")]` /
  `[InlineData("configured")]` and dispatch to `GetAllPathsHealthAsync` or `ListConfiguredPathsAsync`
  through one in-file helper that returns the count of paths read. Facts:
  - (a) `[Theory] A_page_count_that_is_not_a_number_still_reads_one_page_without_a_warning`, with
    reader × `"\"5\""` / `null` (2 × 2 rows). Expect requested `[0]`, 1 path, and no Warning.
  - (b) `[Theory] A_page_of_only_non_canonical_paths_is_not_an_empty_page`, per reader. Page 0 has
    `pageCount=2` and only `manual-x`. Page 1 has one canonical item. Expect `[0,1]` and 1 path.
  - (c) `[Theory] A_zero_page_count_with_an_empty_first_page_reads_one_page`, per reader. Page 0 is
    `{"pageCount":0,"items":[]}`. Expect `[0]`, 0 paths, no throw, and no Warning.
  - (d) New file
    `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayAddIdempotenceTests.cs`,
    with `A_redundant_add_for_a_path_mediamtx_already_has_succeeds`. The stub answers
    `POST /v3/config/paths/add/{name}` with 400 and `GET /v3/config/paths/get/{name}` with 200.
    Expect `AddPathAsync` does not throw, both requests are made in that order, and exactly one
    Information entry reads
    `MediaMTX path {name} was already registered; treating add as success (spec 309).` This confirms
    #3010's open premise (spec P6), which had no unit cover at the gateway. **Counterfactual
    (prove the guard):** with the stub's `get/` answering 404 instead, the fact must fail with an
    `HttpRequestException` (400). Run that once in the working tree, quote it, and revert it. Do not
    commit it.

  Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayListReadersCharacterisationTests|FullyQualifiedName~MediaMtxRtspGatewayAddIdempotenceTests"`.
  Return the verbatim output. Every case is expected to pass.

  **Commit T001-T003:** `test(3010): bounded list reads for the reconcile and health readers (red)`.
  This commit builds on its own, and leaves T001/T002 red until Phase 2 lands (CLAUDE.md, red/green
  pair).

## Phase 1b: Adversarial (test-adversary; optional)

- [ ] **T006** [US1] Hostile `pageCount` against the 2 s watcher. Write new files under
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/` or `HealthWatcher/` only. Cover: cap
  boundaries 999/1000/1001 with full pages, on both readers; `items` absent, `null` or an object
  on page 1 with a huge `pageCount`; a 10,000-digit `pageCount` literal (no throw from the
  configured read, no raw token in any message); a good tick after a failed tick dispatches
  normally (the failed tick left no `degradedSince` entry, so a Degraded stream's Offline window is
  not started early); a cancellation mid-read still propagates as `OperationCanceledException` from
  both readers. Report findings.

## Phase 2: Green (backend-engineer; may not edit any test written above)

- [x] **T010** [US2] Foundational. `src/StreamDistribution/Infrastructure/Log.cs` gets
  `MediaMtxConfiguredPathsPageCountUnreadable(int page)` and
  `MediaMtxConfiguredPathsPageCapReached(int pageCap)`, both at Warning, with the exact templates in
  plan §4. Place them directly after spec 388's `MediaMtxConfiguredSources*` pair.
- [x] **T011** [US1][US2] `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs`.
  Add `MaxListPages`, `PageReadEnd`, `PageRead` and `ReadPagesAsync` (plan §2-§3). Rewrite
  `GetAllPathsHealthAsync` (it throws `HttpRequestException(HttpRequestError.InvalidResponse, …)`
  with the plan §3 messages) and `ListConfiguredPathsAsync` (it logs the T010 Warnings and returns
  the paths read). `AddPageToPaths` and `AddPageToHealth` return `bool`. Reword the stale "above"
  comment (plan §3). Before committing, confirm that `git diff` shows **no hunk** inside
  `ListConfiguredPathSourcesAsync`, `AddPageToSources` or `MaxConfiguredSourcePages`.
- [x] **T012** [P] [US1] `src/StreamDistribution/Domain/Stream/IRtspGateway.cs`: change the doc
  comment only (plan §5). No signature change.
  **Commits:** T010+T011 as
  `fix(3010): the reconcile and health list reads stop on an empty page and at 1,000 pages`, and
  T012 as `docs(3010): a health list read that cannot finish throws rather than returning part of it`.

## Phase 3: Verify

- [ ] **T020** Local. `dotnet build -c Release` is clean: no new warnings, and ADR-0084 metric
  advisories are noted. Then run each of these:
  - `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release`. All green, including
    T001-T003, T006, and the unmodified `MediaMtxRtspGatewayPaginationTests`,
    `StreamHealthWatcherScopeTests` and every spec 378/388 file.
  - `dotnet test tests/StreamDistribution.Application.Tests -c Release`.
  - `dotnet test tests/Architecture.Tests -c Release`.

  Unmodified-test proof: `git diff origin/develop --stat -- tests/` lists **only new files**, and
  `git diff origin/develop -- tests/ | grep '^-' | grep -v '^---'` prints nothing. CI is
  authoritative for C7, `MediaMtxReconcilerIntegrationTests`, `StreamHealthTransitionTests` and
  `ProvisionRetireRaceIntegrationTests`. These use the real MediaMTX, whose `pageCount` is
  well-formed, which is the evidence for B4. Coverage gates hold (ADR-0065). Phase 5 note (spec §2,
  step 3): on a running stack, one minute of sweeps shows no `HealthSweepFailed` and no new gateway
  Warning. The hostile cases are unit-only, and the PR says so.
- [ ] **T021** Phase 6: `/code-review` plus `security-reviewer`. Check the control-API budget: the
  worst case of 1,000 GETs per 2 s tick against the 2000/min `api` partition, and that the limiter's
  401 fails the tick (ADR-0171). Check the truth of announcements: no partial health result reaches
  `DispatchAsync`, under any end of the read. Check that no raw token or MediaMTX text appears in any
  new message.
- [ ] **T022** Orchestrator. File the A2 follow-up: *Move `ListConfiguredPathSourcesAsync` onto the
  shared `ReadPagesAsync` (behaviour-preserving; spec 388's tests are the characterisation)*. It is
  tech-debt, goes on Project #13, and is **not** `agent:ready` until the owner labels it. Link it
  from the PR body.

## Dependencies

T001, T002 and T003 are `[P]` and go into commit 1. T006 (optional) follows commit 1. T010 → T011,
with T012 `[P]` alongside. That is commit 2 (+ commit 3 for the doc). Then T020 → T021 → T022.
T001, T002 and T003 own disjoint new files. T010 and T011 touch different files, but T011 calls the
T010 log methods, so they are ordered.
