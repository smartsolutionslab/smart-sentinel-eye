# Tasks 389: Camera URLs refuse a raw `@` after the authority on write, and stay readable on read

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2999 (feature-level; on
Project #13 already, picked up by the autonomous lane — no per-task issues, per CLAUDE.md Phase 3)

**Phase 4a colour.** Two, by task: **characterisation** (T010-T016, observed green on `2159a482` before any
production edit, then unmodified) and **red** (T020-T025, observed failing, failure quoted verbatim in the PR).
Ambiguity resolves to red. The engineer may not edit any test from T010-T025.

**Roles.** Tests: `test-writer` (T001, T010-T025, T040), then `test-adversary` (T050). Production code:
`backend-engineer` only (T030-T036). Review: `backend-reviewer` + `security-reviewer` (T060). No frontend,
infra or migration work.

Format: `[ID] [P?] [Story] description — files`. `[P]` = owns disjoint files from every other `[P]` task in
the same phase and may run in parallel (ADR-0109). One branch, one index: parallel agents share the worktree,
so `[P]` means disjoint *files*, committed by the orchestrator in sequence.

---

## Phase 0 — Setup (blocks everything)

- [ ] **T001 [US1-3]** Re-scan for writers and pinned rows on the current branch tip: `grep -rn "RtspUrl\.From\|StreamSourceUrl\.From" src` must list exactly W1-W4, R1, R2 (spec §1.3); `grep -rniE 'rtsp:[^"]*@' tests src apps` read hit by hit — any new test whose address reaches a value object and is now refused is added to plan §6 and the PR body. Record the result in the PR. No file changes unless a new row is found.

## Phase 1 — Characterisation, captured green first (US1 FR-004, US2, US3)

All run on unmodified production code and must be green. Each `[P]` owns its own new file.

- [ ] **T010 [P] [US1]** C1 baseline: run `dotnet test tests/Shared.Kernel.Tests`, capture the pass count. No file change. Guards FR-001.
- [ ] **T011 [P] [US1]** C2 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAcceptSetCharacterisationTests.cs` — spec §5 accept set accepted (value round-trips); §5 old-reject set throws `ArgumentException` with today's exact message.
- [ ] **T012 [P] [US1]** C3 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAcceptSetCharacterisationTests.cs` — same for `StreamSourceUrl`.
- [ ] **T013 [P] [US2]** C4 `tests/Architecture.Tests/Persistence/CameraUrlHydrationCharacterisationTests.cs` — from the design-time model (as `CameraCatalogModelCharacterisationTests.BuildModel` does), `Camera.Url`'s value converter `ConvertFromProvider(row)` yields an `RtspUrl` whose `Value` equals `row`, for every §5 hydrate row. Counterfactual recorded in the class summary (tighten `From` locally without touching the converter → red).
- [ ] **T014 [P] [US2]** C5 `tests/StreamDistribution.Infrastructure.Tests/StreamSourceUrlHydrationCharacterisationTests.cs` — same for `Stream.SourceUrl`.
- [ ] **T015 [P] [US2]** C6 `tests/Integration.Tests/CameraCatalog/StoredMisreadCameraAddressIntegrationTests.cs` + one class entry in a `tests/Integration.Tests/ci-shards/shard-N.filter` — insert a camera row with `rtsp_url = 'rtsp://admin:12345#@cam/s'` by raw SQL (pattern: `StaleIdempotencyReservationIntegrationTests`); `GET /cameras/{id}` and `GET /cameras?fabId=…` answer 200 and include it. Aspire fixture, `[Collection(AspireCollection.Name)]`; one stack per machine.
- [ ] **T016 [P] [US3]** C7 `tests/StreamDistribution.Infrastructure.Tests/CameraEventFailurePolicyCharacterisationTests.cs` — Wolverine discovery over the StreamDistribution Application assembly with `StubAllExternalTransports` (pattern: `WallSceneSwitchFailurePolicyTests`): the chains for `CameraRegisteredV1` and `CameraAddressChangedV1` carry no failure rule; where the combined rules are reachable, attempt 3 of an `InvalidOperationException` yields `MoveToErrorQueue`. If discovery cannot compile these chains without the stack, record why in the class summary and assert the strongest reachable fact (no `IHandlerPolicy`/`OnException` registration for them in `StreamDistributionInfrastructureModule`) — do not drop the task silently.

**Gate:** T010-T016 all green on `2159a482`, pass counts quoted.

## Phase 2 — Red (US1, US3)

Depends on Phase 1 (same projects; run after so the characterisation baseline is not contaminated). Each `[P]`
owns its file. Every test references only types that exist at `2159a482` — a compile error is not a red.

- [ ] **T020 [P] [US1]** R1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAtSignAfterAuthorityTests.cs` — each §5 new-reject row: `RtspUrl.From` throws `ArgumentException` with message exactly `value: <FR-003> (Parameter 'value')`; the message contains no substring of the input after `rtsp://` (assert per row against its secret part).
- [ ] **T021 [P] [US1]** R2 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAtSignAfterAuthorityTests.cs` — same for `StreamSourceUrl.From`.
- [ ] **T022 [P] [US1]** R3 `tests/Architecture.Tests/CameraUrlAtSignAfterAuthorityEndpointTests.cs` — on `CameraCatalogEndpointHost`: `POST /cameras` and `PATCH /cameras/{id}` (valid If-Match) with each of the five issue forms → 400, `title` `CAMERA_INVALID_REQUEST`, `detail` the rendered FR-003 text, body free of the secret, handler not invoked. Plus one PATCH with a stale If-Match → still 400 (parse before version check).
- [ ] **T023 [P] [US3]** R4 `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyMisreadSourceReplayTests.cs` — real event handler over the real command handler and the in-memory fakes: (a) `CameraRegisteredV1` for a new camera with `rtsp://admin:12345#@cam/s` → `InvalidOperationException`; no stream saved; no gateway call; exactly one Warning, containing `STREAM_INVALID_RTSP_SOURCE`, not containing `12345` or `admin`; (b) a newer `CameraAddressChangedV1` with the same address onto a seeded stream → throws; stream address unchanged; no gateway call; no log line contains the secret.
- [ ] **T024 [P] [US1]** R5 edit `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressUserInfoLogRedactionTests.cs` — exactly the two facts in plan §6: assert `RtspUrl.From(CutShortAddress)` throws with the FR-003 message that does not contain `operator:2468`, and the seeded camera's address is unchanged. Update the class summary. Authorised by the #2999 decision for these rows only.
- [ ] **T025 [P] [US3]** R6 edit `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedAdversarialOrderingTests.cs` — move the three `InlineData` rows (lines 463-465) from `An_ignored_hostile_address_leaves_no_trace...` to `An_older_change_whose_address_this_context_rejects_fails_rather_than_being_ignored`, unchanged. No assertion edited.
- [ ] **T026 [US1-3]** Run every command in plan §5.4; return the **verbatim** output. Expected: Phase 1 still green; T020-T025 red for the reason in plan §5.2. Any T020-T025 test arriving green is a phase-4 failure — stop and report.

**Gate:** red observed and quoted; the engineer's brief is that output.

## Phase 3 — Implementation (`backend-engineer`)

T030 blocks T031/T032 (they call the new member). T031 and T032 are disjoint contexts: `[P]`. T033/T034 depend
on T031/T032 respectively.

- [ ] **T030 [US1]** `src/Shared.Kernel/UrlAuthority.cs` (new) + `src/Shared.Kernel/UrlRedaction.cs` — move `AtSignFollowsTheAuthority` and `AllSchemeCharacters` verbatim into `public static class UrlAuthority`, make the predicate `public` with `Ensure.That(url).IsNotNull()`; share `AuthorityTerminators` / `IsSchemeCharacter` as `internal`; `ToOrigin` calls `UrlAuthority.AtSignFollowsTheAuthority`. Behaviour-preserving: T010 must pass unmodified.
- [ ] **T031 [P] [US1, US2]** `src/CameraCatalog/Domain/Camera/RtspUrl.cs` — split today's chain into the stored-invariants helper; `From` = helper + FR-002 `Satisfies` with the FR-003 literal; add `FromStored` (plan §3.2). Update the doc comment.
- [ ] **T032 [P] [US1, US2]** `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` — same.
- [ ] **T033 [US2]** `src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs:99` — read lambda → `RtspUrl.FromStored`. Depends on T031.
- [ ] **T034 [US2]** `src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs:73` — read lambda → `StreamSourceUrl.FromStored`. Depends on T032.
- [ ] **T035 [P] [US3]** comment-only, `src/StreamDistribution/Application/EventHandlers/CameraRegisteredIntegrationEventHandler.cs` and `CameraAddressChangedIntegrationEventHandler.cs` — the "cannot occur without drift" reasoning gains the second cause (an event published before spec 389) and states that the default policy dead-letters after three attempts. No code change; characterise by hashing the comment-stripped files before/after.
- [ ] **T036 [US1-3]** Full verification: `dotnet build -c Release` (analyzers clean, IDE0005/IDE0007), `dotnet format --verify-no-changes`, all commands in plan §5.4 green, `git diff --stat -- '**/Migrations/**'` empty, `PrimitiveBoundaryTests` + NetArchTest boundary suite green unmodified, coverage gates (Domain >= 90%, Shared >= 90%).

## Phase 4 — Post-green tests

- [ ] **T040 [P] [US2]** `test-writer`: direct unit rows for `RtspUrl.FromStored` / `StreamSourceUrl.FromStored` (accepts every §5 hydrate row; still rejects blank, over-length, non-`rtsp://`, userinfo with today's messages) in `RtspUrlFromStoredTests.cs` / `StreamSourceUrlFromStoredTests.cs` beside the existing VO tests, and direct rows for `UrlAuthority.AtSignFollowsTheAuthority` in `tests/Shared.Kernel.Tests/UrlAuthorityTests.cs` (null guard; the predicate's true/false table from spec §1.1). Green on arrival is expected here: these cover code that now exists, they are not the new-behaviour evidence.
- [ ] **T041 [US1]** `test-writer`: G1 `tests/Architecture.Tests/FromStoredCallSiteTests.cs` (FR-008) — slash-normalised source scan; allowed sites = the two `*Configuration.cs` files and the declaring files; assert the scan finds exactly those two call sites (not vacuous); counterfactual run recorded in the class summary.

## Phase 5 — Adversarial, verify, review, PR

- [ ] **T050 [US1-3]** `test-adversary`: attack the predicate at the write boundary — case and percent variants (`%40` vs raw), `\` authority markers, multiple `@`, `@` at the 2048th character, IPv6 + `@`, leading/trailing whitespace, a URL that passes `FromStored` but not `From` sent via PATCH, and an `Idempotency-Key` replay across the change (spec A-3). Also try to reach `FromStored` from a write path. Findings → tests or written acceptance.
- [ ] **T051 [US1-2]** Phase 5 `/verify`: spec §8 procedure on a live stack (stop it before building; check the AppHost PID start time is after the last commit). Latency: N/A.
- [ ] **T060 [US1-3]** Phase 6: `backend-reviewer` and `security-reviewer` (credentials in URLs, trust boundary in StreamDistribution, `FromStored` bypass, error text leakage, replay behaviour). Resolve or accept in writing.
- [ ] **T070** Phase 7: PR to `develop` (`--base develop`), body quotes T026 output, lists plan §6 edits, flags spec A-1/A-2 for the owner, re-checks spec number 389, `Closes #2999`. Add the class to the shard filter if not done in T015. After merge: confirm #2999 closed.

## Dependencies

```
T001 → T010..T016 [P] (gate: green) → T020..T025 [P] → T026 (gate: red quoted)
T026 → T030 → (T031 [P] → T033) + (T032 [P] → T034) + T035 [P] → T036
T036 → T040 [P], T041 → T050 → T051 → T060 → T070
```

Foundational: **T030** (Shared.Kernel) blocks both value-object tasks. After it, CameraCatalog (T031/T033) and
StreamDistribution (T032/T034/T035) fan out in parallel.
