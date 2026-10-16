# Tasks 378: The reconciler repairs a MediaMTX path whose source has diverged

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2960 (feature-level; no
per-task issues). **Split out**: #2991 (not here).

**Phase 4a colour: RED** (behaviour-changing). R1-R3 must be seen **failing on assertions** on
unchanged production code. R4-R6 cannot compile until the new types exist; they are written in the
working tree, their compile error is quoted, and they land with the fix. A test that arrives green
is a phase-4 failure.

**Adversarial pass: required** (`test-adversary`, T006) — the failure mode is a race.

**Engineer:** backend-engineer (T010-T013). **Test-writer:** T001-T005. **Test-adversary:** T006.
**Security-sensitive: yes** (stream attribution) — phase 6 runs `security-reviewer` alongside
`/code-review`.

**Docker is down locally; CI is authoritative** for Integration.Tests that boot the stack (R3 and
the unmodified-green set in T020). R1, R2, R4-R6 and the `FixtureLogic` snapshot test need no Docker.

**Forbidden in every task:** `Shared.Contracts`, any other context, `StreamSourceUrl.cs`,
`RepointStreamCommandHandler.cs`, `AddPathAsync`'s behaviour, `ListConfiguredPathsAsync`; any
existing assertion other than the two named in plan §8; any C7 assertion; spec 099's and
`UnresolvedFabAuditRowIntegrationTests`' files; ADRs, the constitution, CLAUDE.md; suppressions,
thresholds, `.editorconfig`, analyzer configs; a new HTTP client or credential.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 (the `IRtspGateway` member) blocks T011-T013 and every test double. Phase 1's
`[P]` tasks touch three different test projects and run in parallel.

---

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1] R1 — `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs`:
  add the three `ExpectedRegistrations` lines from plan §8.1 at the stated positions; nothing else.
  Run `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~StreamDistributionRegistrationCharacterisationTests"`;
  return verbatim output: `The_stream_distribution_registrations_are_these_in_this_order` **fails**
  (the three lines missing), the rest pass.
- [x] **T002** [P] [US2] R2 — `tests/Integration.Tests/AppHostComposition/IntegrationFixture.snapshot.txt`:
  insert the `MediaMtxSourceRepair__Interval="49.17:02:47.2940000"` `EnvironmentAnnotation` on
  `stream-distribution` directly after `WhepAuthorizeRateLimiting__Window` and renumber that
  resource's later indices (plan §8.2). No other snapshot. Run
  `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~AppHostCompositionCharacterisationTests"`
  (in-memory, no Docker); return verbatim output: the IntegrationFixture lane **fails** at the
  inserted line, the other lanes pass.
- [x] **T003** [P] [US1] R3 — `tests/Integration.Tests/StreamDistribution/MediaMtxReconcilerIntegrationTests.cs`:
  add two facts using the file's existing harness (`BuildReconcilerAsync`, `AddMediaMtxPathAsync`):
  (a) `Reconciler_repoints_a_path_whose_source_differs_from_the_persisted_source` — DB row
  `rtsp://10.0.8.1/h264`, MediaMTX path added at `rtsp://10.0.8.2/h264`, `ReconcileOnceAsync`, then
  `GET /v3/config/paths/get/{path}` and assert `source == "rtsp://10.0.8.1/h264"`;
  (b) `Reconciler_leaves_a_path_whose_source_matches_the_persisted_source` — equal sources, run
  twice, assert source unchanged and (via the response's full config) nothing else changed.
  Existing class, shard-2 already lists it: **no shard-filter edit**. Compiles against today's code.
  (a) is red in CI only (Docker down): the orchestrator pushes the Phase 1 commit alone and the
  verbatim assertion failure from the job log is quoted in the PR; if that round trip is not taken,
  say so in the PR and rely on R1/R2 + R4 as the observed red.
- [x] **T004** [US1] R4 — write `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayConfiguredSourcesTests.cs`
  (stub `HttpMessageHandler`, pattern of `MediaMtxRtspGatewayPaginationTests`): source returned
  verbatim (case, trailing slash, `:554` preserved); non-canonical names filtered; three pages
  merged; a canonical item with no `source` / a non-string `source` omitted; absent `pageCount` =
  one page. Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayConfiguredSourcesTests"`,
  return the verbatim compile error naming `ListConfiguredPathSourcesAsync`. **Do not commit**; hand to T010.
- [x] **T005** [US1][US2] R5 — write, in `tests/StreamDistribution.Infrastructure.Tests/Reconciler/`:
  `MediaMtxSourceRepairTests.cs` (fake stateful gateway that applies PATCHes + fake
  `IStreamSourceSnapshot`; existing `Fakes/CapturingLogger.cs`) covering spec §2 happy, converged
  (two passes, zero PATCH), ordinal (case + trailing slash: patched once, then zero), bad request
  (PATCH throws for one path: Warning with path, other paths repaired, no
  throw), scope (no-row path, retired, absent-from-MediaMTX: no call), exact rendered `Repaired` line
  with a source carrying `/SECRET?token=SECRET` (no `SECRET` in any entry); and
  `MediaMtxSourceRepairSweepTests.cs` (mirror `MediaMtxReconcilerStartupOrderingTests`' probe +
  fake lifetime): no scope before `ApplicationStarted`; at `Interval = 50 ms` at least two passes;
  a throwing pass is logged (`SourceRepairSweepFailed`) and the next tick still runs; stops on
  `ApplicationStopping`. Also add the new `IRtspGateway` member to `FakeRtspGateway`,
  `RecordingStreamDistributionPorts`, `StreamHealthWatcherScopeTests.StubGateway`,
  `ProvisionRetireRaceIntegrationTests.WindowedGateway` (delegate) and C7's `RecordingGateway`
  (returns the four configured paths, kept = `rtsp://10.0.5.2/h264` verbatim, **records no event**
  — plan §8). Return the verbatim compile errors. **Do not commit**; hand to T010.
  **Commit T001-T003 only:** `test(2960): pin the source-repair registrations, lane interval and integration outcome (red)`.

## Phase 1b: Adversarial (test-adversary)

- [x] **T006** [US1] R6 — `tests/StreamDistribution.Infrastructure.Tests/Reconciler/MediaMtxSourceRepairRaceTests.cs`,
  one fact per row of plan §3's table, driven by fakes whose hooks run a "repoint" (commit in the
  snapshot fake, PATCH in the gateway fake) at the exact interleaving point: between the MediaMTX
  read and the DB read; after the DB read with its PATCH landing before ours; after ours; after our
  re-read; a stale retry landing after a converged pass (next pass repairs). Plus: invariant R1
  asserted across every fact (every PATCH value was the snapshot's committed value at that moment);
  R3 (never more than two PATCHes per path per pass, even if the committed source changes again
  after the second); a retire committed mid-pass stops the re-patch. Hunt further (cancellation
  mid-pass, a path removed between list and PATCH answering 404, duplicate/absent `pageCount`) and
  report findings. Return verbatim compile errors; hand to T010. Not committed alone.

## Phase 2: Green (backend-engineer; may not edit any test written above)

- [x] **T010** [US1] Foundational — `IRtspGateway.ListConfiguredPathSourcesAsync` + `MediaMtxRtspGateway`
  implementation (plan §2); `AddPathAsync` doc comment (comment only). Commit with R4 and the
  test-double members from T005.
- [x] **T011** [US1] `IStreamSourceSnapshot` + `StreamSourceSnapshot` + `MediaMtxSourceRepair` + the
  three `Log.cs` messages (plan §3, §5); `MediaMtxReconciler.ReconcileOnceAsync` runs the pass last
  (plan §4). `Ensure.That` guards; `Option<T>` for the committed read; `var`; no underscore fields.
- [x] **T012** [US2] `MediaMtxSourceRepairOptions` + binding with `ValidateOnStart` +
  `MediaMtxSourceRepairSweep` + `.AddHostedService<MediaMtxSourceRepairSweep>()` directly after
  `MediaMtxReconciler` (plan §4); `src/StreamDistribution/Api/appsettings.json` is **not** given a
  value (the type default is the production value).
- [x] **T013** [US2] `src/AppHost/IntegrationLaneOverrides.cs`: `WithIntegrationLaneSourceRepairInterval`
  (gated `isE2ETests`, comment naming spec 099's outage technique as the reason), called on
  `streamDistribution` right after `WithIntegrationLaneWhepCeiling` (`AppHost.cs:376`).
  **Commit T010-T013 + R4-R6:** `fix(2960): the reconciler repairs a MediaMTX path whose source diverged from the database`.

## Phase 3: Verify

- [ ] **T020** Local (no Docker): `dotnet build -c Release` clean (no new warnings; IDE1006/S-rules
  advisory noted); `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release`,
  `dotnet test tests/StreamDistribution.Application.Tests -c Release`,
  `dotnet test tests/Architecture.Tests -c Release`,
  `dotnet test tests/Integration.Tests -c Release --filter "Category=FixtureLogic"` — all green, R1/R2/R4-R6 included.
  CI (authoritative): R3 green, and **unmodified** green: `MediaMtxReconcilerPassCharacterisationTests`
  (C7), the four existing `MediaMtxReconcilerIntegrationTests` facts, `StreamHealthTransitionTests`,
  `UnresolvedFabAuditRowIntegrationTests`, `ProvisionRetireRaceIntegrationTests`. Coverage gates
  (ADR-0065) hold. Phase 5 note: one manual observation on a running stack if one is available —
  patch a live path away, watch it return within 10 s in the `Repaired` log line; otherwise R3 in CI.
- [ ] **T021** Phase 6: `/code-review` + `security-reviewer` (attribution: wrong-camera window,
  no URL leak in logs, no new credential path, no write of a MediaMTX-sourced value).

## Dependencies

T001, T002, T003 [P] → commit 1. T004, T005, T006 (uncommitted) → T010 → T011 → T012 → T013 →
commit 2 → T020 → T021. T011 and T012 touch different new files but both edit
`StreamDistributionInfrastructureModule.cs`/`Log.cs`, so they are sequential, not `[P]`.

## As executed

- **T002's snapshot value.** The task text above gives the final value, `49.17:02:47.2940000`.
  The red commit first pinned `00:10:00` (a 10 minute lane interval); commit `5e00f976` changed it
  to `49.17:02:47.2940000`, the largest interval `PeriodicTimer` accepts, once it was clear that any
  finite interval shorter than a shard can tick inside spec 099's outage windows.
- **1 s floor and placement (`de1e2f58`, then the fix commit after it).** The interval validation
  gained a 1 s floor (a shorter interval exhausts the shared MediaMTX control-API rate partition,
  2000/min), and `BindMediaMtxSourceRepair` moved from inside `BindStreamFabAttribution` to the top
  level of `AddStreamDistributionInfrastructure`, right after it.
- **T020** local runs are recorded in the PR body; CI remains authoritative for the Docker-backed
  tests. **T021** (phase 6) is not part of this slice.
- **Accepted in writing** (plan §9): short bounded stale-write windows; a refused PATCH logs a
  Warning every interval (follow-up); unbounded `pageCount` (follow-up); the misleading
  `ReconcilerStartupPassFailed`-after-`Complete` log pair (plan §4).
