# Plan 390: The Scenario Simulator logs a camera's origin and identifier, never its URL or URL path

Spec: [spec.md](./spec.md). Tasks: [tasks.md](./tasks.md). Issue #2976. ADRs 0111, 0050, 0139, 0143, 0144.

## 1. Context and layers

Not a bounded context. `src/ScenarioSimulator` is the dev-only worker of ADR-0111 (never deployed;
AppHost gates it at `AppHost.cs:577`). It has no Domain / Application / Infrastructure split, no
persistence and no Api; it is a flat worker project with `Log.cs` as its single `[LoggerMessage]` source
(ADR-0050). Constitution §II (primitive ban) binds domain models; the simulator has none, and its
`Log.cs` already takes `Guid camera` throughout. This plan mirrors that.

Files touched (production):

| File | Change |
|---|---|
| `src/ScenarioSimulator/Log.cs` | Four templates change, one is added (§3). |
| `src/ScenarioSimulator/EventHandlers/CameraRegisteredSimHandler.cs` | Redact the skip line; branch the recorded line on asset / non-asset (§4). |
| `src/ScenarioSimulator/CameraSim/CameraSimProvisioner.cs` | `ProvisionAsync` returns what it did and stops logging; each public method logs its own line (§5). Public signatures unchanged. |
| `src/ScenarioSimulator/CameraCatalog/CameraCatalogClient.cs` | `:59` renders the origin (§6). |
| `src/ScenarioSimulator/appsettings.json` | One logging filter (§7). |

Not touched: `Shared.Kernel/UrlRedaction.cs` (FR-007), `RtspPath.cs` (the path extraction is
functional), any `Shared.Contracts` type, any csproj (Shared.Kernel arrives through Shared.Contracts).

## 2. Types and invariants

No new public type. One enum nested in `CameraSimProvisioner` (built as `internal`, the second option below, because `Log` is `internal static partial` and must see it):

```csharp
internal enum PathWrite { Added, Replaced }
```

Invariants (each is a test in §8):

- **I1** For a camera that is not a scenario asset, no line written by `CameraRegisteredSimHandler` or
  `CameraSimProvisioner` contains the URL, its userinfo, its query, or its path.
- **I2** A URL rendered into a simulator log line has passed through `UrlRedaction.ToOrigin`, or is
  `UrlRedaction.Unparseable` for a `null` URL.
- **I3** Camera-sim requests, correlation rows and wall attempts are identical, in content and order, to
  today's (FR-006).
- **I4** Scenario-asset lines are byte-identical to today's (FR-004).

## 3. Logging (ADR-0050)

| Method | Today | After |
|---|---|---|
| `SkippedNonSimulatedCamera` | `(string url)` `Camera URL {Url} has no path component; not a simulated camera, skipping.` | `(Guid camera, string origin)` `Camera {Camera} at {Origin} has no path component; not a simulated camera, skipping.` |
| `CameraRegistered` | `(string name, string rtspUrl)` `Registered camera '{Name}' -> {RtspUrl}.` | `(string name, string origin)` `Registered camera '{Name}' -> {Origin}.` |
| `CameraSimPathLabelled` | `(string path, string label, Guid camera)` `camera-sim path '{Path}' now carries the label '{Label}' for camera {Camera}. The label and hue belong to the path: ...` | `(string label, Guid camera, PathWrite write)` - path removed; `{Write}` carries `Added` / `Replaced`. Keep the "two cameras at the same simulator URL share it, and the later one wins" sentence: it is still true and still the reason the line exists. |
| **new** `NonAssetCameraRecorded` | - | `(Guid camera, string origin)` Information: `Recorded camera {Camera} at {Origin}; it is not a scenario asset, so it plays the shared clip with its name drawn on it.` |
| `AssetCameraRecorded`, `CameraSimPathProvisioned`, `CameraSimPathReplaced` | unchanged | unchanged; now called **only** on the scenario-asset branch |

`PathWrite` is private to the provisioner, so `CameraSimPathLabelled`'s parameter must be a type `Log`
can see: make the enum `internal` and nested, or declare it `internal` in `CameraSim/`. The engineer picks
whichever compiles with `Log` being `internal static partial`; both are within the file set above.
`[LoggerMessage]` formats an enum argument by `ToString()`; no custom formatter.

Levels unchanged (all Information). Event names follow the method names.

## 4. The handler

```csharp
var (camera, name, url, _, _, _) = message;

if (!RtspPath.TryExtract(url, out var path))
{
    logger.SkippedNonSimulatedCamera(camera, OriginOf(url));
    return;
}

var isAsset = TryFindAssetClip(path, out var clip);   // moved up; pure read of IOptions

correlation.RecordCamera(path, camera);               // unchanged position
if (isAsset) logger.AssetCameraRecorded(path, camera);
else logger.NonAssetCameraRecorded(camera, OriginOf(url));

await wall.TryCreateAsync(cancellationToken);         // unchanged position

if (isAsset) { await provisioner.ProvisionLoopPathAsync(path, clip, cancellationToken); return; }
await provisioner.ProvisionLabelledPathAsync(path, name, camera, cancellationToken);
```

`OriginOf(string? url) => url is null ? UrlRedaction.Unparseable : UrlRedaction.ToOrigin(url)` (FR-008):
the message is the trust boundary, `RtspPath.TryExtract` already tolerates `null`, and `ToOrigin`'s
`Ensure.That(url).IsNotNull()` would otherwise turn a log-and-drop into a Wolverine retry loop. This is
validation at a trust boundary, not drive-by handling. The deconstruction order and discards are unchanged
(`HandlerDeconstructionTests`).

Moving `TryFindAssetClip` before `RecordCamera` changes no behaviour: it reads only
`scenarioOptions.Value`, has no side effects, and its result was previously used after two calls that do
not touch options.

## 5. The provisioner

- `ProvisionAsync(path, command, ct)` -> `Task<PathWrite>` (the `clip` parameter was dropped: it was only logged and logging moved out): same requests, same
  `EnsureSuccessStatusCode` calls, same "already exists" test; returns `Added` where it logged
  `CameraSimPathProvisioned` and `Replaced` where it logged `CameraSimPathReplaced`; **logs nothing**.
- `ProvisionLoopPathAsync` becomes `async`: `Ensure.That(clip)...` first (unchanged), then
  `var write = await ProvisionAsync(...)`, then `CameraSimPathProvisioned(path, clip)` or
  `CameraSimPathReplaced(path, clip)`. Lines byte-identical to today (I4).
- `ProvisionLabelledPathAsync`: unchanged up to the `ProvisionAsync` call; then
  `logger.CameraSimPathLabelled(label, camera, write)` only. The comment block at `:96-103` keeps its
  reasoning; reword "per simulator path" only where it names the logged path.

`ProvisionLoopPathAsync` is also called by `CameraSimReconciler` with config paths; its lines stay as they
are.

## 6. The catalog client

`CameraCatalogClient.cs:59`: `logger.CameraRegistered(name, UrlRedaction.ToOrigin(rtspUrl));` and add
`using SmartSentinelEye.Shared.Kernel;`. `rtspUrl` is never null (built by interpolation). The request
body (`RegisterCameraBody(name, rtspUrl)`) is unchanged.

## 7. The HTTP client's request log

`src/ScenarioSimulator/appsettings.json` -> `Logging:LogLevel`:

```json
"System.Net.Http.HttpClient.CameraSimProvisioner": "Warning"
```

`AddHttpClient<CameraSimProvisioner>` names the client after the type (no namespace), and
IHttpClientFactory's logging categories are `System.Net.Http.HttpClient.{name}.LogicalHandler` and
`.ClientHandler`; the prefix filter covers both. Precedent: `src/StreamDistribution/Api/appsettings.json:7`
(`System.Net.Http.HttpClient.IRtspGateway`, commit `937340bc`). The camera-sim client logs nothing at
Warning or above itself, so the filter loses no failure signal: a failed provisioning still surfaces as the
handler's exception, logged by Wolverine.

The catalog client's `POST /cameras` URI carries no camera URL (it is in the body, which the factory never
logs), so it is not filtered.

## 8. Messaging

None changed. `CameraRegisteredV1` is consumed exactly as before; no integration event is added; no queue
or Wolverine policy changes.

## 9. Boundary rules

No project reference added. `Shared.Kernel` is reached through `Shared.Contracts` as it already is
(`CameraRegisteredSimHandler.cs:6`). NetArchTest rules are unaffected: the simulator references no
bounded context.

## 10. Tests (phase 4a: RED; adversarial required)

### 10.1 Existing tests that pin changed log text

**None.** Every changed template was searched in `tests/`; `CameraSimProvisionerTests` and
`CameraCatalogClientTests` use `NullLogger`, and nothing constructs `CameraRegisteredSimHandler`. **No
existing test may be edited.** If one fails, the change moved behaviour: stop and report.

### 10.2 Existing tests that guard behaviour (must stay green, unmodified)

- `tests/ScenarioSimulator.Tests/CameraSimProvisionerTests.cs` (request bodies, replace-on-duplicate,
  labelled command shape, hue).
- `tests/ScenarioSimulator.Tests/CameraCatalogClientTests.cs` (registration, 409 read-back, paging).
- `tests/ScenarioSimulator.Tests/ScenarioSeeder*Tests.cs`, `SimulatorRuntimeBindingTests.cs` (the
  seeder and config binding that share the provisioner and the client).
- `tests/Architecture.Tests` `HandlerDeconstructionTests` (the handler's deconstruction).

### 10.3 New tests (test-writer)

All under `tests/ScenarioSimulator.Tests/`, xUnit + Shouldly, sentence-style names (ADR-0053),
hand-written fakes (ADR-0054). Every one compiles against today's code (public signatures unchanged) and
fails on an assertion.

1. **`CameraRegisteredSimHandlerLogRedactionTests.cs`** with a rig `Fakes/SimHandlerRig.cs` building the
   real handler from: `CameraSimProvisioner(new HttpClient(recordingHandler){BaseAddress}, RecordingLogger<CameraSimProvisioner>)`,
   `new AssetCorrelationTable()`, `WallSeeder` exactly as `SeederRig.cs:137-147` builds it,
   `Options.Create(scenarioOptions)`, `RecordingLogger<CameraRegisteredSimHandler>`. The recording camera-sim
   handler answers 200, or 400 `"path already exists"` then 200 (replace branch).
   - `A_camera_with_no_url_path_is_skipped_by_identifier_and_origin` (US1 s1) - RED.
   - `A_non_asset_camera_never_logs_its_url_path` (US1 s2) - RED on the path assertion; the same test
     asserts the camera-sim request path and the correlation row (I3), which already hold.
   - the replace branch (US1 s3) is the `cameraSimAlreadyHasThePath: true` row of the `A_non_asset_camera_never_logs_its_url_path` Theory, not a separate test - RED.
   - `An_unparseable_url_is_skipped_without_being_printed` (US1 s4) - RED.
   - `A_scenario_asset_camera_logs_its_asset_path_as_today` (US2) - **characterisation, GREEN** before
     and after, asserting the two exact strings.
2. **`CameraCatalogClientLogRedactionTests.cs`** - own stub handler and `KeycloakTokenProvider` built as
   `CameraCatalogClientTests.cs:172-196` does, with `RecordingLogger<CameraCatalogClient>`:
   - `Registering_a_scenario_camera_logs_the_origin_not_the_path` (US3) - RED; also asserts the POST body
     still carries the full URL (I3, green).
3. **`CameraSimProvisionerHttpLoggingTests.cs`** - a `ServiceCollection` with
   `AddLogging(b => b.AddConfiguration(<simulator appsettings.json Logging section>).AddProvider(recorder))`
   and `AddHttpClient<CameraSimProvisioner>(c => c.BaseAddress = ...).ConfigurePrimaryHttpMessageHandler(() => stub)`;
   load `appsettings.json` from `AppContext.BaseDirectory` as `ScenarioFileTests.cs:86` does, but
   **not optional**: a missing file must fail the test rather than pass it. Needs a small
   `Fakes/RecordingLoggerProvider.cs` that keeps `(Category, Level, Message)`.
   - `The_camera_sim_client_request_log_does_not_print_the_path` (US4 s1) - RED.
   - `The_recorder_sees_the_camera_sim_client_request_log_when_its_level_allows` (US4 s2) -
     counterfactual, GREEN before and after; proves s1 can fail.

Secrets used in fixtures are distinct, greppable tokens (`hunter2`, `SECRET-token`, `secret-path`); each
assertion checks **every** recorded entry, not the line it expects to change, so a leak moved to another
line still fails.

Run:

```sh
dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj \
  --filter "FullyQualifiedName~LogRedactionTests|FullyQualifiedName~HttpLoggingTests"
```

Whole-project guard run:
`dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj` and
`dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstruction"`.
`ScenarioSimulator.Tests` is a unit project, not an Integration.Tests shard; no `ci-shards/*.filter`
entry is needed.

### 10.4 Adversarial (test-adversary)

Assume a leak survives somewhere and hunt it, writing failing tests where found:

- Exception text: a camera-sim 500 / connection refusal / timeout from `ProvisionLabelledPathAsync` -
  does `exception.ToString()` (what Wolverine logs) contain the path? Also an `OperationCanceledException`.
- `null`, empty, whitespace, `rtsp:\\admin:1#@cam/x`, `rtsp://cam/%53ECRET` (percent-encoded path),
  IPv6 with zone, userinfo-only URLs, a path equal to a scenario asset path but a different host (it takes
  the asset branch and logs the path: confirm that path is the config string, not operator text).
- Category coverage: `Polly` / resilience events for the camera-sim client (timeout, circuit breaker)
  with ServiceDefaults' standard handler in the rig; `System.Net.Http.HttpClient.CameraSimProvisioner.ClientHandler`.
- The label: an operator name containing the URL (`name = "rtsp://admin:pw@..."`) is logged as the label
  by design; record whether that is a leak worth a residual (it is operator-typed display text, not the URL
  field).

## 11. Risks

- **Scope extension (spec §5)** beyond the issue's named sites: D/E/F and the HTTP filter. Low cost to
  strike if the reviewer disagrees; recorded as `[ASSUMPTION]`.
- **Lost diagnostics.** A non-asset camera's lines no longer show which camera-sim path it got; the
  identifier and origin replace it, and camera-sim's own API lists paths. Accepted (owner decision).
- **Filter breadth.** The `Warning` filter also silences the factory's Information lines for
  scenario-asset provisioning, which were not secret. They duplicated `CameraSimPathProvisioned`;
  acceptable.
- **PR #3018** edits `UrlRedaction.cs` internals; no overlap with this file set. Rebase after whichever
  merges first; `ToOrigin` is unchanged in both.
- **Trace attributes** still carry the path (spec §6).

## 11. Addendum: the factory scope, and one shared registration

**Finding (`96b1a286`).** §1 and spec §6 assumed Polly would not log the URI. Polly's message does not, but
its `Execution attempt` records (Information, written for every request, success included) are emitted
inside `IHttpClientFactory`'s `HTTP {HttpMethod} {Uri}` scope, and ServiceDefaults exports scopes
(`IncludeScopes = true`). Each exported record therefore carried
`Uri = http://camera-sim:9997/v3/config/paths/add/<operator URL path>`. The appsettings category filter
cannot reach it: the category is `Polly`.

**Fix.** `.RemoveAllLoggers()` on the `CameraSimProvisioner` typed-client registration removes the factory's
logging handlers and their scope. Failures still reach callers as exceptions (the 500, refused and cancel
adversarial tests stay green). The `appsettings.json` filter stays as defence in depth.

**Lesson - the rig bypassed `Program.cs`.** The red scope tests (`96b1a286`) built their own host and called
`AddHttpClient<CameraSimProvisioner>` themselves, so a fix in `Program.cs` could never turn them green.
The registration was extracted (`7f4667de`) into
`internal static IHttpClientBuilder AddCameraSimProvisionerClient(this IServiceCollection services)`
(`CameraSim/CameraSimProvisionerExtensions.cs`; `InternalsVisibleTo` to the test project already existed).
`Program.cs` and the rig now share ONE registration (`8ebe608c`). The fix is `6f627422`. A red test must
exercise the code path that carries the fix.

**Harness.** The recorders are `RecordingLoggerProvider` (category, level, message, structured state) and
`ScopedRecordingLoggerProvider` (adds scopes), not `RecordingLogger<T>`, because the leak is in scopes and
in framework categories. `The_recorder_sees_the_request_uri_scope_when_a_line_is_written_inside_it` passes
`keepFactoryLoggers: true` (a plain `AddHttpClient`): its job is to prove the recorder can see the scope
production now removes; its assertion is unchanged.

**Characterisation (`613d5f97`).** The correlation row, the wall attempt and the replace-branch log line are
asserted unchanged (spec I3/FR-006).
