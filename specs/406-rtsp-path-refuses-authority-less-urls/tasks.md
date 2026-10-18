# Tasks 406: `RtspPath` refuses an authority-less URL

Spec: [spec.md](./spec.md). Plan: [plan.md](./plan.md). Issue #3025 (feature-level; autonomous lane,
ADR-0144; already on Project #13). No per-task issues.

**Phase 4a colour: RED** (behaviour-changing for authority-less URLs). The US2 rows are characterisation:
observed GREEN before the change and after, unmodified.

**Existing tests authorised for edit: none.** If any existing `ScenarioSimulator.Tests` assertion goes red
after T004, stop and report: the plan found none in conflict (plan §3).

Format: `[ID] [P?] [Story] description`. `[P]` = owns disjoint files (ADR-0109).

**Foundational:** none. No Shared.Kernel, Shared.Contracts, ServiceDefaults, AppHost or Aspire change.

## Phase 4a: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] `tests/ScenarioSimulator.Tests/RtspPathTests.cs`:
  `An_authority_less_url_is_not_a_simulated_camera_path`, spec US1 outline rows. Observed RED: returns
  true with the dot-segment path; quote it.
- [ ] **T002** [US2] (after T001, same file) `A_url_with_a_host_extracts_the_same_path_as_before`, spec US2
  rows - GREEN now; quote it.
- [ ] **T003** [P] [US1] `tests/ScenarioSimulator.Tests/CameraRegisteredSimHandlerAuthorityLessUrlTests.cs`
  over the existing `Fakes/SimHandlerRig` per plan §4.2: the two issue shapes x both rig modes (RED; the
  failure must show the recorded request path `/SECRET-token` and `/v3/config/paths/SECRET/x`), plus the
  asset characterisation row (GREEN).

T001-T002 and T003 own disjoint files and fan out. Return verbatim output of:

```sh
dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj \
  --filter "FullyQualifiedName~RtspPathTests|FullyQualifiedName~CameraRegisteredSimHandlerAuthorityLessUrlTests"
```

## Phase 4b: Green (backend-engineer; tests read-only)

- [ ] **T004** [US1] (after T001-T003) `src/ScenarioSimulator/CameraSim/RtspPath.cs`: refuse an empty
  `uri.Host` per plan §2, plus the one-sentence *why* in the XML summary. No other production file.
- [ ] **T005** (after T004) Run the T003 filter (all green), then the whole project:
  `dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj` - green,
  including `CameraSimSpanRedactionTests`, `CameraSimProvisionerSpanTests` and
  `CameraRegisteredSimHandlerLogRedactionAdversarialTests` unmodified. `dotnet format --verify-no-changes`
  on the touched files; Release build of the simulator and test project clean.

## Phase 5-6

- [ ] **T006** Verification note on the PR: the red/green output, and `git diff --stat` showing exactly
  `RtspPath.cs` plus the two new test files (and this spec directory). No Aspire boot (spec §6).
- [ ] **T007** Review (backend-reviewer; security-reviewer since it is a trust-boundary parser): confirm
  the cure choice in spec §4 and that no provisioner/redaction file changed.

## Dependencies

T001 || T003 -> T002 (same file as T001) -> T004 -> T005 -> T006 -> T007. Builds are light: one test
project; stop any running AppHost first (MSB3027).
