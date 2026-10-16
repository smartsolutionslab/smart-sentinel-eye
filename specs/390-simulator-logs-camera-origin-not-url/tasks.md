# Tasks 390: The Scenario Simulator logs a camera's origin and identifier, never its URL or URL path

Spec: [spec.md](./spec.md). Plan: [plan.md](./plan.md). Issue #2976 (feature-level; already on
Project #13, autonomous lane, ADR-0144). No per-task issues.

**Phase 4a colour: RED** (behaviour-changing log output). One characterisation test (T005) and one
counterfactual (T009, since retired - see its entry) were green before and after, unmodified. Functional behaviour is guarded inside the
red tests (camera-sim request path, correlation row, POST body).

**Existing tests authorised for edit: none.** No existing test pins a changed template (plan §10.1).

Format: `[ID] [P?] [Story] description`. `[P]` = owns disjoint files (ADR-0109).

**Foundational:** none. No Shared.Kernel, Shared.Contracts, AppHost or Aspire change. Phase 1 tasks
fan out immediately; Phase 2 is one engineer because `Log.cs` is shared by every production change.

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1] `tests/ScenarioSimulator.Tests/Fakes/SimHandlerRig.cs`: builds the real
  `CameraRegisteredSimHandler` with `RecordingLogger<CameraRegisteredSimHandler>`,
  `RecordingLogger<CameraSimProvisioner>`, a recording camera-sim `HttpMessageHandler` (200; or 400
  `"path already exists"` then 200), `AssetCorrelationTable`, `WallSeeder` as `SeederRig.cs:137-147`, and
  `ScenarioOptions` (empty, or one active scenario with one asset and `Wall = null`). Exposes all entries
  of both loggers combined, the camera-sim requests, and the correlation table. No production seam.
- [x] **T002** [US1] (after T001) `CameraRegisteredSimHandlerLogRedactionTests.A_camera_with_no_url_path_is_skipped_by_identifier_and_origin`:
  URL `rtsp://admin:hunter2@cam.example:8554?token=SECRET`. Assert one skip line naming the camera Guid and
  `rtsp://cam.example:8554`; no entry contains `hunter2`, `admin`, `token`, `SECRET`; no camera-sim request;
  no correlation row.
- [x] **T003** [US1] (after T001) `...A_non_asset_camera_never_logs_its_url_path`: name `Line 7 Spare`,
  URL `rtsp://admin:pw@cam.example/SECRET-token/stream`, no matching asset. Assert camera-sim got
  `POST /v3/config/paths/add/SECRET-token/stream`; correlation holds the camera under
  `SECRET-token/stream`; entries name the Guid, `rtsp://cam.example`, `Line 7 Spare`; no entry contains
  `SECRET-token`, `:pw`, `admin`.
- [x] **T004** [US1] (after T001) `...A_non_asset_camera_on_the_replace_branch_never_logs_its_url_path`:
  as T003 with camera-sim answering 400 "already exists"; assert the `replace/` request and no
  `SECRET-token` in any entry.
  *Recorded:* implemented as the `cameraSimAlreadyHasThePath: true` row of the T003 Theory, not a separate test.
- [x] **T005** [US2] (after T001) `...A_scenario_asset_camera_logs_its_asset_path_as_today` -
  **characterisation, must be observed GREEN now**: asset `station-4-roughing` / `roughing.mp4`, URL
  `rtsp://camera-sim:8554/station-4-roughing`; assert the exact strings
  `Recorded camera {guid} for asset 'station-4-roughing'.` and
  `Provisioned camera-sim loop path 'station-4-roughing' playing 'roughing.mp4'.`
- [x] **T006** [US1] (after T001) `...An_unparseable_url_is_skipped_without_being_printed`: URL
  `not a url :// hunter2`; skip line names the Guid and `<unparseable url>`; no `hunter2`.
- [x] **T007** [P] [US3] `tests/ScenarioSimulator.Tests/CameraCatalogClientLogRedactionTests.cs`:
  `Registering_a_scenario_camera_logs_the_origin_not_the_path` - `RtspHost = "rtsp.test"`, path
  `secret-path`, catalog answers 201 + Guid. Assert line `Registered camera '<name>' -> rtsp://rtsp.test.`,
  no entry contains `secret-path`, and the captured POST body still contains `rtsp://rtsp.test/secret-path`.
  Own stub handler and token provider (as `CameraCatalogClientTests.cs:172-196`); do not touch that file.
- [x] **T008** [P] [US4] `tests/ScenarioSimulator.Tests/Fakes/RecordingLoggerProvider.cs` +
  `CameraSimProvisionerHttpLoggingTests.The_camera_sim_client_request_log_does_not_print_the_path`:
  `ServiceCollection` with logging configured from the simulator's `appsettings.json` (loaded from
  `AppContext.BaseDirectory`, `optional: false`) and `AddHttpClient<CameraSimProvisioner>` with a stub
  primary handler; call `ProvisionLabelledPathAsync("SECRET-token/stream", "Line 7 Spare", guid, ct)`;
  assert no record in any category contains `SECRET-token`.
- [x] **T009** [US4] (RETIRED) (after T008) `...The_recorder_sees_the_camera_sim_client_request_log_when_its_level_allows`
  - **counterfactual, GREEN now and after**: same rig with an in-memory override setting
  `Logging:LogLevel:System.Net.Http.HttpClient.CameraSimProvisioner` to `Information`; assert a record in
  category `System.Net.Http.HttpClient.CameraSimProvisioner.*` contains `SECRET-token`.
  **RETIRED in `96b1a286`.** It asserted that a record in `System.Net.Http.HttpClient.CameraSimProvisioner.*` contains the secret when the level is raised. With the factory loggers removed that category writes nothing, so the assertion cannot hold. Its job (the recorder can see the factory scope) moved to `The_recorder_sees_the_request_uri_scope_when_a_line_is_written_inside_it` (`keepFactoryLoggers: true`).
- [x] **T010** Run and quote verbatim:
  `dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj --filter "FullyQualifiedName~LogRedactionTests|FullyQualifiedName~HttpLoggingTests"`.
  Expected: T002, T003, T004, T006, T007, T008 **fail on assertions** (not compile errors); T005 and T009
  pass. Any other outcome stops the phase. Also run the whole project once to record the baseline.

## Phase 1b: Adversarial (test-adversary; plan §10.4)

- [x] **T011** [P] Hunt leaks the Phase-1 tests do not cover: exception `ToString()` on camera-sim 500 /
  refused / timeout / cancellation; `null`, empty, whitespace, `rtsp:\\admin:1#@cam/x`,
  percent-encoded path, IPv6 zone, userinfo-only; a non-asset path equal to an asset path; Polly /
  resilience categories with ServiceDefaults' standard handler; `.ClientHandler` category. New file(s)
  `tests/ScenarioSimulator.Tests/*LogRedactionAdversarialTests.cs` only. Report each finding with a
  failing test or a written reason it is not a leak. A null-URL case must cover FR-008 (no throw).

## Phase 2: Green (backend-engineer; may not edit any test above)

- [x] **T012** [US1-US4] (after T010, T011) Implement plan §3-§7 in one pass:
  `src/ScenarioSimulator/Log.cs`, `EventHandlers/CameraRegisteredSimHandler.cs`,
  `CameraSim/CameraSimProvisioner.cs`, `CameraCatalog/CameraCatalogClient.cs`, `appsettings.json`.
  `UrlRedaction` untouched; public signatures of the provisioner and the client untouched; `var`, no
  underscore fields, `Ensure.That`, no new comments beyond the reworded `:96-103` block.
  *Recorded:* the "no new comments" claim did not hold exactly: the handler comment block was moved and
  reworded, and `09fed9ec` later corrected the provisioner comment (dropping a stale "(FR-004)" reference
  that meant an older spec). *Ordering:* T012 says "after T010, T011", but the fixes (`b48c6c42`, `61902b7d`)
  landed before the adversary pass (`eee36742`); the adversary then ran against the finished fix.
- [x] **T013** (after T012) Green run: the T010 filter, then the whole
  `tests/ScenarioSimulator.Tests` project, then
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstruction"`, then
  `dotnet build -c Release` (analyzers clean, IDE0007/IDE1006/S-rules). Quote outputs.

## Phase 2b: Scope leak (added after the adversary and characterisation passes)

- [x] **T017** `96b1a286` red: `CameraSimProvisionerResilienceScopeLeakTests` (3 failures: status 200, status 500, attempt timeout) - the factory scope exports the path (spec FR-009).
- [x] **T018** `7f4667de` refactor: extract `AddCameraSimProvisionerClient` so `Program.cs` and the rig share one registration (behaviour-neutral; the lambda moved verbatim).
- [x] **T019** `6f627422` fix: `.RemoveAllLoggers()` on that registration.
- [x] **T020** `8ebe608c` the rig calls the extension (tests only; `The_recorder_sees_the_request_uri_scope...` uses `keepFactoryLoggers: true`).
- [x] **T021** `613d5f97` characterisation: correlation row, wall attempt and replace log asserted unchanged.
- [x] **T022** `09fed9ec` the labelled line no longer promises a collision diagnostic (comment + template).

## Phase 3: Verify (phase 5)

- [ ] **T014** (after T013) Run-mode stack with the simulator enabled. Register
  `rtsp://admin:pw@10.0.5.42/SECRET-token/stream` by hand through CameraCatalog; Aspire structured logs for
  `scenario-simulator`: zero hits for `SECRET-token`, `admin`, `pw`; the camera Guid shows the recorded and
  labelled lines with `rtsp://10.0.5.42`. Camera-sim `GET /v3/config/paths/list` still lists
  `SECRET-token/stream`. Write the note on the PR. Latency: N/A (not on the SLO path).
  **Open: Docker was down.** The search must cover ATTRIBUTES and SCOPES as well as message text (the Polly leak lived in a scope; the trace attribute `url.full` is known to carry the path, follow-up #3019).

## Phase 4: Review

- [ ] **T015** [P] backend-reviewer on the diff.
- [ ] **T016** [P] Security review: not required by the owner decision (shared helper unchanged); the
  adversary pass (T011) is the leak check. Run one only if T011 finds a leak outside the five files.

## Dependencies

```
T001 ─┬─ T002, T003, T004, T005, T006 ─┐
T007 ─┤                                ├─ T010 ─ T011 ─ T012 ─ T013 ─ T014 ─ T015/T016
T008 ─┴─ T009 ─────────────────────────┘
```

T001, T007 and T008 own disjoint files and run in parallel. T012 is serial (all production edits share
`Log.cs`).
