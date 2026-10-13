# Tasks 341: The endpoint files, split by what they hold

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2896. This is a
feature-level issue that is already on Project #13. No per-task issues are created.

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 and T002 are
written first and observed **passing** on unchanged code. After the change they must pass with
their files byte-identical. An assertion that has to be edited is a block, not an adjustment.

**Engineer:** backend-engineer. **Test-writer:** T001, T002 and T003. **Guards:** none edited.
**Suppressions or threshold edits:** none.

Legend: `[P]` means the task owns files disjoint from the other `[P]` tasks in its phase
(ADR-0109). They are still committed one after another on one branch. Each task is **one
commit, and each commit builds on its own** (FR-007): run `dotnet build -c Release` on the
touched Api project before committing. Commit type: `refactor(<context>): …` for production
tasks, `test(<context>): …` for T001 and T002. Use no `Co-Authored-By` footer (ADR-0086).

---

## Phase 1: Characterisation first (blocks Phase 2)

- [ ] **T001 [US1]** New file `tests/Architecture.Tests/KioskLatencyRecordingCharacterisationTests.cs`,
  covering every case in plan §3.1. The test drives the mapped endpoint's `RequestDelegate` and
  uses a `MeterListener`. Run it on unchanged code and observe it **green**. Capture the 400
  bodies from that run as the expected literals. Commit alone.
- [ ] **T002 [US1]** New file `tests/Architecture.Tests/RuleActionBuildCharacterisationTests.cs`,
  covering every case in plan §3.2. It calls `BuildAction` by reflection, and the file states
  why. Observe it **green**. Commit alone.
- [ ] **T003 [US1]** Capture the baseline **verbatim**, with no commit. This is the
  before-evidence for the PR body:
  1. the gate grep (plan §4.1), which must list the 8 hits in spec §1;
  2. the full `dotnet test tests/Architecture.Tests -c Release` run;
  3. the Integration.Tests filter (plan §4.3), or the CI-artifact fallback that plan §4
     describes, with the run id quoted.

## Phase 2: The eight hits (depends on T001 to T003)

Each task's done-check is in three parts. Its hit disappears from the gate grep. The
`Architecture.Tests` focused subset (plan §4.2) stays green. T001 and T002 are unmodified.

- [ ] **T004 [P] [US1]** `src/Automation/Api/RulesEndpoints.cs`, **S1541 `BuildAction`**:
  extract the three arms into `BuildSetVariableValue`, `BuildHighlightOverlay` and
  `BuildSwitchWallScene` (plan §2.1). Keep `BuildAction`'s name and signature, and keep the
  messages byte-identical. T002 must stay green.
- [ ] **T005 [US1]** (after T004, same file) **S104 `RulesEndpoints.cs`**: mark the class
  `partial`, then move `List`, `GetOne`, `DryRun` and `ResolveReadFabsAsync` whole to a new
  `src/Automation/Api/RulesEndpoints.Reads.cs`. Mappings stay in place.
- [ ] **T006 [P] [US1]** **S104 `src/CameraCatalog/Api/CameraEndpoints.cs`**: mark the class
  `partial`, then move `Get`, `List` and `ResolveReadFabsAsync` whole to a new
  `src/CameraCatalog/Api/CameraEndpoints.Reads.cs` (plan §2.2).
- [ ] **T007 [P] [US1]** **S104 `src/SystemVariables/Api/SystemVariableEndpoints.cs`**: mark the
  class `partial`, then move `GetOne`, `List`, `GetSnapshot`, `ResolveText` and
  `ResolveReadFabsAsync` whole to a new `src/SystemVariables/Api/SystemVariableEndpoints.Reads.cs`
  (plan §2.3). Mapping order is unchanged.
- [ ] **T008 [P] [US1]** **S104 `src/OverlayDesigner/Api/OverlayEndpoints.Commands.cs`**: move
  the six element-parsing helpers to a new `internal static class OverlayElementParsing` in
  `src/OverlayDesigner/Api/OverlayElementParsing.cs`, then re-point the two
  `Select(ParseElement)` call sites (plan §2.5). Do **not** add an `OverlayEndpoints.*.cs` file.
- [ ] **T009 [P] [US1]** **S104 `LayoutEndpoints.Commands.cs`, part 1**: move
  `ResolveWriteFabAsync`, `ResolveCallerFabsAsync` and `ResolveReadFabsAsync` to a new
  `internal static class LayoutCompositionFabResolution` in
  `src/LayoutComposition/Api/LayoutCompositionFabResolution.cs`. Re-point the call sites in
  `LayoutEndpoints.Commands.cs`, `LayoutEndpoints.Queries.cs`, `WallEndpoints.Commands.cs` and
  `WallEndpoints.Queries.cs` (plan §2.4). The S104 hit is **expected to remain** after this
  commit, at about 319 lines of code.
- [ ] **T010 [US1]** (after T009, same file) **S104 `LayoutEndpoints.Commands.cs`, part 2**: move
  `ParseGrid` and `ParseTiles` to a new `internal static class LayoutRequestParsing` in
  `src/LayoutComposition/Api/LayoutRequestParsing.cs`. Re-measure and confirm the file is at or
  below 300 lines of code. If it is not, **block and report**. Do not trim by reformatting. Do
  not add a `LayoutEndpoints.*.cs` file. The five If-Match handlers stay in this file.
- [ ] **T011 [P] [US1]** **S1541 `src/StreamDistribution/Api/StreamEndpoints.cs`
  `RecordKioskLatency`**: extract `SegmentFor(string measurement)` and the recording dispatch
  (plan §2.6). The kiosk gate, the `report.Measurement ==` comparisons and all three
  `ValidationProblem`s stay in the handler. `KioskMeasurementContractTests` and T001 stay green.
- [ ] **T012 [P] [US1]** **S1541 `src/StreamDistribution/Api/Program.cs`**: move the four
  constants, `PeekMediaMtxActionAsync` and `WhepAuthorizeActionClassification` to a new
  `internal static class WhepAuthorizeActionPartition` in
  `src/StreamDistribution/Api/WhepAuthorizeActionPartition.cs` (plan §2.7). Do not touch the
  fallback lines or the inline partition key. Use the §2.7 fallback only if the hit is still
  above 10 after the move.

## Phase 3: After-evidence (depends on T004 to T012)

- [ ] **T013 [US1]** Re-run plan §4.1 to §4.4 and capture each output **verbatim** for the PR
  body. The gate grep must be empty. Run the full `Architecture.Tests`. Run the integration
  filter, or point to the PR's own CI run. The Release build must have 0 errors, and
  `dotnet format` must be clean. Run `git diff origin/develop -- tests/` and confirm it shows
  only the two new files from T001 and T002, with nothing modified. In the PR, state:
  - that what `RecordKioskLatency` records (instrument names, tags, values and refusals) is
    unchanged, naming T001 as the test that pins it;
  - the latency impact, N/A;
  - the stale-comment finding (spec §6.4), as out of scope.

## Dependencies

T001, T002 and T003 come first and block everything in Phase 2. T004 must precede T005, and
T009 must precede T010, because each pair touches the same file. All other Phase 2 tasks are
file-disjoint. T013 comes last.

No foundational change is needed: no Shared.Kernel, Shared.Contracts, AppHost or Aspire
resource is touched.
