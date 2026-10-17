# Tasks 393: The camera-sim client span names the MediaMTX verb, not the camera path; the labelled line names the camera, not its label

Spec: [spec.md](./spec.md). Plan: [plan.md](./plan.md). Issue #3019 (feature-level; autonomous lane,
ADR-0144; already on Project #13). No per-task issues.

**Phase 4a colour: RED** (behaviour-changing: an exported span attribute and a log template). Two
characterisation tests (T006, T009) must be observed GREEN before and after, unmodified. Functional
behaviour (camera-sim request path and body) is asserted inside the red tests.

**Existing tests authorised for edit: one line** - `CameraRegisteredSimHandlerLogRedactionTests.cs:52`
(`ShouldContain("Line 7 Spare")` -> `ShouldNotContain`), by the test-writer in phase 4a, never by the
engineer. Reason: it pins the behaviour FR-005 removes.

Format: `[ID] [P?] [Story] description`. `[P]` = owns disjoint files (ADR-0109).

**Foundational:** none. No Shared.Kernel, Shared.Contracts, ServiceDefaults, AppHost or Aspire change.

## Phase 4a: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] `tests/ScenarioSimulator.Tests/CameraSimSpanRedactionTests.cs`: `Redact` table
  per plan §7 T001 (add/replace/patch/delete/get; query; fragment; percent-encoded; IPv6; no port).
  Red = compile failure on the missing `CameraSimSpanRedaction`; quote it.
- [ ] **T002** [US1] (after T001, same file) `Redact` leaves inputs alone per plan §7 T002.
- [ ] **T003** [P] [US1] `tests/ScenarioSimulator.Tests/Fakes/CameraSimLoopbackServer.cs` +
  `CameraSimProvisionerSpanTests.cs`: rig per plan §6 (real Kestrel on `127.0.0.1:0`, `AddServiceDefaults`,
  `AddCameraSimProvisionerClient`, capturing `BaseProcessor<Activity>`, host started). Test
  `The_add_span_exports_the_verb_not_the_path`: assert one POST client span, `url.full` =
  `{base}v3/config/paths/add/Redacted`, no tag/display name contains `SECRET-token`, server saw
  `/v3/config/paths/add/SECRET-token/stream`. Observed red: `url.full` carries the path.
- [ ] **T004** [US1] (after T003) `The_replace_fallback_span_exports_the_verb_not_the_path`: 400
  `"path already exists"` then 200; two spans, both redacted; server saw the replace path.
- [ ] **T005** [US1] (after T003) `A_failing_add_span_exports_the_verb_not_the_path`: 500; call throws;
  span redacted.
- [ ] **T006** [US1] (after T003) `A_span_outside_the_config_paths_api_is_unchanged` - **characterisation,
  GREEN now and after**: factory `HttpClient` GET `{base}v3/paths/get/station-4-roughing`; `url.full`
  ends with that path.
- [ ] **T007** [P] [US2] `tests/ScenarioSimulator.Tests/CameraRegisteredSimHandlerLogRedactionTests.cs`:
  the authorised `:52` flip, plus assert the camera-sim request body still contains `Line 7 Spare`.
- [ ] **T008** [US2] (after T007, same file) CR/LF-name and URL-name tests per plan §7 T008, asserting over
  message and structured state (`RecordingLoggerProvider.Dump` renders `LoggedEntry.Message` and
  `.Structured`; the body is `RigRequest.Body`).
- [ ] **T009** [US3] Run `A_scenario_asset_camera_logs_its_asset_path_as_today` unmodified - GREEN; quote it.

T001-T002, T003-T006 and T007-T008 own disjoint files and fan out. Return verbatim output of:

```sh
dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj \
  --filter "FullyQualifiedName~CameraSimSpanRedactionTests|FullyQualifiedName~CameraSimProvisionerSpanTests|FullyQualifiedName~CameraRegisteredSimHandlerLogRedactionTests"
```

## Phase 4b: Green (backend-engineer; tests read-only)

- [ ] **T010** [US1] (after T001-T006) `src/ScenarioSimulator/CameraSim/CameraSimSpanRedaction.cs` per
  plan §2-§3.
- [ ] **T011** [US1] (after T010) `CameraSimProvisionerExtensions.AddCameraSimProvisionerClient` configures
  `HttpClientTraceInstrumentationOptions` per plan §4 (request callback; response/exception callbacks only
  if T003-T005 stay red for that reason - say so in the report).
- [ ] **T012** [US2] (after T007-T008; independent of T010-T011, but same engineer) `Log.cs`
  `CameraSimPathLabelled(Guid camera, PathWrite write)` per plan §5; `CameraSimProvisioner.cs:120` call site
  and its comment.
- [ ] **T013** Guard: whole `ScenarioSimulator.Tests` project; Architecture.Tests
  `HandlerDeconstruction|PrimitiveBoundary`; `dotnet build -c Release` (IDE0005/IDE0007/IDE1006 clean);
  `dotnet format --verify-no-changes` on touched files.

## Phase 4 adversarial (test-adversary)

- [ ] **T014** Plan §8 list, as failing tests where a leak is found (new file
  `CameraSimProvisionerSpanAdversarialTests.cs`).

## Phase 5-7

- [ ] **T015** Phase 5 per spec §4 (live stack, Aspire dashboard). If Docker is down, record open.
- [ ] **T016** Phase 6: backend-reviewer; security-reviewer advisable (redaction claim).
- [ ] **T017** Phase 7: PR to `develop` quoting T001/T003/T004/T005/T007/T008 red output and T006/T009
  green-before/after; `Closes #3019`.
