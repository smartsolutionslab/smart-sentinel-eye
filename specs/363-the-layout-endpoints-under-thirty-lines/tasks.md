# Tasks 363: The layout endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2929 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T004 must be
seen **passing on the unrefactored code (`0296275e`)** before any production task starts, and must
pass **byte-unmodified** after every refactor commit. An assertion that has to be edited after a
refactor commit is evidence the behaviour moved: block, do not adjust. A characterisation test that
arrives red before any production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer (T010-T016). **Test-writer:** T001-T004.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves `tests/Architecture.Tests` green and runs the plan §5
build.

**Forbidden in every task:** any file outside `src/LayoutComposition/Api/` and the new test files +
`Architecture.Tests` csproj; any existing test file; any guard; suppressions, thresholds,
`.editorconfig`, `SonarLint.xml`; migrations; `Shared.Contracts`; `BranchDraft`.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 build output on `0296275e` (10 S138 +
  10 S107 in `LayoutComposition.Api`) and the `dotnet test tests/Architecture.Tests -c Release`
  summary line.

## Phase 2: Characterisation (test-writer; blocks Phase 3)

Sequential: T001 owns the csproj and host the others use.

- [ ] **T001** [US1] Add `<PackageReference Include="Moq" />` to
  `tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj` and create
  `tests/Architecture.Tests/LayoutCompositionEndpointHost.cs` (plan §4 C0).
  Commit `test(layout-composition): an in-process host for the layout and wall endpoints`.
- [ ] **T002** [P] [US1] C1, `tests/Architecture.Tests/LayoutCompositionRouteTableCharacterisationTests.cs`
  — route-table baseline captured on `0296275e` as a raw-string literal.
  Commit `test(layout-composition): pin the layout and wall route table`.
- [ ] **T003** [P] [US1] C2, `tests/Architecture.Tests/LayoutEndpointCharacterisationTests.cs`
  — spec §4.2 rows CreateDraft, Publish, Archive, Revert, EditDraft.
  Commit `test(layout-composition): pin layout command refusals, order and dispatch`.
- [ ] **T004** [P] [US1] C3, `tests/Architecture.Tests/WallEndpointCharacterisationTests.cs`
  — spec §4.2 rows CreateWall, EditScenes, Switch.
  Commit `test(layout-composition): pin wall command refusals, order and dispatch`.
- [ ] **T005** Gate: run `dotnet test tests/Architecture.Tests -c Release` on the T001-T004 tip
  (production code still `0296275e`); quote the verbatim pass output for the PR body. Any case
  dropped as infeasible is listed with the CI integration test that observes it instead (plan §4).

## Phase 3: Group A — layouts (backend-engineer; after T005)

Sequential (same files).

- [ ] **T010** [US1] `LayoutCompositionFabResolution.ForCallerFabsAsync` (plan §3.1) and
  `LayoutEndpoints.RevisionOutcome` (§3.2); use both in `Publish`, `Archive`, `Revert`.
  Commit `refactor(layout-composition): share the caller-fab tail of the revision commands`.
- [ ] **T011** [US1] `EditDraft` via `BoundaryParse` + `ParseDraftEdit` + `ForCallerFabsAsync` +
  `RevisionOutcome`; `CreateDraft` via `BoundaryParse` + `ParseCreateLayout`, hoisted command,
  `CreateExecution` (§3.3, §3.4).
  Commit `refactor(layout-composition): split the layout create and edit endpoints`.
- [ ] **T012** [US1] `MapLayoutEndpoints` → one `Map<WithName>(RouteGroupBuilder group)` helper per
  mapping, same order, chains byte-identical (§3.5).
  Commit `refactor(layout-composition): map each layout route in its own method`.
- [ ] **T013** Checkpoint: plan §5 build shows 4 S138 (walls only) + the same 10 S107; C1-C3 and all
  Architecture.Tests green unmodified.

## Phase 4: Group B — walls (backend-engineer; after T013)

- [ ] **T014** [US1] `EditScenes` and `Switch` via `BoundaryParse` (existing `ParseScenes` /
  `ParseSceneTarget`) + `ForCallerFabsAsync`, inline `Match` kept (§3.1, §3.3).
  Commit `refactor(layout-composition): share the caller-fab tail of the wall commands`.
- [ ] **T015** [US1] `CreateWall` via `BoundaryParse` + `ParseCreateWall`, hoisted command,
  `CreateExecution` (§3.4).
  Commit `refactor(layout-composition): split the wall create endpoint`.
- [ ] **T016** [US1] `MapWallEndpoints` → per-mapping helpers (§3.5).
  Commit `refactor(layout-composition): map each wall route in its own method`.

## Phase 5: Verify (orchestrator)

- [ ] **T020** Plan §5 build: 0 S138 in `LayoutComposition.Api`, the same 10 S107 by file + method,
  no new warning; quote verbatim.
- [ ] **T021** `dotnet test tests/Architecture.Tests -c Release` green; `git diff origin/develop --
  tests/` shows only the four new files + csproj (no existing test touched).
- [ ] **T022** PR to `develop`: verification note names CI's integration shards as the observer for
  spec §4.3 and quotes T005's pre-change green run. Latency: N/A (spec §7).

## Dependencies

```
T000 → T001 → {T002, T003, T004} → T005 → T010 → T011 → T012 → T013 → T014 → T015 → T016 → T020 → T021 → T022
```

T002-T004 own disjoint files and may be written in parallel; everything in Phases 3-4 shares files
and is sequential. Group A and group B could in principle run in parallel (disjoint files except
`LayoutCompositionFabResolution.cs`, which T010 creates the helper in), but on one branch they land
in order; T014 depends on T010's helper.
