# Tasks 345: The layout context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2887 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T007 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T007.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/` or `Migrations/`; any existing test file;
`Log.cs`; `Shared.Contracts`; suppressions, thresholds, `.editorconfig`, `SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (29 lines on `f30dced3`) and the
  four `dotnet test` runs (Domain 249, Application 150, Infrastructure 2, Architecture).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [P] [US1] C1, `tests/LayoutComposition.Domain.Tests/Layout/RevisionCreationCharacterisationTests.cs`.
  Commit `test(layout-composition): pin revision creation and owned-instance separation`.
- [ ] **T002** [P] [US1] C2, `tests/LayoutComposition.Application.Tests/Commands/DraftRefusalOrderCharacterisationTests.cs`.
  Commit `test(layout-composition): pin draft edit and branch refusal order`.
- [ ] **T003** [P] [US1] C3, `tests/LayoutComposition.Application.Tests/Walls/SwitchWallSceneOrderCharacterisationTests.cs`.
  Commit `test(layout-composition): pin manual wall switch refusal order and outcome logs`.
- [ ] **T004** [P] [US1] C4, `tests/LayoutComposition.Application.Tests/EventHandlers/WallSceneSwitchRequestLogSlotCharacterisationTests.cs`.
  Commit `test(layout-composition): pin rule-driven switch log text with distinct identifiers`.
- [ ] **T005** [P] [US1] C5, `tests/LayoutComposition.Application.Tests/Queries/ListLayoutsOrderingCharacterisationTests.cs`
  (+ `EventHandlers/LayoutRevisionPublishedTileMappingCharacterisationTests.cs` only if overlay
  Some/None mapping is not already pinned). Commit `test(layout-composition): pin layout listing order and picker mapping`.
- [ ] **T006** [P] [US1] C6, `tests/Architecture.Tests/Persistence/LayoutCompositionRegistrationCharacterisationTests.cs`.
  Commit `test(architecture): pin layout-composition infrastructure registrations`.
- [ ] **T007** [P] [US1] C7, `tests/LayoutComposition.Infrastructure.Tests/LayoutCompositionModelCharacterisationTests.cs`
  + `LayoutCompositionModelBaseline.cs`. Commit `test(layout-composition): pin the EF model`.

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code.

Gate: the orchestrator holds seven verbatim green outputs.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T010** [P] [US1] `Domain/Layout/Revision.cs` + `Domain/Layout/Layout.cs` (one commit; they
  only build together): plan §3.1. Clears hits 18, 19. Commit
  `refactor(layout-composition): pass revision creation as one value object`.
- [ ] **T011** [P] [US1] `Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs`: plan §3.2. Hit 1.
  Commit `refactor(layout-composition): extract branch refusal from BranchDraftRevision handler`.
- [ ] **T012** [P] [US1] `Application/Commands/Handlers/EditDraftRevisionCommandHandler.cs`: plan §3.3. Hit 2.
  Commit `refactor(layout-composition): extract edit refusals from EditDraftRevision handler`.
- [ ] **T013** [P] [US1] `Application/Commands/Handlers/SwitchWallSceneCommandHandler.cs`: plan §3.4. Hits 3, 4.
  Commit `refactor(layout-composition): extract switch refusals and outcome log`.
- [ ] **T014** [P] [US1] `Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs`: plan §3.5. Hit 5.
  Commit `refactor(layout-composition): extract contract tile projection`.
- [ ] **T015** [P] [US1] `Application/EventHandlers/WallSceneSwitchRequestedV1Handler.cs`, step 1:
  introduce `SwitchRequestTrace` and `PlannedSwitch`, re-sign the private helpers (plan §3.6). Hits 8, 9, 10.
  Commit `refactor(layout-composition): bundle switch-request log identifiers`.
- [ ] **T016** [US1] same file, step 2: split `Handle` (plan §3.6). Hit 7. Depends on T015.
  Commit `refactor(layout-composition): split rule-driven switch Handle by phase`.
- [ ] **T017** [P] [US1] `Application/Queries/Handlers/ListLayoutsQueryHandler.cs`: plan §3.7. Hit 16.
  Commit `refactor(layout-composition): extract the two listing shapes`.
- [ ] **T018** [P] [US1] `Infrastructure/LayoutCompositionInfrastructureModule.cs`: plan §3.8. Hit 22.
  Commit `refactor(layout-composition): group infrastructure registrations`.
- [ ] **T019** [P] [US1] `Infrastructure/Persistence/Configurations/LayoutConfiguration.cs`: plan §3.9. Hit 23.
  Commit `refactor(layout-composition): split layout EF configuration`.
- [ ] **T020** [P] [US1] `Infrastructure/Persistence/Configurations/WallConfiguration.cs`: plan §3.9. Hit 24.
  Commit `refactor(layout-composition): split wall EF configuration`.

Per task: run plan §5's grep (the task's hits gone, no new hit) and the test suites of the
touched layer plus `Architecture.Tests` (T018-T020: also `LayoutComposition.Infrastructure.Tests`).

## Phase 4: Verify (orchestrator)

- [ ] **T021** [US1] After evidence, verbatim: the §5 grep (exactly the 14 "Leave" rows), all four
  test runs, `dotnet build -c Release` (0 errors), `dotnet format --verify-no-changes` on the three
  projects, coverage (Domain >= 90 %, Application >= 80 %). Confirm
  `git diff <T007 commit>..HEAD -- tests/` is empty and `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/'` is empty.
- [ ] **T022** PR to `develop` (`--base develop`). Body: T000 and T021 outputs, the 14 left hits each
  with its reason (spec §2), Phase 4a colour "characterisation", the J1/J2 judgement calls, the
  `SaveAsync`-on-no-op oddity left unchanged, latency "N/A", `Closes #2887`. Re-check spec number
  345 is still free. Integration.Tests observed in CI.

## Dependency graph

```
T000
T001..T007 (all [P]) ──> T010, T011, T012, T013, T014, T015 ──> T016, T017, T018, T019, T020 ──> T021 ──> T022
```

Phase 3 tasks own disjoint files (except T015 -> T016, same file) and need no order among
themselves. Nothing here is foundational to another context: no `Shared.Kernel`,
`Shared.Contracts`, `AppHost` or Aspire resource change.
