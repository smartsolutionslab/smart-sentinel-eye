# Tasks 318: The path a retire took away

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2743

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2743 carries `agent:ready`. The design decision (optimistic
  concurrency on the stream row) was a human's, on the issue, 2026-10-08.
- **Engineers:** `test-writer` + **`test-adversary`** (4a, disjoint files); `backend-engineer`
  (4b: `IStreamRepository`, `StreamRepository`, `ProvisionStreamCommandHandler`, `Log.cs`).
  **No frontend, no infra.**
- **New ADR:** **no** — ADR-0113 Layer 2 applied to a handler-vs-handler race, no retry
  (spec §5). **One point flagged for the phase-6 reviewer:** the token is used as a *non-bumping*
  assertion with compensation, a form ADR-0113 does not describe; if judged an amendment, stop.
- **Phase 4a colour: RED** (spec §6). **Declared assertion edits: none.**
- **test-adversary: yes.** The issue is a race; `/next-issue` says to add it for failure modes.
  It owns the facts whose job is to make wrong-but-plausible implementations fail (IT-3..IT-6) and
  runs their counterfactuals.
- **Latency:** N/A (spec §8).
- **Board:** #2743 is on Project #13 (In Progress, verified 2026-10-08). No per-task issues.
- **Shard filters:** two new Integration.Tests classes (T013).
- **Migration:** none (spec §1.2).

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4 prerequisites (orchestrator) — block everything below

- [ ] **T001** Re-check spec number 318 across open PRs, remote **and local** branches (spec header).

No foundational task: no `Shared.Kernel`, `Shared.Contracts` or AppHost change, so nothing blocks a
fan-out except T001.

## US1 (P1) — A retired camera stays unwatchable even if a provision was in flight

### Phase 4a — tests first. T010, T011, T012 run in parallel; T013 by whichever finishes last.

- [x] **T010** [P] [US1] **test-writer** —
  `tests/StreamDistribution.Application.Tests/Fakes/InMemoryStreamRepository.cs` (versioned fake +
  the two plan §3 members as plain methods, plan §6.1) and
  `tests/StreamDistribution.Application.Tests/Commands/ProvisionStreamCommandHandlerTests.cs`:
  facts **A1–A5** (plan §6.1). No existing fact edited.
- [x] **T011** [P] [US1] **test-writer** —
  `tests/Integration.Tests/StreamDistribution/StreamRepositoryVersionAssertionIntegrationTests.cs`
  (new): **IT-1, IT-2** (plan §6.2).
- [x] **T012** [P] [US1] **test-adversary** —
  `tests/Integration.Tests/StreamDistribution/ProvisionRetireRaceIntegrationTests.cs` (new):
  **IT-3–IT-6** (plan §6.3) plus one adversarial angle of its own, or a written reason it is not
  feasible.
- [x] **T013** [P] [US1] `tests/Integration.Tests/ci-shards/shard-2.filter`: entries for T011's and
  T012's classes (plan §6.5).
- [x] **T014** [US1] Run on the unmodified `src/` tree:
  `dotnet test tests/StreamDistribution.Application.Tests` and the Integration.Tests filter
  `StreamRepositoryVersionAssertionIntegrationTests|ProvisionRetireRaceIntegrationTests|ProvisionStreamIntegrationTests|RetireStreamIntegrationTests|MediaMtxReconcilerIntegrationTests|StreamHealthTransitionTests`.
  Return **verbatim** output. Expected:
  - **Red, runtime:** A1, A2, A4 (path present / `Success` instead of 503).
  - **Red, compile:** Integration.Tests, naming `IsUnchangedSinceLoadAsync` /
    `ReadCommittedStateAsync` (spec 302 precedent). Then run the characterisation integration
    classes with T011/T012's files temporarily excluded, so their green is observed too.
  - **Green:** A3, A5, every existing fact (spec §6, plan §6.4).
  A red fact arriving green, or a characterisation fact arriving red, is a phase-4 failure.
  Depends on T010–T013.

### Phase 4b — implementation (backend-engineer; **may not edit tests**). Depends on T014.

- [x] **T020** [US1] `src/StreamDistribution/Domain/Stream/IStreamRepository.cs` +
  `src/StreamDistribution/Infrastructure/Persistence/StreamRepository.cs`: the two members, exact
  plan §3 signatures and §3.1 bodies. Record whether `ExecuteUpdateAsync` translated or the
  `ExecuteSqlInterpolatedAsync` fallback was needed (spec §9 A2).
- [x] **T021** [US1] `ProvisionStreamCommandHandler.RegisterPathAsync` per plan §4.1;
  `src/StreamDistribution/Application/Log.cs` per plan §5. Depends on T020 (same commit, plan §7
  commit 2).
- [x] **T022** [US1] Re-run T014's commands: all green, tests unmodified.
  **test-adversary** then applies each plan §6.3 counterfactual (tracked bumping save; `AsNoTracking`
  read; decision on `stream.State`) plus "compensate on any mismatch" against A3, observes each red,
  reverts, and returns the verbatim red lines. `dotnet format --verify-no-changes`; Release build
  clean (analyzers as errors).

## US2 (P2) — A redelivery onto a retired stream finishes its teardown

Separable commit (plan §7 commit 3). Runs after US1's T022 because both touch
`ProvisionStreamCommandHandler.cs`.

- [x] **T030** [US2] **test-writer** — `ProvisionStreamCommandHandlerTests.cs`: facts **B1, B2**
  (plan §6.1). Run; return verbatim output. Expected red: B1 (no removal), B2 (`Success`).
  `Provision_for_a_retired_stream_does_not_re_register_its_path` must stay green.
- [x] **T031** [US2] **backend-engineer** — `Retired` branch per plan §4.2. Depends on T030.
- [x] **T032** [US2] Re-run T014's commands + B1/B2: all green, unmodified.

## Phase 5–7 (orchestrator)

- [ ] **T040** `/verify` per spec §7 (incl. the US2 planted-path step); verification note on the
  PR. Latency: N/A.
- [x] **T041** Phase 6: `backend-reviewer` (explicitly asked to judge spec §5's flag) +
  `security-reviewer` (a retired camera's video reachability). Findings resolved or accepted in
  writing.
- [ ] **T042** PR to `develop` (`--base develop`), `Closes #2743`; body quotes T014's red output and
  T022's counterfactual reds, states "no declared assertion edits", names the A2 outcome, and
  recommends the spec §9 R2 follow-up (retire vs health-watcher conflict). Re-run T001 first. After
  merge, confirm #2743 actually closed (memory: *a PR mention rarely auto-closes the issue*).

## Dependencies

```
T001 → { T010 ∥ T011 ∥ T012 ∥ T013 } → T014 → T020 → T021 → T022
     → T030 → T031 → T032 → T040 → T041 → T042
```

Fan-out exists only in 4a (three agents-worth of disjoint files: Application tests, repository
integration tests, race integration tests, plus the shard filter). 4b is one engineer, sequential —
the three source files are small and two of them are touched by both stories.
