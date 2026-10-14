# Tasks 353: The camera catalog context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2895 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T004 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T004.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/` or `Migrations/`; any file under
`src/CameraCatalog/Domain/` (hit 3 is left, spec J2); any existing test file (including the
existing `Fakes/*`); both `Log.cs` files; `Shared.Contracts`; the listing handler's deconstruction
line; the rename handler's deconstruction line except plan §3.1's `expectedVersion` -> `_`;
suppressions, thresholds, `.editorconfig`, `SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (6 lines on `ef604f93`) and the
  three `dotnet test` runs (Domain 80, Application 76, Architecture).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [US1] C0, `tests/CameraCatalog.Application.Tests/Fakes/RecordingCameraRepository.cs`
  and `tests/CameraCatalog.Application.Tests/Fakes/CapturingLogger.cs` (new files; support for
  T002, no tests of their own). Blocks T002.
  Commit `test(camera-catalog): add a recording repository and a capturing logger`.
- [ ] **T002** [P] [US1] C1, `tests/CameraCatalog.Application.Tests/Commands/RenameRefusalOrderCharacterisationTests.cs`.
  Commit `test(camera-catalog): pin rename refusal order, calls and log text`.
- [ ] **T003** [P] [US1] C2, `tests/CameraCatalog.Application.Tests/Queries/ListCamerasRefusalOrderCharacterisationTests.cs`.
  Commit `test(camera-catalog): pin listing refusal order and filtered totals`.
- [ ] **T004** [P] [US1] C3, `tests/Architecture.Tests/Persistence/CameraCatalogModelCharacterisationTests.cs`.
  Commit `test(architecture): pin the camera catalog EF model's logical shape`.

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code, **plus** every plan §4 counterfactual
for that file observed red (verbatim failing assertion), then reverted (`git diff src/` empty before
committing).

Gate: the orchestrator holds three verbatim green outputs and, per file, the verbatim red of each
named counterfactual (C1: three, C2: two, C3: two).

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T005** [P] [US1] `src/CameraCatalog/Application/Commands/Handlers/RenameCameraCommandHandler.cs`:
  plan §3.1. Hit 1. C1 and `RenameCameraCommandHandlerTests` green unmodified; run
  `Architecture.Tests` (`HandlerDeconstructionTests`).
  Commit `refactor(camera-catalog): extract the rename's stale-version and taken-name refusals`.
- [ ] **T006** [P] [US1] `src/CameraCatalog/Application/Queries/Handlers/ListCamerasQueryHandler.cs`:
  plan §3.2. Hit 2. C2 and the three existing listing test classes green unmodified.
  Commit `refactor(camera-catalog): extract the listing's parameter refusals and visibility filters`.
- [ ] **T007** [P] [US1] `src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs`:
  plan §3.3. Hit 4. C3 green unmodified.
  Commit `refactor(camera-catalog): split camera EF configuration`.

Per task: run plan §5's grep (the task's hit gone, no new hit) and the test suites of the touched
layer plus `Architecture.Tests`. Report each comment on plan §3's re-read lists with a one-line
confirmation that it is still true (or, for §3.2's "the two above", the permitted naming edit).

## Phase 4: Verify (orchestrator)

- [ ] **T008** [US1] After evidence, verbatim: the §5 grep (exactly the 3 rows: `Camera.cs(37,34)`
  S107 plus the two migrations), the three test runs, `dotnet build -c Release` (0 errors),
  `dotnet format --verify-no-changes` on the two touched projects, coverage (Domain >= 90 %,
  Application >= 80 %). Confirm `git diff <T004 commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Domain/|/Log\.cs$'`
  is empty.
- [ ] **T009** Security-reviewer pass scoped to plan §7 (listing fab predicate before the count,
  rename lookup still fab-scoped and first, no new log of an address).
- [ ] **T010** PR to `develop` (`--base develop`). Body: T000 and T008 outputs, hit 3 left with its
  reason and the two migrations out of scope (spec §2), Phase 4a colour "characterisation" with the
  counterfactuals, judgement calls J1-J4, the three oddities pinned not fixed (spec §6), the
  analyzer-forced shapes (plan §6), latency "not on a §IV leg" with spec §7's reason,
  `Closes #2895`. Re-check spec number 353 is still free. Integration.Tests `CameraCatalog/*`
  observed in CI.

## Dependency graph

```
T000
T001 ──> T002 ─┐
T003, T004 ────┴──> T005, T006, T007 (all [P]) ──> T008 ──> T009 ──> T010
```

Phase 3 tasks own disjoint files and need no order among themselves. Nothing here is foundational
to another context: no `Shared.Kernel`, `Shared.Contracts`, `AppHost` or Aspire resource change.
