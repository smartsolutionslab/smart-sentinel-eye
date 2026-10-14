# Tasks 349: The system variables context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2891 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T007 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. Each also has its named mutation (plan §4) applied locally, observed red,
and reverted. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T007.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/`, `Domain/` or `Migrations/`; any existing test
file or shared test fake; both `Log.cs` files; `Shared.Contracts`; the `#pragma` in
`BindReverseIndexSeeder`; suppressions, thresholds, `.editorconfig`, `SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (16 lines on `782c1b70`), the
  four `dotnet test` runs (Domain 101, Application 113, Infrastructure 28, Architecture), and the
  `NFR_VariableResolutionLatencyTests` figure from the last green `develop` CI run's TRX.

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [P] [US1] C1, `tests/SystemVariables.Application.Tests/EventHandlers/ValueRequestRefusalOrderCharacterisationTests.cs`.
  Commit `test(system-variables): pin value-request refusal order, reservations and log text`.
- [ ] **T002** [P] [US1] C2, `tests/SystemVariables.Application.Tests/EventHandlers/ValueChangedFanOutCharacterisationTests.cs`.
  Commit `test(system-variables): pin value-change fan-out order, types and metadata`.
- [ ] **T003** [P] [US1] C3, `tests/SystemVariables.Application.Tests/EventHandlers/ArchivedFanOutCharacterisationTests.cs`.
  Commit `test(system-variables): pin archive fan-out order, types and metadata`.
- [ ] **T004** [P] [US1] C4, `tests/SystemVariables.Application.Tests/Resolution/VariableSnapshotBuilderOrderCharacterisationTests.cs`.
  Commit `test(system-variables): pin placeholder resolutions in label order`.
- [ ] **T005** [P] [US1] C5, `tests/SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederBranchCharacterisationTests.cs`.
  Commit `test(system-variables): pin reverse-index seed branches and counts`.
- [ ] **T006** [P] [US1] C6, `tests/Architecture.Tests/Persistence/SystemVariablesRegistrationCharacterisationTests.cs`.
  Commit `test(architecture): pin system variables infrastructure registrations`.
- [ ] **T007** [P] [US1] C7, `tests/SystemVariables.Infrastructure.Tests/SystemVariablesModelCharacterisationTests.cs`.
  Commit `test(system-variables): pin the EF model's logical shape`.

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code, plus the verbatim red output of its
named mutation (plan §4) and confirmation it was reverted (`git diff --stat -- src/` empty).

Gate: the orchestrator holds seven verbatim green outputs and seven verbatim red mutation outputs.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T008** [P] [US1] `Application/EventHandlers/SystemVariableValueRequestedV1Handler.cs`: plan §3.1. Hit 1.
  Per-event path: report that no allocation, I/O, lock or `await` was added (FR-004).
  Commit `refactor(system-variables): extract value-request fab, name and command steps`.
- [ ] **T009** [P] [US1] `Application/EventHandlers/VariableValueChangedDomainEventHandler.cs`: plan §3.2. Hits 5, 6.
  Per-event path: report that no new `async` method, `await`, allocation, closure or lock was added.
  Commit `refactor(system-variables): split the value-changed handler's mapping and name parse`.
- [ ] **T010** [P] [US1] `Application/EventHandlers/VariableArchivedDomainEventHandler.cs`: plan §3.3. Hit 3.
  Commit `refactor(system-variables): extract the archive handler's mapping and snapshot`.
- [ ] **T011** [P] [US1] `Application/Resolution/VariableSnapshotBuilder.cs`: plan §3.4 (including
  the reworded "below" comment). Hit 11.
  Commit `refactor(system-variables): extract the found-variable resolution row`.
- [ ] **T012** [P] [US1] `Infrastructure/Persistence/Configurations/VariableConfiguration.cs`: plan §3.5. Hit 13.
  Commit `refactor(system-variables): split variable EF configuration`.
- [ ] **T013** [P] [US1] `Infrastructure/Resolution/ReverseIndexSeederHostedService.cs`: plan §3.6. Hit 15.
  Commit `refactor(system-variables): extract seed status logging and the overlay loop`.
- [ ] **T014** [P] [US1] `Infrastructure/SystemVariablesInfrastructureModule.cs`: plan §3.7. Hit 16.
  Commit `refactor(system-variables): group infrastructure registrations`.

Per task: run plan §5's grep (the task's hits gone, no new hit) and the test suites of the touched
layer plus `Architecture.Tests` (T008-T010 must keep `EventMetadataFabDeclarationTests` and
`HandlerDeconstructionTests` green; T012-T014 also run `SystemVariables.Infrastructure.Tests`).

## Phase 4: Verify (orchestrator)

- [ ] **T015** [US1] After evidence, verbatim: the §5 grep (exactly the 8 "Leave" rows), all four
  test runs, `dotnet build -c Release` (0 errors), `dotnet format --verify-no-changes` on the two
  touched projects, coverage (Domain >= 90 %, Application >= 80 %). Confirm
  `git diff <T007 commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Log\.cs$|/Domain/|Shared\.Contracts'` is empty.
- [ ] **T016** PR to `develop` (`--base develop`). Body: T000 and T015 outputs, the 8 left hits
  each with its reason (spec §2), Phase 4a colour "characterisation" with the seven green and
  seven mutation-red outputs, the J1-J4 judgement calls, the four oddities left unchanged (spec
  §6), latency leg *Event → overlay state* unchanged by construction with the
  `NFR_VariableResolutionLatencyTests` figure before (develop TRX) and after (this PR's CI run and
  one re-run), `Closes #2891`. Re-check spec number 349 is still free.

## Dependency graph

```
T000
T001..T007 (all [P]) ──> T008..T014 (all [P]) ──> T015 ──> T016
```

Phase 3 tasks own disjoint files and need no order among themselves. Nothing here is foundational
to another context: no `Shared.Kernel`, `Shared.Contracts`, `AppHost` or Aspire resource change.
