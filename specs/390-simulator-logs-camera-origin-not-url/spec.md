# Spec 390: The Scenario Simulator logs a camera's origin and identifier, never its URL or URL path

**Feature Branch**: `fix/2976-simulator-log-redaction` (cut from `origin/develop` at `b5a29905`)

**Created**: 2026-10-16

**Status**: Phases 1-4 implemented on the branch (red `45003349`, fixes `b48c6c42` `61902b7d`, adversary
`eee36742`, characterisation `613d5f97`, scope-leak red `96b1a286`, fix `7f4667de` `6f627422` `09fed9ec`,
rig switch `8ebe608c`); the Release suite is green. Phase 5 (the live-stack dashboard check, T014) is open:
Docker was down. Phases 6-7 are open. No `[NEEDS CLARIFICATION]` remains. One scope extension beyond the
issue's named sites is marked `[ASSUMPTION]` in §5 and is the reviewer's to strike.

**Input**: Issue [#2976](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2976), a
follow-up to #2908 (spec 373 §6, task T022).

**Owner decision (2026-10-16, delegated, on #2976), recorded here and not re-opened:** redact, scope
widened as the #2908 review found. (1) `Log.cs:71` `SkippedNonSimulatedCamera` and `Log.cs:40`
`CameraRegistered` render the URL through `Shared.Kernel.UrlRedaction.ToOrigin`. (2) The path-derived
label line `CameraSimPathLabelled` logs the camera identifier (and the origin) instead of the path; the
path is still sent to camera-sim as the path name. (3) `CameraCatalogClient.cs:59` is redacted. Only log
text changes; the simulator's behaviour does not. Phase 4a is RED. The handler has no harness: the
test-writer adds the smallest one that captures the log; if that needs more than a constructor/DI seam,
STOP. Security review is not needed unless the shared helper changes.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** for log output (red), **behaviour-preserving**
for what the simulator does (guarded by characterisation assertions inside the same tests: the
camera-sim request path, the correlation row, the wall). Ambiguity resolves to red, and log text is the
behaviour under test.

**Spec number.** 390. Checked 2026-10-16 against `origin/develop` (highest 388), every local and remote
head, and every open PR head: 389 is claimed by PR #3018 (`fix/2999-reject-at-after-authority`, #2999).
Re-checked immediately before the commit.

**ADRs.** ADR-0111 (the Scenario Simulator is dev-only tooling), ADR-0050 (`[LoggerMessage]`,
structured fields; MEL-native), ADR-0139 / ADR-0144 (phase 4a red first), ADR-0143 (the camera-sim
`POST` is not retried, so no retry log line repeats the path). The redaction helper and its contract are
spec 373's (#2908); this spec reuses it unchanged. **No new ADR is needed**: this applies an existing,
decided pattern to one more host.

**Latency budget.** N/A. The simulator is not on the event-to-overlay path; nothing here touches a leg.

## 1. Premise, verified at `b5a29905` (not assumed)

**A real camera URL does reach the simulator.** `CameraRegisteredSimHandler` subscribes to
`CameraRegisteredV1`, which CameraCatalog publishes for *every* registered camera, not only the ones the
simulator seeded. The simulator runs when `isRunMode && !isE2ETests && isScenarioSimulatorEnabled`
(`src/AppHost/AppHost.cs:577`), i.e. on a developer's run-mode stack. A camera registered by hand on that
stack (the management console, a curl, a test run left behind) arrives with its operator-typed URL. So
the impact is dev-only, and real.

Each cited site, read:

| # | Site | Template today | What reaches the log | Source of the string |
|---|---|---|---|---|
| A | `EventHandlers/CameraRegisteredSimHandler.cs:43` -> `Log.cs:71` `SkippedNonSimulatedCamera` | `Camera URL {Url} has no path component; not a simulated camera, skipping.` | The **whole URL**: userinfo, query, fragment (`rtsp://admin:pw@cam?token=x`) | Any registered camera whose URL has no path (or does not parse) |
| B | `CameraCatalog/CameraCatalogClient.cs:59` -> `Log.cs:40` `CameraRegistered` | `Registered camera '{Name}' -> {RtspUrl}.` | The URL the simulator built itself | `rtsp://{ScenarioSimulator:RtspHost}/{asset path}`: config only, no operator input. Redacted per the owner decision for uniformity, not because it can carry a secret today. |
| C | `CameraSim/CameraSimProvisioner.cs:104` -> `Log.cs:60` `CameraSimPathLabelled` | `camera-sim path '{Path}' now carries the label '{Label}' for camera {Camera}. ...` | `uri.AbsolutePath.Trim('/')` of the operator URL (`RtspPath.cs:19`): `rtsp://cam/SECRET/path` logs `SECRET/path` | Every camera that is not a scenario asset |

Issue items (1) "`Log.cs:40` `CameraRegistered`" and (3) "`CameraCatalogClient.cs:59`" are **one site**
(B): `CameraRegistered` has exactly one caller.

**The premise check found three more lines that log the same path for the same cameras** (site C's
population), at the same Information level:

| # | Site | Template | When |
|---|---|---|---|
| D | `CameraRegisteredSimHandler.cs:53` -> `Log.cs:115` `AssetCameraRecorded` | `Recorded camera {Camera} for asset '{Asset}'.` (`Asset` = the path) | Every camera with a path, before the asset/non-asset branch |
| E | `CameraSimProvisioner.cs:125` -> `Log.cs:49` `CameraSimPathProvisioned` | `Provisioned camera-sim loop path '{Path}' playing '{Clip}'.` | The labelled branch calls the shared `ProvisionAsync` |
| F | `CameraSimProvisioner.cs:147` -> `Log.cs:52` `CameraSimPathReplaced` | `Replaced camera-sim path '{Path}'; it now plays '{Clip}'.` | Same, when camera-sim answers "already exists" |
| G | `System.Net.Http.HttpClient.CameraSimProvisioner.{LogicalHandler,ClientHandler}` (IHttpClientFactory's built-in logging) | `Sending HTTP request POST http://.../v3/config/paths/add/{path}` and the matching `Received HTTP response` / `End processing` lines | The request URI carries the path; `src/ScenarioSimulator/appsettings.json` leaves `Default` at `Information` and has no filter for this category. StreamDistribution filters its own MediaMTX client the same way (`src/StreamDistribution/Api/appsettings.json:7`, commit `937340bc`). |

Redacting C alone would leave D, E, F and G printing the identical string one line earlier. See §5.

**Lines that log a path and are out of scope, with the reason:** `CameraSimReconciler.cs:56,70`
(`ClipMissing`, `CameraSimReconcileFailed`) and every `ClipCue*` / `CameraSimPathUnreadable` line iterate
`ScenarioOptions` asset paths only; those are authored in `Scenarios/*.json`, never operator input. The
scenario-asset branch of the handler (`TryFindAssetClip` true) likewise handles only config paths; D/E/F
keep logging the path there, unchanged.

**Exception text.** `ProvisionAsync` throws through `EnsureSuccessStatusCode`; `HttpRequestException`'s
message is the status line ("Response status code does not indicate success: 500 ...") and a connection
failure names host:port only. Neither carries the path. Wolverine logs that exception on a failed handler.
This is a claim for the adversary pass to try to break, not a test this spec writes.

**Already touched?** No. `git log` on the four files shows only refactors (`f5ebf836`, `fc849a03`,
`21ca4bda`); #2908 (spec 373) and #2977 (closed) changed StreamDistribution, CameraCatalog and
AuditObservability only. PR #3018 (#2999, in review) moves `UrlRedaction`'s at-sign scan into
`UrlAuthority`; `ToOrigin(string)`'s signature and contract are unchanged, and it touches no
ScenarioSimulator file. No textual overlap.

**Reference.** `SmartSentinelEye.ScenarioSimulator` references `Shared.Contracts`, which references
`Shared.Kernel`; `CameraRegisteredSimHandler.cs:6` and `CameraSimProvisioner.cs:2` already import
`SmartSentinelEye.Shared.Kernel`. No project-reference change is needed.

**Harness.** No new seam. `CameraRegisteredSimHandler`'s five dependencies are all constructible in a unit
test today: `CameraSimProvisioner(HttpClient, ILogger<>)` (as `CameraSimProvisionerTests` does),
`AssetCorrelationTable()`, `WallSeeder(LayoutCompositionClient, ...)` (as `Fakes/SeederRig.cs:137` does),
`Options.Create(new ScenarioOptions())`, and `Fakes/RecordingLogger<T>`. The owner's STOP condition does
not trigger.

**No existing test pins any of the changed log text.** Searched `tests/` for every changed template
string and for the three public entry points: `CameraSimProvisionerTests` and `CameraCatalogClientTests`
construct with `NullLogger`, nothing constructs `CameraRegisteredSimHandler`, and the only
"Registered camera" match (`tests/Integration.Tests/Fixtures/LogTailDeliversIntegrationTests.cs:75`) is
CameraCatalog's own line, not the simulator's. **No existing test edit is authorised by this spec.**

## 2. User stories

### US1 (P1) — A hand-registered camera's URL and URL path never reach the simulator's log

As a developer running the stack with the simulator on, when I register a real camera whose URL carries
credentials or a token, I want the simulator's log to name that camera by identifier and origin, so that a
shared console, a pasted log or the Aspire dashboard does not carry the secret.

**Why P1.** It is the whole issue; the other stories are the same slice.

**Acceptance scenarios**

```gherkin
Scenario: A camera with no URL path is skipped by identifier and origin
  Given a CameraRegisteredV1 for camera C with URL "rtsp://admin:hunter2@cam.example:8554?token=SECRET"
  When the simulator handles it
  Then exactly one line says the camera was skipped as not simulated
  And that line names C and "rtsp://cam.example:8554"
  And no line contains "hunter2", "admin", "token" or "SECRET"
  And nothing is sent to camera-sim and nothing is recorded in the correlation table

Scenario: A non-asset camera with a secret in its path is labelled by identifier, not path
  Given a CameraRegisteredV1 for camera C named "Line 7 Spare"
    with URL "rtsp://admin:pw@cam.example/SECRET-token/stream"
  And no active scenario asset has the path "SECRET-token/stream"
  When the simulator handles it
  Then camera-sim receives POST "/v3/config/paths/add/SECRET-token/stream"   # unchanged behaviour
  And the correlation table holds C under "SECRET-token/stream"               # unchanged behaviour
  And the simulator's lines name C, "rtsp://cam.example" and the label "Line 7 Spare"
  And no line from the handler or the provisioner contains "SECRET-token", ":pw" or "admin"   # ":pw", not "pw": the bare
    # two letters occur in ordinary words, so the test asserts the userinfo separator form

Scenario: The same camera when camera-sim already has the path (replace branch)
  Given camera-sim answers the add with 400 "path already exists"
  When the simulator handles the same event
  Then camera-sim receives POST "/v3/config/paths/replace/SECRET-token/stream"
  And no line contains "SECRET-token"

Scenario: A URL that does not parse is skipped without being printed
  Given a CameraRegisteredV1 whose URL is "not a url :// hunter2"
  When the simulator handles it
  Then the skip line names the camera and "<unparseable url>"
  And no line contains "hunter2"
```

**Bad request / conflict / auth.** Not applicable as HTTP outcomes: the handler has no caller-facing
answer. The conflict case is camera-sim's "already exists" (scenario 3). Authorisation is unchanged: the
message arrives over RabbitMQ from CameraCatalog, which authorised the registration.

### US2 (P1, same slice) — A scenario asset keeps its path in the log

As a developer, I still want a scenario camera's lines to name its asset path, because that path is
config I wrote and it is how I find a station in the log.

```gherkin
Scenario: A scenario asset camera logs as today
  Given an active scenario with asset path "station-4-roughing" and clip "roughing.mp4"
  And a CameraRegisteredV1 for camera C with URL "rtsp://camera-sim:8554/station-4-roughing"
  When the simulator handles it
  Then the handler logs "Recorded camera C for asset 'station-4-roughing'."
  And the provisioner logs "Provisioned camera-sim loop path 'station-4-roughing' playing 'roughing.mp4'."
```

This is a characterisation guard: green before the change and green, unmodified, after.

### US3 (P1, same slice) — The simulator's own registration line names the origin

```gherkin
Scenario: Registering a scenario camera logs the origin only
  Given ScenarioSimulator:RtspHost is "rtsp.test" and the asset path is "secret-path"
  When the simulator registers the camera and the catalog answers 201 with identifier C
  Then the line reads "Registered camera '<name>' -> rtsp://rtsp.test."
  And no line contains "secret-path"
  And the request body to POST /cameras still carries "rtsp://rtsp.test/secret-path"   # unchanged
```

### US4 (P1, same slice) — The HTTP client's own request log does not print the camera-sim path

```gherkin
Scenario: IHttpClientFactory logging is quiet for the camera-sim client
  Given the simulator's real appsettings.json logging section
  And a CameraSimProvisioner resolved from AddHttpClient<CameraSimProvisioner>
  When it provisions the labelled path "SECRET-token/stream"
  Then no record in any logging category contains "SECRET-token"

Scenario: Counterfactual - the recorder does see framework lines
  Given the same rig with "System.Net.Http.HttpClient.CameraSimProvisioner" set to Information
  When it provisions the same path
  Then a System.Net.Http.HttpClient.CameraSimProvisioner record contains "SECRET-token"
```

The counterfactual proves the first scenario can fail; without it a recorder that never receives
framework lines would pass it vacuously.

**Independent end-to-end test procedure.** (1) Local, no Docker:
`dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj` runs every
scenario above. (2) Phase 5, on a run-mode stack with the simulator enabled (`dotnet run --project
src/AppHost`): register a camera by hand with
`rtsp://admin:pw@10.0.5.42/SECRET-token/stream` through CameraCatalog; in the Aspire dashboard's
structured logs for `scenario-simulator`, search for `SECRET-token`, `admin` and `pw`: zero results; search
for the camera's identifier: the recorded / labelled lines, carrying `rtsp://10.0.5.42`. Confirm in
camera-sim's API (`GET /v3/config/paths/list`) that the path `SECRET-token/stream` still exists.

## 3. Functional requirements

- **FR-001** `SkippedNonSimulatedCamera` names the camera identifier and `UrlRedaction.ToOrigin(url)`;
  it no longer takes the URL. (Site A.)
- **FR-002** `CameraRegistered` renders `UrlRedaction.ToOrigin(rtspUrl)` in place of the URL; the
  structured field is renamed `{Origin}` so the field name does not claim a URL. (Site B.) The request
  body is unchanged.
- **FR-003** For a camera that is not a scenario asset, no simulator line carries the URL path: the
  handler's recorded line (site D), the provisioner's provisioned / replaced lines (sites E, F) and the
  labelled line (site C). They name the camera identifier, and the handler's line names the origin.
- **FR-004** For a scenario-asset camera, sites D, E and F log exactly as today (US2).
- **FR-005** `src/ScenarioSimulator/appsettings.json` sets `System.Net.Http.HttpClient.CameraSimProvisioner`
  to `Warning`, mirroring StreamDistribution's `IRtspGateway` filter. (Site G.) Since FR-009 this filter is
  defence in depth: with the factory loggers removed the category writes nothing, and the filter only
  matters if the registration regresses.
- **FR-009** (added after `96b1a286`) No exported record carries the camera-sim path in its **scope**. The
  HttpClient factory opens an `HTTP {HttpMethod} {Uri}` scope around every request, ServiceDefaults exports
  scopes (`IncludeScopes = true`), and Polly's `Execution attempt` records (Information, success included)
  are written inside it. The camera-sim client is therefore registered with `.RemoveAllLoggers()`, in the one
  extension `AddCameraSimProvisionerClient` that `Program.cs` and the test rig both call. Failures still
  reach callers as exceptions.
- **FR-006** Functional behaviour is unchanged: the same camera-sim requests (path, body), the same
  correlation-table rows, the same wall attempts, in the same order; the same POST /cameras body.
- **FR-007** `UrlRedaction` is not modified.
- **FR-008** A `CameraRegisteredV1` whose `Url` is `null` on the wire (the contract is non-nullable, but
  the message is a trust boundary and deserialisation does not enforce NRT) is still skipped and logged,
  as today, rendering `UrlRedaction.Unparseable`; it must not throw, because `ToOrigin` guards
  `IsNotNull` and a throw would turn a log-and-drop into a Wolverine retry.

## 4. Non-goals

- OpenTelemetry **trace** attributes. `AddHttpClientInstrumentation` records `url.full` (with the path)
  on the camera-sim client span, exported to the Aspire dashboard. That is a trace, not a log; the owner
  decision scoped this to log text. Recorded as a residual (§6); not changed here.
- Renaming or re-encoding the camera-sim path. The path is functional (it is the MediaMTX path name main
  MediaMTX pulls); deriving it from the identifier would change the simulator's behaviour and the owner
  decision forbids that.
- Introducing a `CameraIdentifier` value object in the simulator. It has no Domain layer; the contracts
  carry `Guid` and `Log.cs` already logs `Guid camera` (e.g. `CameraSimPathLabelled`, `AssetCameraRecorded`).
  Mirroring that is the house rule ("read before write"); a VO here would be speculative.
- Config-authored path lines (reconciler, clip cues), §1.

## 5. Judgement calls

- **[ASSUMPTION] Scope covers sites D-G, not only C.** The owner decision names the labelled line; its
  stated aim is that the path is not logged. Three simulator lines and the framework request log print the
  identical string for the identical cameras at the identical level, so fixing C alone leaves the leak
  unchanged in substance. Extending to D-G stays inside the decision's constraint (log output only, no
  behavioural change). A reviewer who disagrees strikes FR-003's D/E/F clause and FR-005; the slice still
  ships.
- **Where the origin is logged.** The handler holds the URL; the provisioner receives only the path. The
  origin is logged on the handler's non-asset recorded line; the provisioner's lines carry the camera
  identifier, which correlates them. This keeps every public provisioner signature unchanged, so no
  existing test has to be edited (`CameraSimProvisionerTests.cs:73,75,105` call
  `ProvisionLabelledPathAsync(path, label, camera, ct)`).
- **A labelled camera loses its separate "provisioned" line.** The labelled branch emits one line (the
  labelled one, carrying added-vs-replaced) instead of "provisioned `sim-loop.mp4`" plus "labelled". The
  removed line said only `sim-loop.mp4`, which `CameraSimProvisioner.cs:96-98` already calls "no help at
  all".
- **Site B is redacted although it cannot carry a secret today.** Per the owner decision; the cost is that
  a scenario camera's registration line no longer shows the path, which its `'{Name}'` already identifies.

## 6. Accepted residuals (in writing)

- **Trace attributes** (`url.full` on the camera-sim client span) still carry the path; dev-only Aspire
  dashboard. To be filed as a follow-up issue at phase 7 if the reviewer agrees it matters. Follow-up #3019.
- **Polly / resilience telemetry: the original assumption was wrong.** This section assumed Polly does not
  log the request URI. Polly does not put it in the *message*, but its records are written inside the
  HttpClient factory's `HTTP {HttpMethod} {Uri}` scope, which ServiceDefaults exports. `96b1a286` showed it
  (3 failures: status 200, status 500, attempt timeout). Resolved by FR-009; no longer a residual.
- **`CameraSimPathLabelled` logs `{Label}`** - the raw, operator-typed camera name. It is display text,
  and CR/LF in it can forge lines in a text sink. Not pinned by any test. To be filed with the trace
  attribute below (follow-up #3019).
- **Structured-field renames.** `Path` is gone from the simulator's lines and `RtspUrl` became `Origin`;
  saved dashboard searches on the old fields stop matching.
- **Filter prefix.** The `appsettings.json` category filter is a prefix: a future client whose name starts
  with `CameraSimProvisioner` would be matched by it. Harmless (it only raises a level).
- **Bisect note.** `b48c6c42`'s own US4 test stays red until `61902b7d`, because the provisioner's own
  lines still carried the path at that commit.
- **Other typed clients checked.** `CameraSimPathClient` is only called by `ClipCueHostedService` with
  `asset.Camera.Path` from scenario definitions, so only scenario paths reach it; `CameraCatalogClient` and
  the seed clients carry no camera-sim path. No operator-typed path reaches any client but
  `CameraSimProvisioner`, so only that one is changed.

## 7. Acceptance

Every scenario in §2 green in `ScenarioSimulator.Tests`; each new-behaviour test observed red first with
the failure quoted in the PR (ADR-0139); the US2 characterisation test observed green before and after,
unmodified; `dotnet build -c Release` clean; phase-5 dashboard search returns zero hits for the secret.
