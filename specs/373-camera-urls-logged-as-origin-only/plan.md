# Plan 373: Camera URLs are logged as their origin only

**Spec**: [spec.md](./spec.md). **Issue**: #2908. **Base**: `origin/develop` at `d8070adc`.

## 1. Where the helper lives

**`Shared.Kernel`**, as `SmartSentinelEye.Shared.Kernel.UrlRedaction` in
`src/Shared.Kernel/UrlRedaction.cs`:

```csharp
public static class UrlRedaction
{
    public const string Unparseable = "<unparseable url>";
    public static string ToOrigin(string url);
}
```

Name: "origin" is the standard term for exactly `scheme://host[:port]` (RFC 6454), and the class
name says why the transform exists. No abbreviations (ADR-0091).

| Candidate | Referenced by S1-S3 (`StreamDistribution.Infrastructure`)? | Referenced by S4 (`CameraCatalog.Application`)? | Verdict |
|---|---|---|---|
| `Shared.Kernel` | yes, directly | yes, directly | **Chosen.** Zero new references. No package dependencies of its own; the helper needs only `System.Uri`. It is a language-level URL function, not domain knowledge, so it satisfies §III's "nothing domain-specific" (precedent: `Ensure`, `IClock` already live there). |
| `ServiceDefaults` | yes | **no** | Rejected. `CameraCatalog.Application` would need a new reference to a project that drags in ASP.NET Core, EF Core, Wolverine and OpenTelemetry — an Application layer acquiring the host's composition dependencies for one string function. |
| `Shared.Contracts` | yes | yes | Rejected. It holds versioned wire messages and DTOs; a logging helper is neither. |
| Per-context copies | — | — | Rejected by the owner decision ("one shared helper"). |

No NetArchTest rule changes: both callers already depend on `Shared.Kernel`, and nothing in
`Shared.Kernel` gains a dependency.

## 2. Helper behaviour (invariants)

- **I1** Output = `uri.Scheme + "://" + uri.Host` plus `":" + uri.Port` iff `!uri.IsDefaultPort`
  (for unregistered schemes like `rtsp`, `Port == -1` when absent, which `IsDefaultPort` reports as
  default). `Uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)` is acceptable
  if it meets the test table; the table is the authority, not the mechanism.
- **I2** Never contains userinfo, path, query or fragment.
- **I3** `Uri.TryCreate(url, UriKind.Absolute, out var uri)` false, or `uri.Host` empty ->
  `Unparseable`. The output never contains any part of an unparseable input.
- **I4** `Ensure.That(url).IsNotNull()` first (ADR-0105).
- **I5** Pure, static, no I/O, no allocation beyond the result; no logging of its own.
- Host case: `Uri` lowercases the host (`rtsp://CAM.Example/x` -> `rtsp://cam.example`). Pinned in
  the table so the behaviour is a decision rather than a surprise.
- IPv6 literal hosts keep their brackets (`rtsp://[fd00::5]:554/x` -> `rtsp://[fd00::5]:554`).

## 3. Call-site changes (bounded contexts and layers)

Redaction is applied **at the call site**, inside the method that already makes the log call. The
four `[LoggerMessage]` declarations keep their template text, method name and placeholder name, so
the generated `EventId`/`EventName` and the OTLP attribute key do not move.

| Site | File | Change |
|---|---|---|
| S1 | `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxReconciler.cs:223` | `logger.ReconcilerReaddedMissingPath(path, UrlRedaction.ToOrigin(sourceUrl.Value));` — the `gateway.AddPathAsync` call on the line above keeps `sourceUrl.Value`. |
| S2 | `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:51` | `logger.RegisteredMediaMtxPath(path, UrlRedaction.ToOrigin(rtspSourceUrl));` — the POST body keeps `rtspSourceUrl`. |
| S3 | `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:67` | `logger.RepointedMediaMtxPath(path, UrlRedaction.ToOrigin(rtspSourceUrl));` — the PATCH body keeps `rtspSourceUrl`. |
| S4 | `src/CameraCatalog/Application/Commands/Handlers/ChangeCameraAddressCommandHandler.cs:64` and `src/CameraCatalog/Application/Log.cs:32` | `ChangedCameraAddress`'s last parameter changes from `RtspUrl url` to `string url` (name kept, so `{Url}` still binds); call becomes `logger.ChangedCameraAddress(changing.Id, UrlRedaction.ToOrigin(changing.Url.Value));`. The `using SmartSentinelEye.CameraCatalog.Domain.Camera;` in `Log.cs` stays (`CameraIdentifier`, `CameraName`). |

`src/StreamDistribution/Infrastructure/Log.cs` is **not edited**: its three parameters are already
`string`. `MediaMtxRtspGateway.cs` and `ChangeCameraAddressCommandHandler.cs` already import
`SmartSentinelEye.Shared.Kernel`; `MediaMtxReconciler.cs` gains that one `using`.

**Rejected alternative:** a hand-written public wrapper in each `Log.cs` that redacts and forwards to
a renamed private generated method. It would make the redaction impossible to forget at a future
caller, but renaming the generated methods changes their `EventName` in OTLP unless every one is
pinned with `EventName = "..."`, which is more surface than four one-argument edits. Not pursued;
no new call site exists today.

**Domain / messaging:** none. No entity, value object, domain event or integration event changes
(FR-005). `RtspUrl`, `StreamSourceUrl`, `CameraRegisteredV1` and `CameraAddressChangedV1` are untouched. Boundary rules unchanged: no cross-context
reference is added.

## 4. Tests

### Red, compiling against today's tree (commit 1, test-writer)

| ID | File | What it asserts | Red today because |
|---|---|---|---|
| R1 | `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayLogRedactionTests.cs` (new) | In-file `HttpMessageHandler` stub answering 200 and recording each request body; `CapturingLogger<MediaMtxRtspGateway>` (existing, `Fakes/CapturingLogger.cs`). For `AddPathAsync` and `RepointPathAsync`, each with `rtsp://cam/stream?token=SECRET` and `rtsp://cam/SECRET/path`: (a) the `Information` entry equals `Registered MediaMTX path {path} -> rtsp://cam.` / `Re-pointed MediaMTX path {path} at rtsp://cam.`; (b) no entry's message contains `SECRET`; (c) the recorded request body's `source` equals the full input (FR-005 guard against over-redaction). | today's line ends `-> rtsp://cam/stream?token=SECRET.` |
| R2 | `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressLogRedactionTests.cs` (new; the existing `ChangeCameraAddressCommandHandlerTests` builds its handler with `NullLogger` and is not edited) | Seed via the existing fakes (`InMemoryCameraRepository`, `FixedClock`); `CapturingLogger<ChangeCameraAddressCommandHandler>` (existing). Change to `rtsp://10.0.5.44/stream?token=SECRET` and to `rtsp://10.0.5.44/SECRET/path`: (a) the entry equals `Changed camera {id} address to rtsp://10.0.5.44.`; (b) no entry contains `SECRET`; (c) the stored camera's `Url.Value` is the full input. This is also the first pin of S4's template (the issue noted it was unpinned). | today's line ends `address to rtsp://10.0.5.44/stream?token=SECRET.` |
| R3 | `tests/Integration.Tests/StreamDistribution/MediaMtxReconcilerPassCharacterisationTests.cs` line 89 (existing, spec 351 C7) | Expected `"log Information Reconciler re-added missing MediaMTX path cam-0197a1b2-0000-7000-8000-000000000a05 -> rtsp://10.0.5.5."` (was `…/h264.`). **Only this string changes**; the `add …` gateway-call entries on lines 87-88 keep the full URL (they record what the gateway was asked, which FR-005 keeps). | today's line ends `/h264.` |

### Red by compilation, landing with the fix (commit 2)

| ID | File | What it asserts |
|---|---|---|
| R4 | `tests/Shared.Kernel.Tests/UrlRedactionTests.cs` (new) | `[Theory]` table: `rtsp://10.0.5.5/h264` -> `rtsp://10.0.5.5`; `rtsp://cam/stream?token=SECRET` -> `rtsp://cam`; `rtsp://cam/SECRET/path` -> `rtsp://cam`; `rtsp://cam:8554/x?y=z#frag` -> `rtsp://cam:8554`; `rtsp://user:pass@cam/x` -> `rtsp://cam`; `rtsp://[fd00::5]:554/x` -> `rtsp://[fd00::5]:554`; `rtsp://CAM.Example/x` -> `rtsp://cam.example`; `rtsp://cam` -> `rtsp://cam`. `[Theory]` of unparseable inputs (`not a url SECRET`, `/relative/SECRET`, `""`, `"   "`) -> `UrlRedaction.Unparseable`, and `ShouldNotContain("SECRET")`. `null` -> `ArgumentNullException`. |

R4 cannot compile before `UrlRedaction` exists, so it cannot sit in commit 1 without breaking that
commit's build (CLAUDE.md: each commit builds on its own). Its red is the compile error
(`CS0103`/`CS0246` naming `UrlRedaction`), observed in the working tree and quoted in the PR —
the spec 302 precedent.

### Pinned assertions expected to change, and why

| Assertion | Before | After | Why it may change |
|---|---|---|---|
| C7 line 89 (R3) | `-> rtsp://10.0.5.5/h264.` | `-> rtsp://10.0.5.5.` | The owner's decision on #2908 names this exact change ("update only the `{SourceUrl}`/`{Source}`/`{Url}` rendering assertions"). |

No other existing assertion pins S1-S4's text: `MediaMtxRtspGatewayPaginationTests`,
`MediaMtxRtspGatewayIntegrationTests`, `ProvisionRetireRaceIntegrationTests` use `NullLogger`;
`ChangeCameraAddressCommandHandlerTests` uses `NullLogger`. Any **other** existing assertion going
red after commit 2 is a defect in the change, not a test to edit.

### Counterfactuals (not committed)

- Drop the `!uri.IsDefaultPort` condition so the port is always appended: R4's `rtsp://cam` rows go
  red (`rtsp://cam:-1`).
- Return `uri.GetLeftPart(UriPartial.Path)` instead: R4's path rows and R1/R2 (b) go red.
- In S2, pass the redacted value to the POST body as well: R1 (c) goes red.

## 5. Commands

```sh
# R1
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayLogRedactionTests"
# R2
dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~ChangeCameraAddressLogRedactionTests"
# R3 (Docker + Aspire fixture; only if no other Aspire stack is running on the machine)
dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~MediaMtxReconcilerPassCharacterisationTests"
# R4
dotnet test tests/Shared.Kernel.Tests -c Release --filter "FullyQualifiedName~UrlRedactionTests"
# after the fix: full projects + coverage gate for Shared.Kernel (>= 90%, ADR-0065)
dotnet test tests/Shared.Kernel.Tests -c Release
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release
dotnet test tests/CameraCatalog.Application.Tests -c Release
dotnet test tests/Architecture.Tests -c Release
dotnet build -c Release
dotnet format --verify-no-changes
```

No new Integration.Tests class, so no `ci-shards/shard-N.filter` entry is needed (R3 edits a class
already listed in `shard-2.filter`).

## 6. Risks

| Risk | Mitigation |
|---|---|
| Over-redaction leaks into behaviour (MediaMTX gets the origin, a stream stops pulling) | R1 (c), R2 (c), C7's `add …` entries unchanged; Phase 5 boots the stack and watches a stream play. |
| Platform difference in `Uri` parsing (`/x` as `file://` on Linux) | I3's non-empty-host rule; R4 row `/relative/SECRET` runs on Linux CI. |
| An operator loses information they relied on | The path name (`cam-<guid>`) and camera identifier are already in every line; the full URL remains readable through the authorized camera API. |
| A future log site prints a URL without the helper | Not mechanically prevented (rejected alternative, §3). Phase 6 security review is told to look for it. |

## 7. Constitution / ADR check

§II: no domain model changes. §III: no cross-context reference; `Shared.Kernel` stays
language-level. §IV: N/A (control plane). §VII: the OTLP attribute keys are unchanged.
§VIII: narrows what reaches the telemetry sink. ADR-0050, ADR-0105, ADR-0139, ADR-0144 as cited in
the spec. No new ADR.
