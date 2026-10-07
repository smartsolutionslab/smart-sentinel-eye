# Tasks 309: A path that outlived its row

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2658

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2658 carries `agent:ready`. The design decision was a human's
  (issue comment 6032165587); this spec only applies it.
- **Engineers:** `test-writer` (4a: xUnit unit facts + one Aspire integration class);
  `backend-engineer` (4b: `MediaMtxRtspGateway`, `IRtspGateway` doc, `ProvisionStreamCommandHandler`,
  one module comment). **No frontend, no infra.**
- **New ADR:** **no.** ADR-0161 §Decision already records this gap and names save-before-register
  as one of its two closures (spec §5).
- **Phase 4a colour: RED** (spec §6). Two declared assertion edits in
  `ProvisionStreamCommandHandlerTests` (spec §6 items 1-2), landed in T021 with the behaviour.
  `WhepAuthIntegrationTests` is **not touched**.
- **Latency:** N/A (spec §8).
- **Board:** #2658 is on Project #13 (In Progress, verified 2026-10-07). No per-task issues.
- **Shard filters:** one new Integration.Tests class → `shard-2.filter` entry (T012).

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4 prerequisites (orchestrator) — block everything below

- [ ] **T001** Re-check spec number 309 across open PRs and remote branches (spec header).

## US1 (P1) — A failed provisioning never leaves video reachable by every fab

### Phase 4a — tests first (test-writer)

- [ ] **T010** [P] [US1] `tests/StreamDistribution.Application.Tests/Fakes/InMemoryStreamRepository.cs`
  + `.../Commands/ProvisionStreamCommandHandlerTests.cs`: `OnSave` hook; the four facts of plan §6.1.
  No existing assertion edited in this task.
- [ ] **T011** [P] [US1] `tests/Integration.Tests/StreamDistribution/MediaMtxRtspGatewayIntegrationTests.cs`
  (new): the two facts of plan §6.2.
- [ ] **T012** [P] [US1] `tests/Integration.Tests/ci-shards/shard-2.filter`: entry for T011's class.
- [ ] **T013** [US1] Run on `0d1864f5`: `dotnet test tests/StreamDistribution.Application.Tests` and
  the Integration.Tests filter for `MediaMtxRtspGatewayIntegrationTests|ProvisionStreamIntegrationTests|MediaMtxReconcilerIntegrationTests|WhepAuthIntegrationTests`.
  Return verbatim output. Expected red, for the stated reason: the three red facts of §6.1 and
  `Adding_a_path_that_already_exists_succeeds` (400 `HttpRequestException`). Expected green:
  `Provision_for_a_retired_stream_does_not_re_register_its_path`,
  `Adding_a_path_MediaMTX_rejects_for_another_reason_still_throws`, and every characterisation class
  in spec §6. A new red fact arriving green is a phase-4 failure. Depends on T010–T012.

### Phase 4b — implementation (backend-engineer; may not edit tests except the two declared edits). Depends on T013.

Sequential: T021 depends on T020 (plan §7).

- [ ] **T020** [US1] `MediaMtxRtspGateway.AddPathAsync` existing-path branch (plan §3), new
  `[LoggerMessage]` in `src/StreamDistribution/Infrastructure/Log.cs`, `IRtspGateway.AddPathAsync`
  XML doc, `StreamDistributionInfrastructureModule` retry comment (plan §5). Commit 2.
- [ ] **T021** [US1] `ProvisionStreamCommandHandler` reorder + existing-row branch (plan §4); apply
  the two declared assertion edits (spec §6). Commit 3.
- [ ] **T022** [US1] Re-run T013's commands: all green; tests unmodified apart from the two declared
  edits. `dotnet format --verify-no-changes` and a Release build clean (analyzers as errors).

### Phase 5–7 (orchestrator)

- [ ] **T030** `/verify` per spec §7; verification note on the PR. Latency: N/A.
- [ ] **T031** Phase 6: `backend-reviewer` + `security-reviewer` (trust-boundary change: closes a
  cross-fab video exposure).
- [ ] **T032** PR to `develop` (`--base develop`), closing keyword `Closes #2658`; body quotes T013's
  red output, lists the two declared edits, and notes that ADR-0161's "Filed separately" gap is now
  closed (ADR text unchanged — ADR-0144). Re-run T001 first.

## Dependencies

T001 → T010/T011/T012 (parallel) → T013 → T020 → T021 → T022 → T030 → T031 → T032.
No foundational/shared-contract task; nothing to fan out beyond the three 4a test files.
