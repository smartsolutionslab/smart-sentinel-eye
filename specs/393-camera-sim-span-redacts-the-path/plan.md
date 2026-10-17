# Plan 393: The camera-sim client span names the MediaMTX verb, not the camera path; the labelled line names the camera, not its label

Spec: [spec.md](./spec.md). Tasks: [tasks.md](./tasks.md). Issue #3019. ADRs 0111, 0050, 0118, 0139, 0144.

## 1. Context and layers

Not a bounded context. `src/ScenarioSimulator` is ADR-0111's dev-only flat worker (no Domain /
Application / Infrastructure / Api split, no persistence). No cross-context reference is added; the only
new dependency is on `OpenTelemetry.Instrumentation.Http`, which already reaches the project transitively
through `ServiceDefaults` (no csproj edit; the build proves it).

| File | Change |
|---|---|
| `src/ScenarioSimulator/CameraSim/CameraSimSpanRedaction.cs` (new) | Pure redaction of a `url.full` string + the activity hook (§3). |
| `src/ScenarioSimulator/CameraSim/CameraSimProvisionerExtensions.cs` | `AddCameraSimProvisionerClient` also configures `HttpClientTraceInstrumentationOptions` (§4). |
| `src/ScenarioSimulator/Log.cs` | `CameraSimPathLabelled` drops `{Label}` (§5). |
| `src/ScenarioSimulator/CameraSim/CameraSimProvisioner.cs` | `:120` call site passes `(camera, write)`; comment above it updated to say the label is not logged either. |

Not touched: `ServiceDefaults/Extensions.cs` (its `.AddHttpClientInstrumentation()` reads the default-named
options, so a `Configure<>` here applies to it), `Program.cs` (it already calls
`AddCameraSimProvisionerClient`), `RtspPath.cs`, `Shared.*`, AppHost.

## 2. Types and invariants

No public type. One `internal static class CameraSimSpanRedaction`:

```csharp
internal const string Placeholder = "Redacted";   // the instrumentation's own query placeholder
internal static string Redact(string urlFull);      // pure, total, never throws
internal static void Apply(Activity activity);      // rewrites the "url.full" tag if present
```

- **I1** For any `url.full` whose path starts `/v3/config/paths/<segment>/`, `Redact` returns
  `<scheme>://<authority>/v3/config/paths/<segment>/Redacted`; nothing after the verb survives (path,
  query, fragment).
- **I2** For any other input - another path, `/v3/config/paths/list`, `/v3/config/paths/add` with no
  trailing segment, an unparseable string - `Redact` returns the input unchanged.
- **I3** The redaction never throws (it runs inside the instrumentation's callback; an exception there is
  swallowed by the SDK and leaves the tag unredacted - so it must not happen). Operate on the string, do
  not `new Uri(...)`; find the prefix with ordinal search after the authority.
- **I4** Camera-sim's request is unchanged: the hook touches the `Activity`, never the `HttpRequestMessage`.
- **I5** Composition: an enrich callback set before ours still runs (FR-004).

`Redact` takes `string` because `url.full` is the runtime's string and this is not a domain model
(constitution §II binds domain models; the simulator has none, as spec 390 §1 records).

## 3. The redaction

Read `activity.GetTagItem("url.full") as string`; if non-null, `activity.SetTag("url.full", Redact(value))`.
Rewriting the tag the runtime produced (rather than re-rendering `request.RequestUri`) keeps the runtime's
own normalisation and query redaction for every URL outside the prefix, and makes the hook idempotent, so
it is safe in more than one callback.

Matching: locate the path start (first `/` after `://`), require ordinal prefix `/v3/config/paths/`, then
one non-empty segment and a `/`; truncate after that `/` and append `Placeholder`. Case-sensitive, as
MediaMTX is.

## 4. Registration

```csharp
internal static IHttpClientBuilder AddCameraSimProvisionerClient(this IServiceCollection services)
{
    services.Configure<HttpClientTraceInstrumentationOptions>(options =>
    {
        var previous = options.EnrichWithHttpRequestMessage;
        options.EnrichWithHttpRequestMessage = (activity, request) =>
        {
            previous?.Invoke(activity, request);
            CameraSimSpanRedaction.Apply(activity);
        };
    });

    return services.AddHttpClient<CameraSimProvisioner>(...)   // unchanged
        .RemoveAllLoggers();
}
```

Shape only; the engineer mirrors house style (one fluent chain where it reads, `Ensure.That` not
needed - no argument guard is added). A comment states **why** (the path is operator-typed URL text; the
callback is process-wide, matched by prefix).

Start with the request callback only. If the span tests (T004-T006) show `url.full` re-set after start,
add the same `Apply` to `EnrichWithHttpResponseMessage` and `EnrichWithException`, composed the same way.
Do not add them speculatively.

The pinned package: `OpenTelemetry.Instrumentation.Http` 1.19.0 (`Directory.Packages.props:100`). Its
README documents overriding `url.full` from an enrich callback and that on .NET 9+ the native attributes
are not otherwise changed by the library. No URL-path redaction option exists in it (only the query
redaction toggle `OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION`), hence the enrich hook.

## 5. Logging (ADR-0050)

| Method | Today | After |
|---|---|---|
| `CameraSimPathLabelled` | `(string label, Guid camera, PathWrite write)` `camera-sim path was {Write} and now carries the label '{Label}' for camera {Camera}. The label and hue belong to the path: ...` | `(Guid camera, PathWrite write)` `camera-sim path was {Write} and now carries the label of camera {Camera}. The label and hue belong to the path: two cameras registered at the same simulator URL share it, and the later one wins.` |

Removing the field neutralises CR/LF in the label for this line by construction; no other simulator line
logs a non-asset camera's name (`CameraRegistered`, `CameraAlreadyRegistered`, `CameraReadBackFailed` log
scenario names from our own config). Level unchanged.

## 6. Test rig (for the test-writer)

**Span tests need a real socket.** Build the host exactly as
`CameraSimProvisionerLogRedactionAdversarialTests.Rig.Build` does (`Host.CreateApplicationBuilder`,
`AddServiceDefaults()`, `AddCameraSimProvisionerClient()`), but:

- **no** `ConfigurePrimaryHttpMessageHandler`: point `SimulatorOptions.CameraSimApiUrl` at a loopback
  listener - a minimal Kestrel `WebApplication` on `http://127.0.0.1:0` (ASP.NET Core arrives through
  ServiceDefaults' `FrameworkReference`) that records request paths and answers a scripted status/body.
  A stub handler yields no `System.Net.Http` activity and the test would be vacuous.
- capture spans by adding a processor to the tracer provider ServiceDefaults builds - mirror
  `tests/Architecture.Tests/Persistence/PostgresTracingRegistrationTests.cs:76`
  (`tracing.AddProcessor(new SimpleActivityExportProcessor(exporter))` with a hand-written collecting
  `BaseExporter<Activity>`; no new package) via `builder.Services.ConfigureOpenTelemetryTracerProvider`;
  **start the host** (or resolve `TracerProvider`) so the instrumentation subscribes.
- non-vacuity in every span test: assert exactly the expected number of client spans with
  `http.request.method` = `POST` was captured before asserting their content.
- dump every tag of every captured span plus `DisplayName` into the failure message.

Pure `Redact` tests need no rig.

## 7. Tests (phase 4a)

| ID | File | Test | Colour |
|---|---|---|---|
| T001 | `CameraSimSpanRedactionTests.cs` (new) | `Redact` table: each of add/replace/patch/delete/get with `SECRET-token/stream`; with `?token=x`; with `#frag`; percent-encoded `%53ECRET`; IPv6 authority `http://[::1]:9997/...`; no-port authority | RED (type does not exist: compile failure is the red; quote it) |
| T002 | same | `Redact` leaves alone: `/v3/paths/get/station-4`, `/v3/config/paths/list`, `/v3/config/paths/add`, `/v3/config/paths/add/`, `/cameras`, empty, `not a url` | RED with T001 (same compile), then green |
| T003 | `CameraSimProvisionerSpanTests.cs` (new) | add, 200 -> `url.full` = `{base}v3/config/paths/add/Redacted`; nothing contains `SECRET-token`; listener saw the real path | RED (observed: url.full carries the path) |
| T004 | same | 400 "already exists" then 200 -> two spans, add + replace both redacted; listener saw the replace path | RED |
| T005 | same | 500 -> throws; span redacted | RED |
| T006 | same | **characterisation, GREEN before and after**: a plain `HttpClient` from the same host's factory `GET {base}v3/paths/get/station-4-roughing` -> `url.full` ends `/v3/paths/get/station-4-roughing` | GREEN |
| T007 | `CameraRegisteredSimHandlerLogRedactionTests.cs` (existing) | `A_non_asset_camera_never_logs_its_url_path`: **authorised edit** of `:52` from `ShouldContain("Line 7 Spare")` to `ShouldNotContain(...)`, and add an assertion that the camera-sim request body still contains `Line 7 Spare` | RED |
| T008 | same | new: `A_non_asset_camera_name_with_line_breaks_cannot_forge_a_line` (`"Spare\r\nERROR forged line"`) and `A_non_asset_camera_name_that_is_a_url_is_not_logged` (`rtsp://admin:hunter2@cam.example/x`), asserting over message **and** structured state | RED |
| T009 | same | `A_scenario_asset_camera_logs_its_asset_path_as_today` - existing, unmodified | GREEN |

The `:52` edit is the only existing assertion changed; it pins the label in the log, which is exactly the
behaviour FR-005 removes. `CameraSimProvisionerTests.cs:78` asserts the label in the request **body** and
stays as is.

Commands:

```sh
dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj \
  --filter "FullyQualifiedName~CameraSimSpanRedactionTests|FullyQualifiedName~CameraSimProvisionerSpanTests|FullyQualifiedName~CameraRegisteredSimHandlerLogRedactionTests"
```

Guard runs: the whole `ScenarioSimulator.Tests` project, and
`dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstruction|FullyQualifiedName~PrimitiveBoundary"`.
`ScenarioSimulator.Tests` is a unit project, not an `Integration.Tests` shard: no `ci-shards/*.filter`
entry.

## 8. Adversarial (test-adversary, phase 4)

Connection refused (listener stopped) and attempt timeout through the standard resilience handler: is the
span redacted when there is no response (decides whether `EnrichWithException` is needed)? Retries: does
each attempt's span get redacted? A path containing `/v3/config/paths/add/` again inside itself. A
`url.full` with userinfo. Does anything else in the trace (a Polly event, an `exception.message` tag with
`RecordException` on) carry the path?

## 9. Risks

- **Process-wide hook.** Configuring the default-named options affects every `HttpClient` span in the
  simulator; bounded by the prefix match and I2/T006.
- **Enrich ordering assumption** (spec §5): disproved cheaply by T003; the fallback is in §4.
- **Phase 5 needs Docker and the shared stack** (one stack per machine).
- **Security review**: advisable but short - the change is a redaction claim; no auth, scope or shared
  helper moves.
