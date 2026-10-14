# Tasks 351: The stream distribution context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2893 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T007 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T007. **Phase 6:** code-review **and**
security-reviewer (plan §7).

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/` or `Migrations/`; any existing test file or
fake; both `Log.cs`; any Domain file; `Shared.Contracts`; `BindMediaMtxOptions`,
`BindMediaMtxApiOptions`, `BindStreamFabAttribution`, `BindWhepAuthOptions`, `IsHttpsAbsolute`;
suppressions, thresholds, `.editorconfig`, `SonarLint.xml`, guards. The one shared CI file touched
is `tests/Integration.Tests/ci-shards/shard-2.filter`, by T007 only.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (12 lines on `8ded100e`), the
  four `dotnet test` runs (Domain 141, Application 86, Infrastructure 77, Architecture), and the
  last green `develop` CI run's `WhepHandshakeLatencyTests` p95 (trx artifact).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [US1] C0, `tests/StreamDistribution.Application.Tests/Fakes/RecordingStreamDistributionPorts.cs`
  (shared ordered call log over the existing fakes, by delegation). Blocks T002-T004.
  Commit `test(stream-distribution): add an ordered call log over the handler ports`.
- [ ] **T002** [P] [US1] C1, `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepRefusalOrderCharacterisationTests.cs`.
  Commit `test(stream-distribution): pin the WHEP refusal precedence and log text`.
- [ ] **T003** [P] [US1] C2 + C3, `tests/StreamDistribution.Application.Tests/Commands/RepointStreamOrderCharacterisationTests.cs`
  and `.../ReportStreamHealthPrecedenceCharacterisationTests.cs` (two files, one commit each:
  `test(stream-distribution): pin repoint save-before-gateway order` and
  `test(stream-distribution): pin health report precedence and default text`).
- [ ] **T004** [P] [US1] C4, `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepValidatorClaimOrderCharacterisationTests.cs`.
  Commit `test(stream-distribution): pin the WHEP validator's claim order`.
- [ ] **T005** [P] [US1] C5, `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs`.
  Commit `test(architecture): pin stream distribution infrastructure registrations`.
- [ ] **T006** [P] [US1] C6, `tests/StreamDistribution.Infrastructure.Tests/StreamDistributionModelCharacterisationTests.cs`.
  Commit `test(stream-distribution): pin the EF model's logical shape`.
- [ ] **T007** [P] [US1] C7, `tests/Integration.Tests/StreamDistribution/MediaMtxReconcilerPassCharacterisationTests.cs`
  **plus** its entry appended to `tests/Integration.Tests/ci-shards/shard-2.filter` in the same
  commit (a missing entry fails CI deterministically). Commit
  `test(stream-distribution): pin the reconcile pass order and summary counts`. Observed green per
  plan §4 "C7 observation" (local only if no other Aspire stack runs; else `workflow_dispatch` on
  the pushed test-only tip, read from the trx artifact).

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code, plus the plan §4 named mutation per
file, observed RED (failing line quoted) and reverted.

Gate: the orchestrator holds eight verbatim green outputs (T003 has two) and the C7 observation.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T010** [P] [US1] `Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs`: plan §3.1.
  Hits 1, 2. Commit `refactor(stream-distribution): extract the WHEP action and stream refusals`.
- [ ] **T011** [P] [US1] `Application/Commands/Handlers/RepointStreamCommandHandler.cs`: plan §3.2.
  Hit 3. Commit `refactor(stream-distribution): extract the repointable-stream check`.
- [ ] **T012** [P] [US1] `Application/Commands/Handlers/ReportStreamHealthCommandHandler.cs`: plan §3.3.
  Hit 4. Commit `refactor(stream-distribution): extract the health observation transition`.
- [ ] **T013** [P] [US1] `Infrastructure/Auth/WhepAuthValidator.cs`: plan §3.4. Hit 7.
  Commit `refactor(stream-distribution): split WHEP token validation from the metadata fetch`.
- [ ] **T014** [P] [US1] `Infrastructure/Persistence/Configurations/StreamConfiguration.cs`: plan §3.5.
  Hit 9. Commit `refactor(stream-distribution): split the stream EF configuration`.
- [ ] **T015** [US1] `Infrastructure/StreamDistributionInfrastructureModule.cs`, step 1: plan §3.7
  `AddMediaMtxGateway`. Hit 12. Commit
  `refactor(stream-distribution): move the MediaMTX probe log demotion out of the gateway registration`.
- [ ] **T016** [P] [US1] `Infrastructure/Reconciler/MediaMtxReconciler.cs`: plan §3.6. Hit 10.
  Requires the T007 before-observation. Commit
  `refactor(stream-distribution): extract the reconcile pass's remove and re-add loops`.
- [ ] **T017** [US1] same file as T015, step 2: plan §3.7 `AddStreamDistributionInfrastructure`.
  Hit 11. Depends on T015. Commit `refactor(stream-distribution): group infrastructure registrations`.

Per task: run plan §5's grep (the task's hits gone, no new hit, S104 still clear for the file) and
the test suites of the touched layer plus `Architecture.Tests` (T013-T017: also
`StreamDistribution.Infrastructure.Tests`). Every moved comment re-read in its new place (plan §3).

## Phase 4: Verify (orchestrator)

- [ ] **T020** [US1] After evidence, verbatim: the §5 grep (exactly the 3 "Leave" rows), all four
  test runs, `dotnet build -c Release` (0 errors), `dotnet format --verify-no-changes` on
  Application and Infrastructure, coverage (Domain >= 90 %, Application >= 80 %). Confirm
  `git diff <T007 commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Domain/|Log\.cs$'` is empty.
- [ ] **T021** Phase 6: code-review and security-reviewer (plan §7 checklist), findings resolved.
- [ ] **T022** PR to `develop` (`--base develop`). Body: T000 and T020 outputs, the 3 left hits
  each with its reason (spec §2), Phase 4a colour "characterisation" with T001-T007 outputs and the
  observed-RED mutations, the J1-J4 judgement calls, the four oddities left unchanged (spec §6),
  latency "no §IV leg; `WhepHandshakeLatencyTests` p95 before/after from CI", `Closes #2893`.
  Re-check spec number 351 is still free. Integration.Tests observed in CI.

## Dependency graph

```
T000
T001 ──> T002, T003 ; T004, T005, T006, T007 (all [P]) ──>
  T010, T011, T012, T013, T014, T016 [P] ; T015 ──> T017 ──> T020 ──> T021 ──> T022
```

Phase 3 tasks own disjoint files except T015 -> T017 (same file). T016 additionally waits for the
C7 before-observation. Nothing here is foundational to another context: no `Shared.Kernel`,
`Shared.Contracts`, `AppHost` or Aspire resource change.
