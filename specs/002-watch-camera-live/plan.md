# Implementation Plan: 002 — Watch a Camera Live

**Branch:** `002-watch-camera-live` | **Date:** 2026-05-26 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from
`specs/002-watch-camera-live/spec.md` (Phase 1 closed; zero
`[NEEDS CLARIFICATION]` markers).

## Summary

Implements the first end-to-end slice through the **StreamDistribution**
bounded context:

- **External resource:** Add **MediaMTX** as an Aspire container
  resource. MediaMTX pulls each registered camera's RTSP source 24/7
  (always-on lifecycle per Resolved Clarification #3) and exposes WHEP
  for browser playback.
- **Backend:** New `Stream` aggregate (state machine
  `Provisioning → Healthy → Degraded → Offline`), Wolverine consumer
  for `CameraRegisteredV1`, idempotent `ProvisionStreamCommand` that
  POSTs to MediaMTX's path API, a background health watcher polling
  MediaMTX's runtime metrics, a `GET /streams` read endpoint, an
  external auth-hook endpoint that MediaMTX calls back into for WHEP
  bearer-token validation, and `StreamHealthChangedV1` publishing on
  every state transition.
- **Frontend:** New `streamsApi` RTK Query slice. New shared
  `CameraViewer` composite in `apps/shared/src/ui/composites/`
  encapsulating the WHEP handshake and `<video>` element so spec 003
  (Layout Composition) can embed it without modification. Cameras list
  gains a health badge column that polls `/streams?cameraIdentifiers=...`
  every 5 seconds.
- **Tests:** Two new per-layer test projects (`StreamDistribution.Domain.Tests`,
  `StreamDistribution.Application.Tests`), Stream-related integration
  tests reusing the existing `AspireFixture` with a MediaMTX container
  + an RTSP source fixture (Bluenviron's test RTSP server image).

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Frontend language | TypeScript / React 19 | ADR-0001, ADR-0074 |
| SFU | **MediaMTX** container (`bluenviron/mediamtx:latest-ffmpeg`) | spec Resolved Clarification #6, ADR-0011 |
| Persistence | EF Core on Postgres (per-context DB `stream-distribution-db`) | ADR-0009 |
| Messaging | RabbitMQ via Wolverine (per-module queue isolation, eager transactions) | ADR-0010, ADR-0042, ADR-0088 |
| Identity | Keycloak; `sse.management` scope required for control plane and WHEP | ADR-0007, ADR-0023, spec FR-007 |
| API style | Minimal APIs only | ADR-0070 |
| Errors | `Result<T, ApiError>` with sealed-record `StreamError` / `StreamAuthError` hierarchies | ADR-0047, ADR-0089 |
| Frontend state | Redux Toolkit + RTK Query; one slice per bounded context (`camerasApi`, `streamsApi`) | ADR-0075 |
| WebRTC client | Plain `RTCPeerConnection` against MediaMTX's WHEP endpoint; no third-party WebRTC SDK | (locked here; matches MediaMTX's native protocol) |
| Tests | xUnit + Shouldly + Moq + Testcontainers via `AspireFixture` (extended) | ADR-0052, ADR-0068 |
| Performance goals | `GET /streams/{id}` ≤ 100 ms p95 (FR-012). Click-to-first-frame ≤ 3 s p95 (FR-013). Outage / recovery detection ≤ 10 s (SC-003 / SC-004). | spec |
| Latency budget | Spec 002 produces the substrate; constitution §IV's 800 ms event-to-overlay budget starts ticking only AFTER the first frame is rendered. | ADR-0015 |
| Scale | 250 cameras per fab; single MediaMTX instance per fab is the v1 assumption | spec Assumptions |

## Constitution Check

Verifying alignment with each load-bearing principle before
implementation begins. Re-checked after data model is drafted.

| Principle | Check | Status |
|---|---|---|
| §I On-prem first, cloud-ready | MediaMTX runs on the same fab host as the rest of the stack; no cloud calls. WHEP signalling stays on the fab LAN. ICE uses host candidates only (spec Assumptions). | ✅ |
| §II DDD + value objects | `StreamIdentifier`, `MediaMtxPath`, `StreamState`, `TranscodeMode`, `StreamError` are maximalist value objects per ADR-0038, hand-written per ADR-0046, with `IValueObject<T>` markers per ADR-0066. | ✅ |
| §III Bounded-context isolation | All new work in `SmartSentinelEye.StreamDistribution.*`. Cross-context contracts via `Shared.Contracts/StreamDistribution/` (out) and `Shared.Contracts/CameraCatalog/` (in). NetArchTest enforces. No project references between `StreamDistribution` and `CameraCatalog`. | ✅ |
| §IV Latency budget sacred | Spec 002 produces the FIRST frame; the 800 ms event-to-overlay budget begins ticking AFTER that. Camera→SFU leg is bounded by MediaMTX (≤ 80 ms per budget). SFU→kiosk decode is bounded by the browser's WebRTC pipeline (≤ 120 ms). PR will report measured click-to-first-frame from the integration test. | ✅ |
| §V Spec-driven | Spec exists (PR #94 merged). This plan exists. Tasks follow. | ✅ |
| §VI Aspire is composition root | MediaMTX wired as `builder.AddContainer("mediamtx", ...)` in `AppHost.cs`. StreamDistribution.Api gets `WithReference(mediamtx)` so service discovery resolves the management URL. `stream-distribution-db` is a `postgres.AddDatabase(...)` per-context resource. | ✅ |
| §VII Observability mandatory | Stream state-machine transitions logged via `ILogger<T>` with structured fields `{ cameraIdentifier, fromState, toState }`. OpenTelemetry traces span the `CameraRegisteredV1 → ProvisionStreamCommand → MediaMTX HTTP POST` call. | ✅ |
| §VIII Safe at trust boundaries | `[Authorize(Policy = "admin")]` on `/streams/*` endpoints. WHEP requires `sse.management` scope, enforced by MediaMTX's external auth hook calling `POST /streams/{path}/authorize` on StreamDistribution.Api. Validation rejects malformed input at the API edge AND at value-object constructors. | ✅ |
| §IX Forward-compatible interfaces | `ICommandHandler<,>` / `IQueryHandler<,>` interfaces stay framework-agnostic; Wolverine dispatcher behind them. `IRtspGateway` abstracts the MediaMTX HTTP API behind a domain-friendly interface so a future swap (LiveKit, custom Pion) is a single-class change. | ✅ |

**Result:** No constitutional violations. No Complexity Tracking
entries needed.

## Project Structure

### Documentation (this feature)

```
specs/002-watch-camera-live/
├── spec.md          ← Phase 1 (PR #94, merged)
├── plan.md          ← this file (Phase 2)
└── tasks.md         ← Phase 3 (next; created by /speckit-tasks)
```

### Source Code — files added / modified

```
src/StreamDistribution/Domain/                          ← scaffold exists; populated here
└── Stream/                                              ← new aggregate folder (ADR-0092)
    ├── Stream.cs                                        ← aggregate root
    ├── StreamIdentifier.cs                              ← Guid v7-backed IStronglyTypedId<Guid>
    ├── StreamState.cs                                   ← enum-backed VO: Provisioning|Healthy|Degraded|Offline
    ├── TranscodeMode.cs                                 ← enum-backed VO: Passthrough|Software|Unknown
    ├── MediaMtxPath.cs                                  ← string-backed VO; derived as cam-{cameraId}
    ├── IStreamRepository.cs                             ← domain repository contract
    ├── IRtspGateway.cs                                  ← abstracts MediaMTX HTTP API; implemented in Infra
    └── Events/
        ├── StreamProvisionedDomainEvent.cs              ← in-process domain event
        └── StreamHealthChangedDomainEvent.cs

src/StreamDistribution/Application/
├── Commands/
│   ├── ProvisionStreamCommand.cs                        ← record : ICommand<Result<StreamIdentifier, ProvisionStreamError>>
│   ├── ProvisionStreamErrors.cs                         ← sealed-record hierarchy : ApiError
│   ├── ReportStreamHealthCommand.cs                     ← invoked by the health watcher
│   ├── ReportStreamHealthErrors.cs
│   ├── AuthorizeWhepCommand.cs                          ← invoked by MediaMTX's external auth hook
│   ├── AuthorizeWhepErrors.cs
│   └── Handlers/
│       ├── ProvisionStreamCommandHandler.cs
│       ├── ReportStreamHealthCommandHandler.cs
│       └── AuthorizeWhepCommandHandler.cs
├── Queries/
│   ├── GetStreamQuery.cs
│   ├── ListStreamsQuery.cs                              ← batch by cameraIdentifiers
│   ├── ListStreamsErrors.cs
│   └── Handlers/
│       ├── GetStreamQueryHandler.cs
│       └── ListStreamsQueryHandler.cs
├── EventHandlers/
│   ├── CameraRegisteredIntegrationEventHandler.cs       ← Wolverine subscriber → ProvisionStreamCommand
│   ├── StreamProvisionedDomainEventHandler.cs           ← logs + (later) audit
│   └── StreamHealthChangedDomainEventHandler.cs         ← translates to StreamHealthChangedV1 + outbox
└── DTOs/
    ├── StreamHealthDto.cs                               ← { cameraIdentifier, state, whepUrl, transcodeMode, lastSuccessAt, error? }
    └── StreamListItemDto.cs                             ← same shape, returned in batch list

src/StreamDistribution/Infrastructure/
├── StreamDistributionInfrastructureModule.cs            ← AddStreamDistributionInfrastructure()
├── StreamDistributionPersistenceModule.cs               ← AddStreamDistributionPersistence() (slim — Domain+EF only)
├── Persistence/
│   ├── StreamDistributionDbContext.cs                   ← EF Core; Wolverine outbox tables included
│   ├── Configurations/
│   │   └── StreamConfiguration.cs                       ← IEntityTypeConfiguration<Stream>; unique index on cameraIdentifier
│   ├── StreamRepository.cs                              ← IStreamRepository impl
│   ├── StreamQuerySource.cs                             ← IQueryable<Stream> read-side seam
│   ├── StreamDistributionMigrator.cs                    ← IMigrator implementation
│   └── DesignTimeDbContextFactory.cs                    ← for dotnet ef migrations CLI
├── Gateways/
│   ├── MediaMtxRtspGateway.cs                           ← IRtspGateway implementation; HTTP client + retry policy
│   └── MediaMtxOptions.cs                               ← bound from IConfiguration
├── HealthWatcher/
│   └── StreamHealthWatcher.cs                           ← BackgroundService; polls MediaMTX /paths every 2 s
└── Migrations/
    └── <timestamp>_InitialStreamDistribution.cs

src/StreamDistribution/Api/
├── StreamDistributionApiModule.cs                       ← AddStreamDistributionApi() + handler registrations
├── StreamEndpoints.cs                                   ← GET /streams/{id}, GET /streams?ids=..., POST /streams/{path}/authorize (called by MediaMTX)
├── Requests/                                            ← (none for v1; all reads + the MediaMTX-driven authorize body)
└── Program.cs                                           ← AddStreamDistributionInfrastructure + auth + endpoints

src/Shared.Contracts/                                    ← cross-context (no project ref between contexts)
└── StreamDistribution/
    └── StreamHealthChangedV1.cs                         ← new versioned integration event (ADR-0073)

src/MigrationRunner/
└── Program.cs                                           ← +builder.AddStreamDistributionPersistence();
                                                          (slim module, no Wolverine/RabbitMQ — same pattern as CameraCatalog)

src/AppHost/
├── AppHost.cs                                           ← add MediaMTX container + stream-distribution-db database +
│                                                         stream-distribution Api project + WithReference graph
└── Resources/                                           ← new
    └── mediamtx.yml                                     ← templated MediaMTX config (paths section is dynamic)

apps/shared/src/
├── api/
│   └── streams.api.ts                                   ← new RTK Query slice (getStream, listStreams)
├── streaming/
│   ├── WhepClient.ts                                    ← thin wrapper around RTCPeerConnection + WHEP fetch
│   └── index.ts
└── ui/composites/
    └── CameraViewer.tsx                                 ← shared composite: { cameraIdentifier } → renders <video>

apps/management-web/src/
├── features/cameras/
│   ├── CamerasPage.tsx                                  ← MODIFIED: add health-badge column + Watch button per row
│   ├── CameraViewerPanel.tsx                            ← NEW: dialog/panel that mounts CameraViewer
│   └── streamHealthBadge.tsx                            ← NEW: tiny component for the table column
└── app/store.ts                                         ← +streamsApi.reducer + .middleware

tests/StreamDistribution.Domain.Tests/                   ← new test project (ADR-0063 per-feature)
├── Stream/
│   ├── StreamTests.cs                                   ← state-machine transitions
│   ├── StreamIdentifierTests.cs
│   ├── StreamStateTests.cs
│   ├── TranscodeModeTests.cs
│   ├── MediaMtxPathTests.cs
│   └── Builders/
│       └── StreamBuilder.cs                             ← fluent builder per ADR-0054

tests/StreamDistribution.Application.Tests/              ← new test project
├── Commands/
│   ├── ProvisionStreamCommandHandlerTests.cs            ← idempotency + state transitions + repository writes
│   ├── ReportStreamHealthCommandHandlerTests.cs         ← Healthy↔Degraded↔Offline rules
│   └── AuthorizeWhepCommandHandlerTests.cs              ← bearer-token validation against fake Keycloak
├── Queries/
│   ├── GetStreamQueryHandlerTests.cs
│   └── ListStreamsQueryHandlerTests.cs
├── EventHandlers/
│   └── CameraRegisteredIntegrationEventHandlerTests.cs  ← idempotent on re-delivery
└── Fakes/
    ├── InMemoryStreamRepository.cs
    ├── InMemoryStreamQuerySource.cs                     ← reuses the TestAsyncEnumerable pattern from CameraCatalog
    ├── FakeRtspGateway.cs                               ← scripts MediaMTX HTTP responses
    └── FakeClock.cs                                     ← reused from Shared.Kernel.Tests (or duplicated locally)

tests/Integration.Tests/StreamDistribution/              ← new directory; same AspireFixture
├── ProvisionStreamIntegrationTests.cs                   ← end-to-end: register camera → MediaMTX path created
├── StreamHealthIntegrationTests.cs                      ← simulate RTSP outage → Degraded → Offline → recover
└── WhepAuthIntegrationTests.cs                          ← MediaMTX → /authorize callback → 200/401 paths

tests/Integration.Tests/Fixtures/
└── AspireFixture.cs                                     ← MODIFIED: wait for mediamtx + add CreateStreamDistributionDbContextAsync
                                                          + expose CreateRtspTestSource() utility

tests/Architecture.Tests/
└── BoundaryTests.cs                                     ← no source changes; existing rules cover StreamDistribution
                                                          (rules are assembly-level wildcards)
```

**Structure Decision:** Backend follows the per-aggregate Domain
folder layout (ADR-0092) and the per-message-kind Application layout
(ADR-0093). The "infrastructure split into `Persistence` and
`Infrastructure` modules" pattern is borrowed from spec 001
(`AddCameraCatalogPersistence` / `AddCameraCatalogInfrastructure`) —
MigrationRunner consumes only persistence, the API consumes the full
stack including Wolverine. Frontend follows the per-feature folder
under `apps/management-web/src/features/cameras/` so the **viewer** UI
lives next to the **list** UI it integrates with; the WHEP plumbing
lives in `apps/shared/src/streaming/` because spec 003 will reuse it
in kiosk-web.

## Backend Design

### Domain layer

```csharp
public sealed class Stream : AggregateRoot<StreamIdentifier>
{
    public CameraIdentifier Camera { get; private set; }           // value copy across contexts (Shared.Kernel type)
    public MediaMtxPath Path { get; private set; } = default!;
    public StreamState State { get; private set; } = default!;
    public TranscodeMode TranscodeMode { get; private set; } = default!;
    public Option<DateTimeOffset> LastSuccessAt { get; private set; }
    public Option<string> LastError { get; private set; }
    public DateTimeOffset ProvisionedAt { get; private set; }
    public OperatorIdentifier ProvisionedBy { get; private set; }

    private Stream() { }   // EF Core ctor

    public static Stream Provision(
        CameraIdentifier camera,
        OperatorIdentifier provisionedBy,
        IClock clock)
    {
        var path = MediaMtxPath.For(camera);
        var stream = new Stream
        {
            Id = StreamIdentifier.New(),
            Camera = camera,
            Path = path,
            State = StreamState.Provisioning,
            TranscodeMode = TranscodeMode.Unknown,
            ProvisionedAt = clock.UtcNow,
            ProvisionedBy = provisionedBy,
        };
        stream.Raise(new StreamProvisionedDomainEvent(stream.Id, camera, path, stream.ProvisionedAt, provisionedBy));
        return stream;
    }

    public void ReportHealthy(TranscodeMode mode, IClock clock)
    {
        var from = State;
        State = StreamState.Healthy;
        TranscodeMode = mode;
        LastSuccessAt = Option<DateTimeOffset>.Some(clock.UtcNow);
        LastError = Option<string>.None;
        if (from != StreamState.Healthy)
        {
            Raise(new StreamHealthChangedDomainEvent(Id, Camera, from, StreamState.Healthy, clock.UtcNow, Option<string>.None));
        }
    }

    public void ReportDegraded(string error, IClock clock) { /* state-machine guard + event */ }
    public void ReportOffline(string error, IClock clock)  { /* state-machine guard + event */ }
}
```

State-machine transitions enforced inside the aggregate:

| From | To | Allowed by |
|---|---|---|
| Provisioning | Healthy | first successful read |
| Provisioning | Degraded | first 10 s without a frame after path created |
| Healthy | Degraded | 10 s without a frame |
| Degraded | Healthy | 3 consecutive frames |
| Degraded | Offline | 5 min stuck in Degraded |
| Offline | Healthy | 3 consecutive frames |
| any | any | invalid → throws `InvalidOperationException("...")`; the handler catches and returns `Result.Failure` |

### Value objects

- `StreamIdentifier`: `readonly record struct StreamIdentifier(Guid Value) : IStronglyTypedId<Guid>`. `New()` returns `Guid.CreateVersion7()`.
- `StreamState`: enum-backed VO with the four values; static factory `From(string)` for EF Core conversion.
- `TranscodeMode`: enum-backed VO with three values.
- `MediaMtxPath`: `sealed record MediaMtxPath : StringValueObject`. Constructor accepts only strings matching `^cam-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$`. Factory `For(CameraIdentifier)` constructs from a camera.

### Application layer

`ProvisionStreamCommand` is a record implementing
`ICommand<Result<StreamIdentifier, ProvisionStreamError>>`. The
handler:

1. Calls `IStreamRepository.GetByCameraAsync(camera, ct)`. If a stream
   already exists → return success with its existing identifier
   (idempotency per FR-011).
2. Otherwise constructs `Stream.Provision(camera, operator, clock)` and
   adds to the repository.
3. Calls `IRtspGateway.AddPathAsync(MediaMtxPath, rtspSourceUrl, ct)` to
   register the path with MediaMTX. The RTSP source URL is included in
   the inbound `CameraRegisteredV1` event payload, so the handler
   doesn't need to call back into CameraCatalog.
4. Saves the unit of work — same EF transaction commits the aggregate,
   the raised domain event (consumed in-process by
   `StreamProvisionedDomainEventHandler`), and the Wolverine outbox row.
   No `StreamHealthChangedV1` published yet; the first one fires when
   the health watcher reports a transition.

`ReportStreamHealthCommandHandler` is invoked by the
`StreamHealthWatcher` background service on each 2-second poll. It
loads the Stream by `cameraIdentifier`, calls the appropriate
`ReportHealthy/Degraded/Offline` aggregate method, and saves. The
aggregate decides whether to raise `StreamHealthChangedDomainEvent`
(only on actual transitions); the event handler translates it to
`StreamHealthChangedV1` and publishes via the outbox.

`AuthorizeWhepCommandHandler` is invoked when MediaMTX's external auth
hook POSTs to `/streams/{path}/authorize` with the browser's bearer
token. The handler:

1. Validates the bearer JWT against the same Keycloak realm
   (delegated to `JwtBearerOptions` — the request goes through the
   same auth middleware so this is essentially a check that the
   principal carries the `sse.management` scope).
2. Optionally checks that the path exists in `StreamRepository` and
   isn't in `Offline` state (otherwise return 403 with a hint).
3. Returns the result; MediaMTX uses the HTTP status to allow or
   reject the WHEP handshake.

### Wolverine subscription wiring

`CameraRegisteredIntegrationEventHandler` is a Wolverine message
handler in `StreamDistribution.Application/EventHandlers/`. Wolverine
discovers it via the assembly scan registered in
`AddStreamDistributionInfrastructure`. The Wolverine routing prefix is
`stream-distribution.` per ADR-0088 (per-module queue isolation), so
the bound queue is
`stream-distribution.SmartSentinelEye.Shared.Contracts.CameraCatalog.CameraRegisteredV1`.

The handler simply maps the event to a `ProvisionStreamCommand` and
dispatches via `ICommandDispatcher` (the hand-rolled façade over
Wolverine per ADR-0042 + ADR-0057). Idempotency is enforced inside the
command handler (FR-011), so re-delivery is safe.

### Infrastructure layer

`StreamDistributionDbContext` defines `DbSet<Stream>` plus the
Wolverine outbox tables (schema `wolverine_stream_distribution`).
`StreamConfiguration : IEntityTypeConfiguration<Stream>` registers:

- Value-object conversions for `StreamIdentifier`, `CameraIdentifier`,
  `MediaMtxPath`, `StreamState`, `TranscodeMode`.
- `Option<T>` → nullable column conversions for `LastSuccessAt` and
  `LastError`.
- Unique index on `camera_id` so two Streams can't share a camera.
- `Version` column with EF Core concurrency token attribute
  (per ADR-0043).

`MediaMtxRtspGateway : IRtspGateway` uses a typed `HttpClient`
configured against the Aspire-injected `services:mediamtx:http:0`
endpoint. Methods:

```csharp
public interface IRtspGateway
{
    Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken ct);
    Task RemovePathAsync(MediaMtxPath path, CancellationToken ct);
    Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken ct);
}

public sealed record RtspPathHealth(
    bool IsReady,
    string? LastError,
    DateTimeOffset? LastFrameAt,
    TranscodeMode DetectedMode);
```

Retry policy: Polly-driven exponential backoff with the same schedule
as the spec's outage retries (1 s, 2 s, 5 s, 10 s, 30 s, cap). Each
retry is logged with the cameraIdentifier so the health watcher can
correlate.

`StreamHealthWatcher : BackgroundService` runs in the Api process.
Every 2 seconds it lists active streams from `StreamRepository`, calls
`GetPathHealthAsync` per stream, and dispatches
`ReportStreamHealthCommand` only when state would change. CPU cost for
250 streams: ~125 HTTP calls/second to MediaMTX over loopback — fine.

### Api layer

`StreamEndpoints` exposes three routes under `/streams` with
`RequireAuthorization("admin")` (per ADR-0023):

```csharp
group.MapGet("/{cameraIdentifier:guid}", GetOne)
     .Produces<StreamHealthDto>(StatusCodes.Status200OK)
     .Produces(StatusCodes.Status404NotFound);
group.MapGet("/", ListByCameras)        // ?cameraIdentifiers=guid1,guid2,...
     .Produces<IReadOnlyList<StreamHealthDto>>(StatusCodes.Status200OK)
     .ProducesValidationProblem(StatusCodes.Status400BadRequest);
group.MapPost("/{path}/authorize", AuthorizeWhep) // called by MediaMTX
     .AllowAnonymous()                            // MediaMTX hits this from the local network
     .Produces(StatusCodes.Status200OK)
     .Produces(StatusCodes.Status401Unauthorized);
```

The `authorize` endpoint is `AllowAnonymous` at the routing layer
because MediaMTX's external auth hook can't carry a JWT itself — but
the bearer it forwards is validated in the handler. This is the one
endpoint where authorization is performed inside the handler rather
than via the policy. The endpoint is bound to localhost-only via
MediaMTX's config (the `externalAuthenticationURL` points at
`http://127.0.0.1:{streamDistPort}/streams/{path}/authorize`).

### Configuration: MediaMTX

A templated `mediamtx.yml` config ships in `src/AppHost/Resources/`.
Aspire mounts it into the container at `/mediamtx.yml`. Key settings:

```yaml
authMethod: http
externalAuthenticationURL: http://stream-distribution:8080/streams/{path}/authorize
externalAuthenticationProtocols: [whep]

webrtc: yes
webrtcAddress: :8889
rtspAddress: :8554
api: yes
apiAddress: :9997

paths:
  # paths are added dynamically via API; static block intentionally empty
```

StreamDistribution.Api does NOT seed paths on startup; it relies on
`CameraRegisteredV1` redelivery (Wolverine outbox replay) to provision
each camera's path. MediaMTX is the runtime source of truth for live
paths; StreamDistribution.Api is the durable source of truth that
reconciles after restarts.

## Frontend Design

### RTK Query slice (`apps/shared/src/api/streams.api.ts`)

```typescript
export const streamsApi = createApi({
  reducerPath: 'streamsApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/streams', prepareHeaders: attachKeycloakToken }),
  tagTypes: ['Stream'],
  endpoints: (b) => ({
    getStream: b.query<StreamHealth, string>({
      query: (cameraIdentifier) => `/${cameraIdentifier}`,
      providesTags: (_r, _e, cid) => [{ type: 'Stream', id: cid }],
    }),
    listStreams: b.query<StreamHealth[], string[]>({
      query: (ids) => ({ url: '', params: { cameraIdentifiers: ids.join(',') } }),
      providesTags: (r) => r?.map((s) => ({ type: 'Stream', id: s.cameraIdentifier })) ?? [],
    }),
  }),
});
```

The cameras list polls `useListStreamsQuery(ids, { pollingInterval: 5000 })`. v2 (replaceable real-time transport per ADR-0076) will replace the poll with a push subscription; no API shape change.

### WHEP client (`apps/shared/src/streaming/WhepClient.ts`)

Thin wrapper, no third-party WebRTC library. Approximate shape:

```typescript
export class WhepClient {
  constructor(private opts: { whepUrl: string; getToken: () => Promise<string> }) {}

  async connect(videoEl: HTMLVideoElement, signal: AbortSignal): Promise<void> {
    const pc = new RTCPeerConnection({ iceServers: [] }); // host candidates only (on-prem)
    pc.addTransceiver('video', { direction: 'recvonly' });
    pc.ontrack = (e) => { videoEl.srcObject = e.streams[0]; };

    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);

    const token = await this.opts.getToken();
    const res = await fetch(this.opts.whepUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/sdp', Authorization: `Bearer ${token}` },
      body: offer.sdp,
      signal,
    });
    if (!res.ok) throw new WhepError(res.status, await res.text());
    const answerSdp = await res.text();
    await pc.setRemoteDescription({ type: 'answer', sdp: answerSdp });
    // ...store pc on the instance for cleanup
  }

  close(): void { /* stop tracks + close pc */ }
}
```

Errors are mapped to a small `WhepError` union so the UI can decide
between "show banner" and "give up".

### `CameraViewer` composite (`apps/shared/src/ui/composites/CameraViewer.tsx`)

```tsx
export interface CameraViewerProps {
  cameraIdentifier: string;
}

export function CameraViewer({ cameraIdentifier }: CameraViewerProps) {
  const { data: stream, isLoading } = useGetStreamQuery(cameraIdentifier, { pollingInterval: 5000 });
  const videoRef = useRef<HTMLVideoElement>(null);
  const [status, setStatus] = useState<'idle' | 'connecting' | 'live' | 'reconnecting' | 'error'>('idle');

  useEffect(() => {
    if (!stream?.whepUrl || !videoRef.current) return;
    const ctrl = new AbortController();
    const client = new WhepClient({ whepUrl: stream.whepUrl, getToken });
    setStatus('connecting');
    client.connect(videoRef.current, ctrl.signal)
      .then(() => setStatus('live'))
      .catch(() => setStatus('error'));
    return () => { ctrl.abort(); client.close(); };
  }, [stream?.whepUrl]);

  // Health-driven banner
  useEffect(() => {
    if (!stream) return;
    if (stream.state === 'Degraded') setStatus('reconnecting');
    if (stream.state === 'Healthy' && status === 'reconnecting') setStatus('live');
  }, [stream?.state]);

  return (
    <div className="relative aspect-video bg-black">
      <video ref={videoRef} autoPlay playsInline muted className="w-full h-full" />
      {status !== 'live' && <ViewerOverlay status={status} />}
    </div>
  );
}
```

The composite owns nothing the future LayoutComposition cell will need
to override — it accepts a `cameraIdentifier` and renders the stream.
Spec 003 will mount one per cell.

### Cameras list integration

`CamerasPage.tsx` (modified):

- Add a `Stream` column to the existing DataTable. Cell renders
  `<StreamHealthBadge cameraIdentifier={row.cameraIdentifier} />`.
- Existing row click expands a side panel (`CameraViewerPanel`) that
  mounts `<CameraViewer />` for that camera.
- `StreamHealthBadge` consumes the page-level `useListStreamsQuery`
  with the visible camera IDs.

## Tests

### Domain unit tests (StreamDistribution.Domain.Tests)

- `StreamTests`:
  - `Provision_with_a_camera_creates_a_provisioning_stream_and_raises_the_provisioned_event`
  - `Two_streams_for_the_same_camera_cannot_be_provisioned_but_idempotency_is_handler_concern` (asserts no aggregate-level guard; uniqueness lives in the DB index + handler)
  - `Report_healthy_from_provisioning_transitions_state_and_raises_HealthChanged`
  - `Report_healthy_when_already_healthy_does_not_raise_an_event`
  - `Report_degraded_from_healthy_raises_HealthChanged_with_the_error`
  - `Report_degraded_when_already_degraded_updates_LastError_but_does_not_raise_an_event`
  - `Report_offline_from_degraded_raises_HealthChanged`
  - `Report_offline_directly_from_healthy_throws_InvalidOperationException` (must pass through Degraded)
- `StreamStateTests`, `TranscodeModeTests`, `MediaMtxPathTests`, `StreamIdentifierTests`: value-object guard rails.

### Application handler tests

- `ProvisionStreamCommandHandlerTests`:
  - `Provision_for_a_new_camera_creates_the_stream_and_registers_the_path_with_MediaMTX`
  - `Provision_for_an_existing_camera_returns_the_existing_identifier_and_does_not_re_register` (idempotency)
  - `Provision_when_MediaMTX_is_unreachable_returns_RtspGatewayUnavailable_and_does_not_save`
- `ReportStreamHealthCommandHandlerTests`:
  - `Report_healthy_after_provisioning_transitions_the_stream_and_publishes_StreamHealthChangedV1`
  - `Report_degraded_after_healthy_publishes_with_correct_from_to`
  - `Report_offline_directly_from_healthy_does_not_skip_Degraded` (handler enforces, not aggregate)
  - `Report_healthy_when_already_healthy_does_not_publish_an_event`
- `AuthorizeWhepCommandHandlerTests`:
  - `Authorize_with_a_valid_admin_token_returns_200`
  - `Authorize_with_a_token_missing_sse_management_returns_403`
  - `Authorize_for_an_Offline_stream_returns_403_with_StreamUnavailable`
- `CameraRegisteredIntegrationEventHandlerTests`:
  - `On_first_receipt_dispatches_ProvisionStreamCommand`
  - `On_redelivery_is_idempotent_because_the_handler_is`
- `ListStreamsQueryHandlerTests`:
  - `List_by_camera_identifiers_returns_one_dto_per_id_preserving_order`
  - `List_by_zero_identifiers_returns_an_empty_array`
  - `List_by_more_than_200_identifiers_returns_InvalidBatchSize`

Hand-written `InMemoryStreamRepository` + `InMemoryStreamQuerySource` (TestAsyncEnumerable pattern reused from CameraCatalog) + `FakeRtspGateway` (in-memory scripted responses) + `FakeClock`.

### Integration tests (extending AspireFixture)

`AspireFixture` modifications:
- Wait for `mediamtx` resource to reach `Running`.
- Wait for the path-API health endpoint
  (`GET /v3/paths/list` on MediaMTX) to return 200.
- Add `CreateStreamDistributionDbContextAsync()` helper.
- Add `StartRtspTestSourceAsync()` — boots a side-car
  `bluenviron/mediamtx:latest` instance configured as a *publisher*
  (different paths) so tests have a real RTSP source URL to point at.

Test classes:
- `ProvisionStreamIntegrationTests`:
  - `Register_a_camera_provisions_a_stream_within_30_seconds_and_marks_it_Healthy`
  - `Provisioning_is_idempotent_on_event_redelivery_via_Wolverine_replay`
- `StreamHealthIntegrationTests`:
  - `Stopping_the_RTSP_source_transitions_the_stream_to_Degraded_within_15_seconds`
  - `Restarting_the_RTSP_source_transitions_the_stream_back_to_Healthy_within_15_seconds`
  - `Sustained_5_minute_outage_transitions_to_Offline` *(optional; gated behind a `[Trait("category","slow")]` so it doesn't run on every PR)*
- `WhepAuthIntegrationTests`:
  - `WHEP_with_a_valid_admin_token_succeeds_and_negotiates_an_SDP_answer`
  - `WHEP_without_a_token_returns_401`
  - `WHEP_with_a_token_missing_sse_management_returns_403`

A "click-to-first-frame" integration test using a headless browser is
deferred — covered by the WHEP-handshake-completes-within-Ns assertion
above (the bulk of the latency budget is in handshake + decode, both
testable without a browser).

### Architecture tests

No changes. Existing rules in `tests/Architecture.Tests/BoundaryTests.cs`
cover the new types automatically:
- Domain has no infrastructure references → covers
  `StreamDistribution.Domain`.
- No project references between bounded contexts → covers
  StreamDistribution ↔ CameraCatalog.

### Frontend tests

- `apps/shared/src/streaming/WhepClient.test.ts`:
  - `Connect_posts_an_SDP_offer_with_the_bearer_token`
  - `Connect_handles_a_401_response_by_throwing_WhepError_with_code_Unauthorized`
- `apps/management-web/src/features/cameras/CameraViewerPanel.test.tsx`:
  - `Renders_the_viewer_when_a_camera_is_selected_and_unmounts_on_close`
- `apps/management-web/src/features/cameras/streamHealthBadge.test.tsx`:
  - `Renders_Healthy_state_with_the_green_pill_and_shows_lastSuccessAt_in_the_tooltip`
  - `Renders_Degraded_state_with_the_yellow_pill_and_shows_the_error_in_the_tooltip`

Existing `App.test.tsx` smoke test is updated to also mock
`useListStreamsQuery` so the cameras page renders without a backend.

### Coverage gates (ADR-0065)

- `StreamDistribution.Domain` ≥ 90 % — aggregate + 4 value objects;
  achievable.
- `StreamDistribution.Application` ≥ 80 % — five command handlers + two
  query handlers, all small and pure.
- `Shared.Contracts.StreamDistribution` ≥ 90 % — one new record;
  satisfied by integration test reference + the existing
  `Shared.Contracts.Tests` project (extended).

## Migrations

```pwsh
dotnet ef migrations add InitialStreamDistribution `
  --project src/StreamDistribution/Infrastructure `
  --startup-project src/MigrationRunner `
  --output-dir Persistence/Migrations
```

Migration creates the `streams` table, `wolverine_stream_distribution.*`
outbox tables, and the unique index on `camera_id`.

## Latency budget allocation

Spec FRs 012, 013, and SCs 003 / 004 set the per-leg budgets:

| Path | Budget | Leg breakdown |
|---|---|---|
| `GET /streams/{id}` | 100 ms p95 | 5 ms auth + 5 ms model bind + 30 ms Postgres lookup + 10 ms serialize + 50 ms headroom |
| `GET /streams?ids=...` (200 cam batch) | 200 ms p95 | 5 ms auth + 5 ms parse + 100 ms one Postgres `WHERE camera_id IN (...)` + 20 ms serialize + 70 ms headroom |
| Click → first frame (US1) | 3 s p95 | ≤ 200 ms `/streams/{id}` lookup + ≤ 500 ms WHEP POST + ≤ 1 s ICE/DTLS handshake + ≤ 1 s decoder warmup + 300 ms headroom |
| Outage → `Degraded` badge | 10 s | health watcher polls every 2 s + MediaMTX declares no-frame after 5 s + RabbitMQ delivery ≤ 1 s + 5 s UI poll worst-case |
| Recovery → `Healthy` badge | 10 s | symmetric to above |

CI emits the measured click-to-first-frame from the integration test;
if it drifts above 3 s p95, the PR is blocked.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| **MediaMTX path-config drift between Postgres source of truth and live runtime state** | StreamDistribution.Api reconciles on startup: list all streams in DB, ensure each is registered with MediaMTX, drop any orphan path. Single reconciliation pass; documented in the API project README. |
| **First-feature use of MediaMTX — unknown unknowns in the integration test** | Spike during early Phase 4. If MediaMTX-from-Aspire turns out to be flaky, fall back to a long-running MediaMTX container that the test asserts against (cold start budget ↑ but stability ↑). |
| **Software transcode (FFmpeg) saturates the SFU host CPU at 250 cameras** | Out of scope for v1 — the spec assumes most cameras are H.264 (passthrough). Document the FFmpeg path as "best-effort"; production deployments should plan for GPU transcode in a follow-up spec. |
| **WHEP handshake fails behind some browser configurations (e.g., Firefox's stricter ICE rules)** | Integration test runs only Chrome via Playwright (deferred to Phase 4 if scope creep allows). Manual smoke test on Firefox added to the PR checklist. |
| **External auth hook adds a synchronous HTTP roundtrip to every WHEP open** | MediaMTX's hook is in-process for path-list operations; the per-WHEP callback is one HTTP call on the same host. ~5 ms overhead. Documented in the click-to-first-frame budget. |
| **Wolverine consumer in StreamDistribution can't subscribe to CameraCatalog's exchange without name conflicts** | Per-module queue prefix (ADR-0088) gives StreamDistribution its own queue bound to the same routing key. Verified in the existing camera-catalog→camera-catalog handler chain in spec 001. |
| **Existing `AspireFixture` extension breaks spec 001's tests** | The fixture is additive (new wait + new helper); existing spec 001 tests don't touch the new code paths. Re-run all 8 integration tests as part of this PR's CI. |

## Out of scope (deferred to follow-up specs)

- **PTZ control** (US3 of this spec, deferred). Needs ONVIF auth, a
  dedicated control-channel design, and probably its own `PtzCommand`
  aggregate in StreamDistribution.
- **Recording / replay** (US4). Needs a retention policy decision,
  storage backend (MinIO per ADR-0009), and a replay UI.
- **GPU transcode** for non-H.264 cameras at scale (NVENC / QSV).
- **Multi-fab clustering of MediaMTX** for fabs exceeding single-host
  capacity (~250 H.264 1080p30 streams).
- **PTP frame-sync timestamps** in the WebRTC pipeline (ADR-0014).
  Not needed for single-cell viewing; needed for the multi-cell video
  wall.
- **Kiosk-web stream view** — kiosk-web app is scaffolded but its
  layout/overlay specs are separate.
- **Audit & Observability subscription** to `StreamHealthChangedV1` —
  the integration event will be available, but the audit-log handler
  ships in a future Audit context spec.
