# Tasks 366: Four Api projects' endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2931 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T010 must be
seen **passing on the unrefactored code (`39b0ef64`)**, in Release and (route tables) Debug, before
any production task starts, and must pass **byte-unmodified** after every refactor commit. An
assertion that has to be edited after a refactor commit is evidence the behaviour moved: block, do
not adjust. A characterisation test that arrives red before any production edit is a test defect
and is fixed in the test.

**Engineer:** backend-engineer (T020-T031). **Test-writer:** T001-T010.

**Commits.** Conventional Commits, no `Co-Authored-By` (ADR-0086), never `--no-verify`. Each
commit builds **and passes** `tests/Architecture.Tests` on its own; each refactor commit also runs
plan §5's build.

**Forbidden in every task:** any production file other than the 7 in plan §2; any existing test
file (including spec 341's, 363's and 365's); any guard; suppressions, thresholds, `.editorconfig`,
`SonarLint.xml`; csproj files; migrations; `Shared.Contracts`; methods outside spec §1.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). On one
branch the commits still land one after another.

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: plan §5 build on `39b0ef64` (10 S138 + 15 S107) and
  the `dotnet test tests/Architecture.Tests -c Release` summary line (incl.
  `KioskLatencyRecordingCharacterisationTests` green).

## Phase 2: Characterisation (test-writer; blocks Phases 3-6)

**Foundational, blocks the rest:** T001 (shared helpers).

- [ ] **T001** [US1] `tests/Architecture.Tests/RouteTableRendering.cs` (H1) and
  `tests/Architecture.Tests/InProcessEndpointDriver.cs` (H2), plan §4.
  Commit `test(architecture): a shared route-table renderer and in-process endpoint driver`.
- [ ] **T002** [P] [US1] C1a-d: the four `*RouteTableCharacterisationTests.cs` (plan §4), baselines
  captured on `39b0ef64`; run in Release **and** Debug.
  Commit `test(architecture): pin the overlay, variable, stream and camera route tables`.
- [ ] **T003** [P] [US1] H3 `OverlayDesignerEndpointHost.cs` + C2
  `OverlayDraftEndpointCharacterisationTests.cs` (spec §4.2 `CreateDraft`, `EditDraft`).
  Commit `test(overlay-designer): pin draft create and edit refusals, order and dispatch`.
- [ ] **T004** [P] [US1] H4 `SystemVariablesEndpointHost.cs` + C3
  `SystemVariableDefineEndpointCharacterisationTests.cs` (spec §4.2 `Define`).
  Commit `test(system-variables): pin define refusals, order and dispatch`.
- [ ] **T005** [P] [US1] H5 `CameraCatalogEndpointHost.cs` + C4
  `CameraRegisterEndpointCharacterisationTests.cs` (spec §4.2 `Register`).
  Commit `test(camera-catalog): pin register refusals, order and dispatch`.
- [ ] **T006** [US1] C5 `CameraPatchEndpointCharacterisationTests.cs` (spec §4.2 `Patch`; uses H5,
  so after T005). Every adjacent pair of the fab → If-Match → exactly-one → identifier → operator →
  parse order gets one case.
  Commit `test(camera-catalog): pin the patch refusal order and both dispatches`.
- [ ] **T010** Gate: `dotnet test tests/Architecture.Tests -c Release` and the Debug route-table
  filter (plan §5) on the T001-T006 tip, production still `39b0ef64`; quote verbatim for the PR. Any
  case dropped as infeasible is listed with the spec §4.3 CI test that observes it instead.

## Phase 3: G1 StreamDistribution (backend-engineer; after T010)

- [ ] **T020** [US1] `RecordKioskLatency` → `UnknownMeasurement()` (plan §3.3).
  Commit `refactor(stream-distribution): name the unknown-measurement refusal`.
- [ ] **T021** [US1] `MapStreamEndpoints` → `MapGetStream`, `MapListStreams`, `MapAuthorizeWhep`,
  `MapRecordKioskLatency` on `RouteGroupBuilder group` (plan §3.1).
  Commit `refactor(stream-distribution): map each stream route in its own method`.
- [ ] **T022** Checkpoint: plan §5 build → 8 S138, 15 S107; Architecture.Tests green unmodified.

## Phase 4: G2 SystemVariables (after T022)

- [ ] **T023** [US1] `Define` via `BoundaryParse` + `ParseDefinition` + `CreateExecution` (plan §3.2).
  Commit `refactor(system-variables): split the define endpoint`.
- [ ] **T024** [US1] `MapSystemVariableEndpoints` → one helper per mapping on `group` (plan §3.1).
  Commit `refactor(system-variables): map each variable route in its own method`.
- [ ] **T025** Checkpoint: 6 S138, 15 S107; tests green unmodified.

## Phase 5: G3 OverlayDesigner (after T025)

- [ ] **T026** [US1] `CreateDraft` via `ParseCreateOverlay` + hoisted command + `CreateExecution`;
  `EditDraft` via `ParseDraftEdit`, `Guid.Empty` refusal and `If-Match` read kept inline (plan §3.2,
  §3.6).
  Commit `refactor(overlay-designer): split the draft create and edit endpoints`.
- [ ] **T027** [US1] `MapOverlayEndpoints` → one helper per mapping on `group`.
  Commit `refactor(overlay-designer): map each overlay route in its own method`.
- [ ] **T028** Checkpoint: 3 S138, 15 S107; tests green unmodified.

## Phase 6: G4 CameraCatalog (after T028) — security-sensitive ordering

- [ ] **T029** [US1] `Register` via `ParseRegistration` + `CreateExecution` (plan §3.5).
  Commit `refactor(camera-catalog): split the register endpoint`.
- [ ] **T030** [US1] `Patch` per plan §3.4 (`ExactlyOneAttribute`, `PatchAttributeAsync`), order
  unchanged.
  Commit `refactor(camera-catalog): dispatch a camera patch through one attribute helper`.
- [ ] **T031** [US1] `MapCameraCatalogEndpoints` → `MapRegisterCamera`, `MapRetireCamera`,
  `MapPatchCamera` on `RouteGroupBuilder writes`; `MapGetCamera`, `MapListCameras` on
  `RouteGroupBuilder reads` (plan §3.1 — the names are load-bearing).
  Commit `refactor(camera-catalog): map each camera route in its own method`.

## Phase 7: Verify and review (orchestrator)

- [ ] **T040** Plan §5 build: 0 S138 in the four projects, the same 15 S107 by file + method, no
  other new Sonar warning; quote verbatim.
- [ ] **T041** `dotnet test tests/Architecture.Tests -c Release` + Debug route-table filter green;
  `git diff origin/develop --stat -- tests/` shows only new files (no existing test touched).
- [ ] **T042** Phase 6 runs `/code-review` **and** `/security-review` (spec §2 auth scenarios:
  `Patch` fab-first order, the three idempotency scopes, `RecordKioskLatency` discriminator,
  `/streams/authorize` `AllowAnonymous` + rate limit).
- [ ] **T043** PR to `develop` (`--base develop`): verification note quotes T010's pre-change run and
  T040/T041, names spec §4.3's integration tests as the CI observer, latency N/A (spec §7), J1 shift
  stated.

## Dependencies

```
T000 → T001 → {T002, T003, T004, T005} → T006 → T010
     → T020 → T021 → T022 → T023 → T024 → T025 → T026 → T027 → T028 → T029 → T030 → T031
     → T040 → T041 → T042 → T043
```

T002-T005 own disjoint files and may be written in parallel (T006 shares H5 with T005). The four
production groups own disjoint files and could be implemented in parallel worktrees, but they land
in order on one branch (one branch, one index); the order is lowest risk first so a stall in G4
leaves G1-G3 shippable (plan §6 split fallback).
