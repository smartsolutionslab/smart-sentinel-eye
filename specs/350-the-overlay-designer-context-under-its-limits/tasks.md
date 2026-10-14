# Tasks 350: The overlay designer context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2892 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T006 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T006.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/` or `Migrations/`; any existing test file
(including the existing `Fakes/*`); `Log.cs`; `OverlayElement.TextElement`'s signature;
`Shared.Contracts`; the deconstruction lines of both handlers; suppressions, thresholds,
`.editorconfig`, `SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (12 lines on `8ded100e`) and the
  three `dotnet test` runs (Domain 297, Application 49, Architecture).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [US1] C0, `tests/OverlayDesigner.Application.Tests/Fakes/RecordingOverlayRepository.cs`
  and `tests/OverlayDesigner.Application.Tests/Fakes/CapturingLogger.cs` (new files; support for
  T002-T003, no tests of their own). Blocks T002, T003.
  Commit `test(overlay-designer): add a recording repository and a capturing logger`.
- [ ] **T002** [P] [US1] C1, `tests/OverlayDesigner.Application.Tests/Commands/BranchDraftRefusalOrderCharacterisationTests.cs`.
  Commit `test(overlay-designer): pin branch-draft refusal order, calls and log text`.
- [ ] **T003** [P] [US1] C2, `tests/OverlayDesigner.Application.Tests/Commands/EditDraftRefusalOrderCharacterisationTests.cs`.
  Commit `test(overlay-designer): pin edit-draft refusal order, calls and log text`.
- [ ] **T004** [P] [US1] C3, `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayElementConstructionCharacterisationTests.cs`.
  Commit `test(overlay-designer): pin overlay element construction and deep copies`.
- [ ] **T005** [P] [US1] C4, `tests/Architecture.Tests/Persistence/OverlayDesignerRegistrationCharacterisationTests.cs`.
  Commit `test(architecture): pin overlay designer infrastructure registrations`.
- [ ] **T006** [P] [US1] C5, `tests/Architecture.Tests/Persistence/OverlayDesignerModelCharacterisationTests.cs`.
  Commit `test(architecture): pin the overlay designer EF model's logical shape`.

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code, **plus** the plan §4 counterfactual for
that file observed red (verbatim failing assertion), then reverted (`git diff src/` empty before
committing).

Gate: the orchestrator holds five verbatim green outputs and five verbatim red counterfactuals.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T007** [P] [US1] `Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs`:
  plan §3.1. Hit 1. C1 green unmodified.
  Commit `refactor(overlay-designer): extract the unrecoverable-branch refusals`.
- [ ] **T008** [P] [US1] `Application/Commands/Handlers/EditDraftRevisionCommandHandler.cs`:
  plan §3.2. Hit 2. C2 green unmodified.
  Commit `refactor(overlay-designer): extract the edit-draft revision refusals`.
- [ ] **T009** [P] [US1] `Domain/Overlay/OverlayElement.cs`: plan §3.3. Hit 3 (hit 4 stays, line
  moves). Rewrite the two constructor summaries that would become false. C3 and C5 green
  unmodified; run `Architecture.Tests` (PrimitiveBoundaryTests).
  Commit `refactor(overlay-designer): build overlay elements from one parts value`.
- [ ] **T010** [P] [US1] `Infrastructure/OverlayDesignerInfrastructureModule.cs`: plan §3.4. Hit 5.
  C4 green unmodified.
  Commit `refactor(overlay-designer): group infrastructure registrations by what they register`.
- [ ] **T011** [P] [US1] `Infrastructure/Persistence/Configurations/OverlayConfiguration.cs`:
  plan §3.5. Hit 6. C5 green unmodified; re-read the four positional comments listed in §3.5.
  Commit `refactor(overlay-designer): split overlay EF configuration`.

Per task: run plan §5's grep (the task's hit gone, no new hit) and the test suites of the touched
layer plus `Architecture.Tests`. Report each moved comment containing "above"/"below"/"this
branch"/"the line" with a one-line confirmation that it is still true.

## Phase 4: Verify (orchestrator)

- [ ] **T012** [US1] After evidence, verbatim: the §5 grep (exactly the 7 rows: `TextElement` plus
  six migrations), the three test runs, `dotnet build -c Release` (0 errors), `dotnet format
  --verify-no-changes` on the three touched projects, coverage (Domain >= 90 %, Application >= 80 %).
  Confirm `git diff <T006 commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Log\.cs$'` is empty.
- [ ] **T013** PR to `develop` (`--base develop`). Body: T000 and T012 outputs, hit 4 left with its
  reason and the six migrations out of scope (spec §2), Phase 4a colour "characterisation" with the
  five counterfactuals, judgement calls J1-J3, the three oddities pinned not fixed (spec §6),
  latency "not on a §IV leg" with spec §7's reason, `Closes #2892`. Re-check spec number 350 is
  still free. Integration.Tests `OverlayDesigner/*` observed in CI.

## Dependency graph

```
T000
T001 ──> T002, T003 ─┐
T004, T005, T006 ────┴──> T007..T011 (all [P]) ──> T012 ──> T013
```

Phase 3 tasks own disjoint files and need no order among themselves. Nothing here is foundational
to another context: no `Shared.Kernel`, `Shared.Contracts`, `AppHost` or Aspire resource change.
