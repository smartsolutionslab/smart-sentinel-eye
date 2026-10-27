# Tasks 465 — An overlay seed that times out is retried

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3182 (feature-level issue, on Project #13, status Todo).
Engineer: **backend**. Phase 4a colour: **behaviour-changing → red first**. No foundational task: everything is
inside `SystemVariables` (Application interface, Infrastructure, tests). No migration, no AppHost change,
no frontend, no `Shared.Contracts` change.

The `Integration.Tests` shard filters (`tests/Integration.Tests/ci-shards/`) are not touched: every test
here lives in `SystemVariables.Infrastructure.Tests` / `SystemVariables.Application.Tests`.

## User story 1 (P1) — the index fills itself once overlay-designer is reachable (FR-001, 002, 004, 008)

- [ ] **T001 [US1]** (test-writer) Add `tests/SystemVariables.Infrastructure.Tests/Fakes/ManualTimeProvider.cs`
  (minimal copy of `tests/Identity.Application.Tests/Fakes/ManualTimeProvider.cs`; not shared, test projects do
  not reference each other). Extend `ReverseIndexSeederHostedServiceTests.cs` with scripted-handler tests that
  drive the loop through the clock, no real waiting: timeout then 200; 503 then 200; connection error then 200;
  200 without `published` then 200; 401, 403 and a refused token mint, each then 200 → index populated after the
  second attempt; success on the third attempt → exactly three requests and none after the clock keeps
  advancing; successful first attempt → exactly one request and `SeededOverlays` logged word for word.
  Delays asserted: 2 s, 4 s, 8 s, 16 s, 30 s, 30 s. Run; **quote the red output**.
- [ ] **T002 [US1]** (backend) `src/SystemVariables/Infrastructure/Resolution/ReverseIndexSeederHostedService.cs`:
  become a `BackgroundService` taking `TimeProvider`; `ExecuteAsync` = `await Task.Yield()`, then loop
  `AttemptAsync(attempt, delay, ct)` (today's branches, parse and `SeedOverlays` moved unchanged) until `true`;
  delay 2 s ×2, cap 30 s, via `Task.Delay(delay, timeProvider, ct)`; refused statuses and refused-mint exceptions
  take the same retry path (FR-008). Update the class summary (best-effort-and-self-heals wording is wrong).
  Depends on T001 (red observed). T001 must pass **unmodified**.

## User story 2 (P1) — starting never waits on, or fails because of, the seed (FR-003, 005)

- [ ] **T003 [P] [US2]** (test-writer) Same file as T001, so **not** parallel with it in practice: add
  `StartAsync` returns while the first attempt is still pending; overlay-designer unreachable → host start
  completes and the loop keeps retrying; stop between two attempts → the loop ends and logs nothing
  (keeps `A_cancelled_token_propagates_and_logs_nothing` green). Run; quote red.
  Sequence it after T001 and before T002 so both are observed red together.
- [ ] **T004 [US2]** (backend) Covered by T002's `Task.Yield` and cancellation handling; this task is the
  check that T003 passes unmodified and that `SystemVariablesRegistrationCharacterisationTests`
  (`IHostedService → ReverseIndexSeederHostedService`) stays green unmodified. Depends on T002, T003.

## User story 3 (P2) — the logs say what is happening (FR-007)

- [ ] **T005 [US3]** (test-writer) Re-state, in `ReverseIndexSeederHostedServiceTests.cs` and
  `ReverseIndexSeederBranchCharacterisationTests.cs`, the tests the spec lists as moving:
  `A_500_logs_the_self_healing_warning_naming_that_status`, `A_server_error_stays_a_warning`, `A_404_…`,
  `A_transport_failure_that_is_not_a_refusal_stays_a_warning`, `A_refused_*`, `A_refused_token_mint_*`.
  Levels stay (Warning, Error for a refusal); each failure line names the attempt and the delay and contains
  no "events arrive" / "self-heal" wording; the refusal line no longer says "restarted"; a seed that failed
  twice then succeeded logs `SeededOverlays` once at Information. Run; quote red.
- [ ] **T006 [US3]** (backend) `src/SystemVariables/Infrastructure/Log.cs`: `SeedNonSuccessStatus`,
  `SeedRefused`, `SeedFailed`, `SeedMissingPublishedKey` gain `Attempt` and `RetryDelay`, new wording per
  FR-007/FR-008; `SeededOverlays` untouched (pinned by spec 349). Update the `SeedRefused` XML doc, which
  states the opposite of FR-008. Depends on T005 (red observed).

## User story 4 (P2) — a late seed never undoes what events established (FR-006)

- [ ] **T007 [P] [US4]** (test-writer) `tests/SystemVariables.Infrastructure.Tests/Resolution/InMemoryReverseIndexTests.cs`
  (disjoint from T001/T003/T005): `TrySeedOverlayReferences` on an overlay an event removed → `false`, still
  absent; on an overlay an event upserted → `false`, event text kept; on an untouched overlay → `true`, indexed;
  an event after the seed overwrites it. Plus one seeder-level test in `ReverseIndexSeederHostedServiceTests.cs`
  (add after T001's edits): listing contains O, archive handled before apply → O absent; the reported count
  excludes skipped overlays. Run; quote red (compile failure on the missing method is acceptable only if the
  assertion-level red is captured too; prefer adding the interface method as a stub that returns `false` first
  — see T008 — so the red is behavioural).
- [ ] **T008 [US4]** (backend) `src/SystemVariables/Application/Resolution/IReverseIndex.cs`: add
  `TrySeedOverlayReferences(Guid, IReadOnlyList<string>) → bool`. `InMemoryReverseIndex.cs`: `touchedByEvent`
  set marked by `UpsertOverlayReferences` and `RemoveOverlay`; the seed-only write under the same lock;
  `ReverseIndexSeederHostedService.SeedOverlays` calls it and counts only applied overlays.
  `tests/SystemVariables.Application.Tests/Fakes/InMemoryReverseIndex.cs`: implement the method.
  Depends on T007 (red observed). Argument guards use `Ensure.That(...)`.

## Closing

- [ ] **T009** (backend) Run `SystemVariables.Infrastructure.Tests`, `SystemVariables.Application.Tests`,
  `Architecture.Tests`. Confirm unmodified: every listing-parse test (specs 300, 349, 358),
  `SystemVariablesRegistrationCharacterisationTests`, handler tests for `OverlayRevisionPublishedV3` /
  `Archived`. `dotnet format --verify-no-changes` on touched projects; Release build clean (analyzers
  are errors there). Depends on T002, T004, T006, T008.
- [ ] **T010** Phase 5 `/verify`: SC-005 on the live stack per [quickstart.md](quickstart.md). Needs the AppHost
  (stopped; start only when the user asks) and the stack stopped before any build (MSB3027). Depends on T009.

## Dependencies

T001 → T003 → (T002 ‖ T005 red captured) → T002 → T004; T005 → T006; T007 → T008; T002, T004, T006, T008 → T009 → T010.
T001, T003, T005 and the seeder-level test in T007 edit the same test file, so run them as one test-writer
pass, one red run, rather than in parallel. T007's `InMemoryReverseIndexTests.cs` part is the only
genuinely disjoint file.

## Not tasks (explicitly)

- No `AppHost` `WaitFor(overlay-designer)` and no self-heal on read (spec Out of scope).
- No change to `SystemVariablesInfrastructureModule.BindReverseIndexSeeder`, the named client, the auth
  handler, `overlay-designer`, the seeder credential, or any event handler.
- No new metric or dashboard, no new ADR, no `Co-Authored-By` footer (ADR-0086).
