# Tasks 342: The simulator's two tangled methods

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2897 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 and T002 must be
seen **passing on the unrefactored code** before T004/T005 start, and must pass **unmodified**
after. An assertion that has to be edited after T004/T005 is evidence the behaviour moved: block,
do not adjust. A characterisation test that arrives red before any production edit is a test defect
and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001, T002.

**Commits.** One commit per task that changes files (T001, T002, T004, T005), Conventional Commits,
no `Co-Authored-By` (ADR-0086). Each commit must build **and pass** on its own: the test commits add
green tests against unchanged production code, and each refactor commit leaves the suite green.

Legend: `[P]` means parallel with the other `[P]` task in its phase (disjoint files). Agents that
run in parallel on this one branch still commit one after the other (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record the "before" evidence verbatim for the PR body: the S1541 grep (plan §5), the
  same grep with `S138|S107` for the two files, `dotnet test tests/ScenarioSimulator.Tests -c Release`
  (162 passed on 2026-10-13), and `AppHostE2ESwitchTests` (plan §5).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [P] [US1] Create `tests/ScenarioSimulator.Tests/ClipManifestValidationOrderingTests.cs`
  with the six case groups of plan §4.1, exact-list assertions. Run
  `dotnet test tests/ScenarioSimulator.Tests -c Release --filter "FullyQualifiedName~ClipManifestValidationOrderingTests"`
  and return the verbatim output: all green on unrefactored code. Commit:
  `test(scenario-simulator): pin clip-manifest violation text and order`.
- [ ] **T002** [P] [US1] Create `tests/ScenarioSimulator.Tests/SimulatorRuntimeBindingTests.cs` with
  the seven cases of plan §4.2 (reflection on the `g__BindRuntime|` local function, invoked with
  `DoNotWrapExceptions`). Run
  `dotnet test tests/ScenarioSimulator.Tests -c Release --filter "FullyQualifiedName~SimulatorRuntimeBindingTests"`
  and return the verbatim output: all green on unrefactored code. Commit:
  `test(scenario-simulator): pin BindRuntime precedence, defaults and failure messages`.

Gate between phases: the orchestrator has both verbatim green outputs in hand.

## Phase 3: Refactor (backend-engineer; depends on T001 and T002)

- [ ] **T004** [P] [US1] `src/ScenarioSimulator/Cues/ClipManifestValidation.cs` only: extract named
  private static methods per plan §3.1. Do not touch any test. Run the ScenarioSimulator.Tests suite
  and the S1541/S138/S107 grep. Commit:
  `refactor(scenario-simulator): split ValidateOneCue into one decision per method`.
  Depends on T001.
- [ ] **T005** [P] [US1] `src/ScenarioSimulator/Program.cs` only: extract static local functions from
  `BindRuntime` per plan §3.2 (name and signature of `BindRuntime` unchanged). Do not touch any test.
  Run the ScenarioSimulator.Tests suite and the grep. Commit:
  `refactor(scenario-simulator): split BindRuntime's setting resolution into named helpers`.
  Depends on T002.

## Phase 4: Verify (orchestrator)

- [ ] **T006** [US1] After evidence, verbatim: the S1541 grep (no `ScenarioSimulator` line), the
  S138/S107 grep for the two files (state which cleared, which remain and why),
  `dotnet test tests/ScenarioSimulator.Tests -c Release`, `AppHostE2ESwitchTests`,
  `dotnet test tests/Architecture.Tests -c Release`, `dotnet build -c Release` (0 errors) and
  `dotnet format --verify-no-changes` on the two touched projects. Confirm
  `git diff <T002 commit>..HEAD -- tests/` is empty (tests unmodified).
- [ ] **T007** PR to `develop` (`--base develop`), body quoting T000 and T006, Phase 4a colour
  "characterisation", latency "N/A: dev-only simulator, not on the event-to-overlay path", and
  `Closes #2897`. Re-check that no other branch has taken spec number 342 first.

## Dependency graph

```
T000 ─┐
T001 ─┼─> T004 ─┐
T002 ─┴─> T005 ─┴─> T006 ─> T007
```

T004 and T005 own disjoint files and may run in parallel; T001 and T002 likewise. Nothing here is
foundational to another context: no `Shared.Kernel`, `Shared.Contracts` or `AppHost` change.
