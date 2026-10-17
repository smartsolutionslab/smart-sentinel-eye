# Spec 393: The camera-sim client span names the MediaMTX verb, not the camera path; the labelled line names the camera, not its label

**Feature Branch**: `fix/3019-camera-sim-span-path` (cut from `origin/develop` at `fbd4176b`)

**Created**: 2026-10-17

**Status**: Phases 1-3 written. No `[NEEDS CLARIFICATION]` remains. Two scope decisions are marked
`[DECISION]` in §5 and are the reviewer's to strike.

**Input**: Issue [#3019](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3019), found by the
test-adversary pass on #2976 and recorded as residuals in spec 390 §6.

**Owner decision (on #3019, lane recommendation, not re-opened here):** (1) redact the path segment after
`/v3/config/paths/{add|replace|patch|delete}/` on the `CameraSimProvisioner` client's span; (2) log the
camera identifier in place of the raw operator-typed label in `CameraSimPathLabelled`, CR/LF neutralised
either way. Code-only, dev tooling.

**ADRs.** 0111 (Scenario Simulator, dev-only), 0050 (logging), 0118 (one telemetry sink per environment:
the Aspire dashboard is where the span is read), 0139/0144 (red first, autonomous lane). **No new ADR**:
this applies spec 390's already-taken decision (the simulator's telemetry does not carry operator-typed
URL text) to the one export spec 390 left open. Nothing in `ServiceDefaults` or `Shared.*` changes.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (phase 4a RED) for the exported span attribute and
the labelled log line. **Behaviour-preserving** for what the simulator does - the camera-sim request path
and body, the drawn label, the hue, the replace fallback - guarded by characterisation assertions inside
the same tests.

**Latency budget (constitution §IV): N/A.** The Scenario Simulator is dev tooling gated off outside run
mode and off in the E2E lane (`AppHost.cs:577`); no leg of the event-to-overlay path runs through it, and
the enrichment it registers lives only in its own process.

## 1. What leaks today

| # | Site | Exported as | Carries |
|---|---|---|---|
| S | `CameraSimProvisioner.ProvisionAsync` -> `POST /v3/config/paths/add/{path}` (and `replace/{path}`) | The `System.Net.Http` client span's `url.full`, emitted natively by the .NET 10 runtime and exported by ServiceDefaults' tracing (`Extensions.cs:112`) | For a non-asset camera, `path` is `RtspPath.TryExtract` of the operator-typed URL (`uri.AbsolutePath`): `rtsp://cam/SECRET-token/stream` exports `http://camera-sim:9997/v3/config/paths/add/SECRET-token/stream` |
| L | `CameraSimProvisioner.cs:120` -> `Log.cs:57` `CameraSimPathLabelled` | Structured log `{Label}` and the rendered message | The raw operator-typed camera name. CR/LF in it can forge lines in a text sink; a name can itself be a URL with credentials. Only the drawtext command sanitises it. |

Checked and **not** leaking (so out of scope): the runtime already replaces query values with `Redacted`
(pinned `OpenTelemetry.Instrumentation.Http` 1.19.0 README, `url.full`); the span's display name is the
method only; `http.client.request.duration` dimensions carry no URL; the factory loggers are removed
(spec 390 FR-009); `CameraSimPathClient` (`GET /v3/paths/get/{path}`) is called only by
`ClipCueHostedService` with scenario paths (spec 390 §6).

## 2. User stories and acceptance scenarios

### US1 (P1) - A non-asset camera's path never reaches an exported span

As a developer reading the Aspire dashboard, I can see that the simulator added or replaced a camera-sim
path - verb, host, status, duration - without the span exporting the URL path an operator typed.

```gherkin
Scenario: The add span exports the verb and a placeholder, not the path
  Given the simulator's real camera-sim client registration and ServiceDefaults tracing
  And camera-sim answers 200
  When it provisions the labelled path "SECRET-token/stream"
  Then exactly one client span is exported for that request
  And its url.full is "<camera-sim base>/v3/config/paths/add/Redacted"
  And no tag value and no display name of any exported span contains "SECRET-token"
  And camera-sim received "POST /v3/config/paths/add/SECRET-token/stream"

Scenario: The replace fallback is redacted too (conflict)
  Given camera-sim answers 400 "path already exists" to the add, then 200 to the replace
  When it provisions the labelled path "SECRET-token/stream"
  Then two client spans are exported, ending ".../paths/add/Redacted" and ".../paths/replace/Redacted"
  And no exported span contains "SECRET-token"
  And camera-sim received the replace request for "SECRET-token/stream"

Scenario: A failing request is redacted (bad request / server error)
  Given camera-sim answers 500
  When it provisions the labelled path "SECRET-token/stream" and the call throws
  Then the exported span's url.full ends "/v3/config/paths/add/Redacted"
  And no exported span contains "SECRET-token"

Scenario: Another client's span is untouched (characterisation)
  Given the same host
  When a request is made to "/v3/paths/get/station-4-roughing" or to any URL outside "/v3/config/paths/"
  Then its url.full is exactly what it was before this change
```

Auth: N/A - the camera-sim control plane is unauthenticated on the docker network
(`CameraSimPathClient.cs` remarks); nothing here touches a token or scope.

### US2 (P1) - The labelled line names the camera, not its label

```gherkin
Scenario: A non-asset camera's labelled line carries the identifier and no label
  Given a non-asset camera C named "Line 7 Spare" at "rtsp://admin:pw@cam.example/SECRET-token/stream"
  When the handler provisions it
  Then the labelled line names C and "Added"
  And no simulator log entry contains "Line 7 Spare"
  And the camera-sim request body still draws "Line 7 Spare"

Scenario: A name carrying CR/LF cannot forge a line
  Given a non-asset camera named "Spare\r\nERROR forged line"
  When the handler provisions it
  Then no simulator log entry's message or structured value contains "forged line", CR or LF from the name

Scenario: A name that is itself a URL with credentials does not reach the log
  Given a non-asset camera named "rtsp://admin:hunter2@cam.example/x"
  When the handler provisions it
  Then no simulator log entry contains "hunter2"
```

### US3 (P2) - Scenario-asset behaviour unchanged (characterisation)

```gherkin
Scenario: A scenario asset camera's log lines are byte-identical to today
  Given asset "station-4-roughing" playing "roughing.mp4"
  When its camera is registered
  Then the lines "Recorded camera {guid} for asset 'station-4-roughing'." and
       "Provisioned camera-sim loop path 'station-4-roughing' playing 'roughing.mp4'." are written
```

This is the existing `A_scenario_asset_camera_logs_its_asset_path_as_today`; it must pass unmodified.

## 3. Functional requirements

- **FR-001** For every outgoing request whose URL path begins `/v3/config/paths/<verb>/`, the exported
  span's `url.full` replaces everything after that prefix (path, query, fragment) with `Redacted`.
- **FR-002** The scheme, host, port, the prefix and the verb stay visible, so add-vs-replace, target and
  failure are still readable on the dashboard.
- **FR-003** Requests outside that prefix keep the runtime's `url.full` unchanged.
- **FR-004** The redaction is registered by `AddCameraSimProvisionerClient`, the registration the worker
  and the tests both build (spec 390 §11 addendum), and composes with any enrich callback already set
  rather than replacing it.
- **FR-005** `CameraSimPathLabelled` takes the camera identifier and the write kind only; `{Label}` is
  removed from the template and the structured state. The label still reaches drawtext through
  `Sanitize`, unchanged.
- **FR-006** Camera-sim requests (verb, path, body), the add-then-replace fallback, the drawn label and the
  hue are unchanged.
- **FR-007** No change to `ServiceDefaults`, `Shared.Kernel`, `Shared.Contracts`, the AppHost or any csproj.

## 4. Independent end-to-end test procedure (phase 5)

1. Boot the AppHost in run mode with the simulator enabled (`ScenarioSimulator=true`; one stack per machine).
2. Register a camera through management-web or `POST /cameras` with URL
   `rtsp://camera-sim:8554/SECRET-token-393/stream` and name `Spare 393` (not a scenario asset).
3. Aspire dashboard -> Traces -> `scenario-simulator`: open the `POST` client span under the
   `CameraRegisteredV1` handler. Expect `url.full` = `http://camera-sim:9997/v3/config/paths/add/Redacted`.
4. Structured logs for `scenario-simulator`: search `SECRET-token-393` and `Spare 393`: zero hits; the
   labelled line names the camera identifier.
5. Camera-sim `GET /v3/config/paths/list` shows the path `SECRET-token-393/stream`: the provisioning still
   happened.

If Docker is down, record that phase 5 is open; the unit rig in plan §6 drives the same runtime
`System.Net.Http` activity and OTel SDK the dashboard receives, but not the OTLP export.

## 5. Decisions and assumptions

- **[DECISION] Scenario-asset paths are redacted on the span as well.** They come from our own scenario
  files and are not secrets, but the enrich callback sees only the request, not where the path came from;
  keeping them visible would need a provenance channel (an `HttpRequestOptions` marker set by the
  provisioner) to preserve information that is already in the same trace: `CameraSimPathProvisioned`,
  `CameraSimPathReplaced` and `CameraSimReconcileFailed` still log the asset path, unchanged (US3). Redact-all
  also fails safe for any future caller of `ProvisionAsync`. Scenario-asset paths stay visible **in the
  logs** and on the `CameraSimPathClient` `GET /v3/paths/get/...` span, which carries scenario paths only.
- **[DECISION] Any verb, not only the four named.** MediaMTX v3 also has `config/paths/get/{name}`; matching
  `/v3/config/paths/<one segment>/` covers it and any later verb at no extra cost. `paths/list` has no
  trailing segment and is unaffected.
- **[DECISION] Path-prefix match, not host match.** The options are process-wide inside the simulator; no
  other client in this process calls a MediaMTX config API, and over-redacting a look-alike URL fails safe.
- **[ASSUMPTION, proved by the red test]** OpenTelemetry.Instrumentation.Http 1.19.0 invokes
  `EnrichWithHttpRequestMessage` for the .NET 10 runtime's native span and an overwrite of `url.full` there
  survives to export (README: "Overrides the value ... `activity.SetTag("url.full", ...)`"). If a test shows
  the runtime re-sets the tag at stop, the same redaction is also applied in the response / exception
  callbacks (plan §4).
- **[ASSUMPTION]** A stub primary handler produces no `System.Net.Http` activity (the runtime's
  diagnostics handler sits inside `SocketsHttpHandler`), so the span test needs a real loopback listener.
- **Lost diagnostics (accepted):** the labelled line no longer shows the label; the camera identifier finds
  the camera's name in the catalog.

## 6. Accepted residuals

- Wolverine's handler span and its exception records for a failing provisioning are not inspected here;
  spec 390 §10.4 found no path in the exception text. Not re-opened.
- Saved dashboard searches on `Label` stop matching.

## 7. Acceptance

Every scenario in §2 green in `ScenarioSimulator.Tests`; each new-behaviour test observed red first with
the failure quoted in the PR (ADR-0139); the US1 "other client" and US3 characterisation tests observed
green before and after, unmodified; `dotnet build -c Release` clean; phase-5 dashboard check per §4 or
recorded open.
