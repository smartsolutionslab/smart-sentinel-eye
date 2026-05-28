# Implementation Plan: 006 — EventIngestion

**Branch:** `006-event-ingestion` | **Date:** 2026-05-28 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from `specs/006-event-ingestion/spec.md`
(Phase 1, ten Q&A clarifications resolved, zero `[NEEDS CLARIFICATION]`
markers). Phase-1 gate approved 2026-05-28.

## Summary

Lights up the first end-to-end slice through the **EventIngestion**
bounded context — the upstream half of the camera → event → overlay
loop. Four source types (PLC, inference, manual, webhook) land in a
single canonical `events` table; each one fans out as
`FabEventIngestedV1` on Wolverine's outbox. EventIngestion is
deliberately *boring* — it knows nothing about variables, automation
rules, or overlays. It accepts, persists, and fans out. Spec 008
(Automation) will consume `FabEventIngestedV1` and (eventually) issue
`SetVariableValueCommand` against spec 005's SystemVariables.

- **New infra component — Mosquitto MQTT broker.** Aspire resource
  in dev, Helm chart in prod. Per-fab single-instance (no
  clustering in v1). Per-device passwords + ACL file authenticate
  PLC + inference publishers. **ADR-0095 (new)** records the
  choice and rejected alternatives (RabbitMQ MQTT plugin, EMQX).
- **Backend (EventIngestion):** new `Event` aggregate (CRUD per
  ADR-0009; no event sourcing). Envelope value objects
  (`EventIdentifier`, `Source`, `DeviceIdentifier`, `Kind`,
  `OccurredAt`, `IngestedAt`, `Payload`). Three commands (HTTP
  `IngestManualEvent`, HTTP `IngestWebhookEvent`, internal
  `IngestMqttEvent`), three queries (`GetEvent`, `ListEvents`,
  `ListDeadLetters`). New context DB `event-ingestion-db`.
  Standard Wolverine outbox + per-module queue isolation.
- **Bounded-channel backpressure:** a singleton
  `BoundedChannel<EventEnvelope>` (5 000 slots) sits between all
  three ingress paths and the persistence loop. Full channel ⇒
  HTTP 429 + MQTT subscriber stops ACKing. Prometheus counters
  (`ingest_channel_depth`, `ingest_429_total`,
  `ingest_channel_full_seconds`) exposed via the standard OTEL
  exporter (ADR-0026).
- **MQTT subscriber:** `IHostedService` per fab, subscribes
  wildcard `fab/{fabId}/+/+` at QoS 1, parses payloads into the
  envelope, drops on the bounded channel, lets the persistence
  loop ACK upstream. Subscriber resumes from the broker on
  process restart (persistent session, MQTT clean=false).
- **HTTP endpoints:** `POST /events/manual` (OIDC bearer with
  `operator` policy), `POST /events/webhook/{integrationName}`
  (static bearer per integration). Plus `POST
  /webhook-integrations` (admin) for registering integrations.
- **Persistence:** EF Core on Postgres. `events` table is
  list-partitioned by `fab_id` and range-partitioned (monthly) on
  `ingested_at`. Partitions created lazily on first insert per
  month. Unique constraint `(fab_id, event_id)` enforces hybrid
  idempotency. Separate `dead_letters` table for malformed MQTT
  messages.
- **Fan-out:** `FabEventIngestedV1` published via Wolverine's
  Postgres outbox after the persistence transaction commits.
  Wolverine routes by `deviceId` hash to preserve per-source
  FIFO on consumer side (per ADR-0088 queue-isolation).
- **No frontend changes in v1.** The management UI for browsing
  events / dead-letters / webhook integrations is **spec 006a**
  (see Out of Scope below).

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Persistence | EF Core on Postgres (per-context DB `event-ingestion-db`), table-level list+range partitioning | ADR-0009, ADR-0071 |
| Messaging | RabbitMQ via Wolverine; per-module queue isolation; Postgres outbox | ADR-0042, ADR-0088 |
| MQTT broker | **Mosquitto** (new); per-fab single instance | **ADR-0095 (this plan)** |
| MQTT client lib | **MQTTnet** (most-used .NET MQTT lib, MIT) | **ADR-0096 (this plan)** |
| Identity | Keycloak per fab; `operator` policy on `/events/manual`; static bearer per webhook integration; admin on registration + dead-letter read | ADR-0007 + spec FR-026 |
| API style | Minimal APIs only | ADR-0070 |
| Errors | `Result<T, ApiError>` with sealed-record error hierarchies | ADR-0047, ADR-0089 |
| Backpressure | `System.Threading.Channels.Channel<T>` bounded (5 000), `FullMode.Wait` for MQTT (the subscriber blocks → broker doesn't get ACKs), 429 short-circuit for HTTP | spec FR-021/022 |
| Tests | xUnit + Shouldly + Moq + Testcontainers (Postgres + Mosquitto containers) via `AspireFixture` | ADR-0052, ADR-0068 |
| Observability | OpenTelemetry → Aspire dashboard + Grafana stack; key spans: `ingest.parse`, `ingest.persist`, `ingest.outbox` | ADR-0026 |
| Performance | Event arrival → durable outbox ≤ 50 ms p95 (NFR-001). 1 000 ev/s sustained. ≤ 30 s burst at 5× absorbed without loss. | spec NFR-001/002/003 |

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| §I On-prem first | EventIngestion runs on the same fab host. Mosquitto runs per fab. No cloud calls. | ✅ |
| §II DDD + VOs | Envelope columns become value objects: `EventIdentifier` (Guid v7), `Source` (closed enum-backed VO), `DeviceIdentifier` (string), `Kind` (string), `OccurredAt`/`IngestedAt` (`DateTimeOffset`), `Payload` (JsonDocument-backed VO with `≤ 64 KB` invariant). Maximalist hand-written. | ✅ |
| §III Bounded-context isolation | All new code in `SmartSentinelEye.EventIngestion.*`. The only outward contract is `Shared.Contracts/EventIngestion/FabEventIngestedV1`. **No new `AllowedCrossContext` entries.** | ✅ |
| §IV Latency budget | 50 ms p95 ingest→outbox is a strict subset of the 200 ms `event → overlay state` leg of the 800 ms NFR. Plan benchmarks the budget under §Performance Validation. | ✅ |
| §V Spec-driven | Spec gate approved 2026-05-28. This plan. Tasks follow via `/speckit-tasks`. | ✅ |
| §VI Aspire composition root | New AppHost resources: `event-ingestion` (.NET Aspire project), `event-ingestion-db` (`postgres.AddDatabase`), `mosquitto` (new resource — see ADR-0095). Connection strings flow via Aspire references, not hand-wired. | ✅ |
| §VII No event sourcing without justification | Events are CRUD inserts in a partitioned table. The audit/query story is a partitioned table + cursor pagination — no event-store, no projections. | ✅ |
| §VIII Safe at trust boundaries | Auth at every ingress: MQTT broker-level password+ACL; HTTP `operator` policy for manual; static bearer + constant-time compare for webhook; admin for registration + dead-letter read. Envelope VOs validate at construction. 64 KB payload cap at HTTP middleware + MQTT subscriber. | ✅ |
| §IX Forward-compatible interfaces | `Source` is a closed VO over `{ plc, inference, manual, webhook }` but the enum is expressible (`SourceCode` string) so spec 007's archive worker and spec 008's Automation can match on source without compiling against `EventIngestion.Domain`. `FabEventIngestedV1` is V1 — additive evolution only (per ADR-0040/0073). | ✅ |

**Result:** No violations. No Complexity Tracking entries.

**Tech-stack additions requiring ADR before Phase 4:**
- **ADR-0095** — Mosquitto as the canonical MQTT broker.
- **ADR-0096** — MQTTnet as the .NET MQTT client library.

Both ADRs are drafted as part of Phase 4 PR-A (infra + scaffolding).
Constitution §II permits tech-stack additions with ADR justification.

## Project Structure

### Documentation

```
specs/006-event-ingestion/
├── spec.md          ← Phase 1 (approved 2026-05-28)
├── plan.md          ← this file (Phase 2)
└── tasks.md         ← Phase 3 (next; created by /speckit-tasks)
```

### Source code — files added / modified

```
src/EventIngestion/Domain/                                ← scaffold exists; populated here
└── Event/
    ├── Event.cs                                          ← aggregate root (single-write, no state machine)
    ├── EventIdentifier.cs                                ← Guid v7 strongly-typed id
    ├── Source.cs                                         ← enum-backed VO (plc|inference|manual|webhook)
    ├── DeviceIdentifier.cs                               ← StringValueObject (≤ 64 chars, no whitespace)
    ├── Kind.cs                                           ← StringValueObject (≤ 128 chars, PascalCase grammar)
    ├── OccurredAt.cs                                     ← DateTimeOffset VO (UTC, monotonic future ≤ +5min skew)
    ├── IngestedAt.cs                                     ← DateTimeOffset VO (server-minted)
    ├── Payload.cs                                        ← JsonDocument VO (≤ 64 KB)
    ├── FabIdentifier.cs                                  ← StringValueObject (already exists in Shared.Kernel? check; add if missing)
    ├── IEventRepository.cs
    └── Events/
        └── EventIngestedDomainEvent.cs

src/EventIngestion/Domain/                                
└── DeadLetter/
    ├── DeadLetter.cs                                     ← rejected MQTT message record (envelope + raw bytes + error)
    ├── DeadLetterIdentifier.cs
    └── IDeadLetterRepository.cs

src/EventIngestion/Domain/
└── WebhookIntegration/
    ├── WebhookIntegration.cs                             ← aggregate (Name, DefaultKind, BearerTokenHash)
    ├── WebhookIntegrationName.cs                         ← StringValueObject (^[a-z][a-z0-9-]{0,62}$)
    ├── BearerTokenHash.cs                                ← SHA-256 hash VO (we never store the plaintext)
    ├── IWebhookIntegrationRepository.cs
    └── Events/
        ├── WebhookIntegrationRegisteredDomainEvent.cs
        └── WebhookIntegrationRevokedDomainEvent.cs

src/EventIngestion/Application/
├── Commands/
│   ├── IngestManualEventCommand.cs
│   ├── IngestManualEventErrors.cs
│   ├── IngestWebhookEventCommand.cs
│   ├── IngestWebhookEventErrors.cs
│   ├── IngestMqttEventCommand.cs                          ← internal, not exposed on HTTP
│   ├── IngestMqttEventErrors.cs
│   ├── RegisterWebhookIntegrationCommand.cs
│   ├── RegisterWebhookIntegrationErrors.cs
│   ├── RevokeWebhookIntegrationCommand.cs
│   ├── RevokeWebhookIntegrationErrors.cs
│   └── Handlers/
│       ├── IngestManualEventCommandHandler.cs
│       ├── IngestWebhookEventCommandHandler.cs
│       ├── IngestMqttEventCommandHandler.cs
│       ├── RegisterWebhookIntegrationCommandHandler.cs
│       └── RevokeWebhookIntegrationCommandHandler.cs
├── Queries/
│   ├── GetEventQuery.cs
│   ├── GetEventErrors.cs
│   ├── ListEventsQuery.cs                                  ← cursor pagination params
│   ├── ListEventsErrors.cs
│   ├── ListDeadLettersQuery.cs
│   ├── ListDeadLettersErrors.cs
│   ├── ListWebhookIntegrationsQuery.cs
│   ├── IEventQuerySource.cs
│   ├── IDeadLetterQuerySource.cs
│   ├── IWebhookIntegrationQuerySource.cs
│   └── Handlers/
│       ├── GetEventQueryHandler.cs
│       ├── ListEventsQueryHandler.cs
│       ├── ListDeadLettersQueryHandler.cs
│       └── ListWebhookIntegrationsQueryHandler.cs
├── DTOs/
│   ├── EventDto.cs
│   ├── EventPageDto.cs                                     ← items + nextCursor
│   ├── DeadLetterDto.cs
│   └── WebhookIntegrationDto.cs
├── Ingress/
│   ├── EventEnvelope.cs                                    ← internal DTO for the bounded channel
│   ├── IIngestChannel.cs                                   ← BoundedChannel facade
│   └── ChannelMetrics.cs                                   ← Prometheus counters
└── EventHandlers/                                          ← (deliberately empty in v1; spec 008 owns FabEventIngestedV1 consumers)

src/EventIngestion/Infrastructure/
├── Persistence/
│   ├── EventIngestionDbContext.cs                          ← DbSet<Event>, DbSet<DeadLetter>, DbSet<WebhookIntegration>
│   ├── EventConfiguration.cs                               ← partitioning DDL via raw SQL migration helper
│   ├── DeadLetterConfiguration.cs
│   ├── WebhookIntegrationConfiguration.cs
│   ├── EventRepository.cs
│   ├── DeadLetterRepository.cs
│   ├── WebhookIntegrationRepository.cs
│   ├── EventQuerySource.cs
│   ├── DeadLetterQuerySource.cs
│   ├── WebhookIntegrationQuerySource.cs
│   └── Migrations/
│       └── 20260528_InitialEventIngestionSchema.cs
├── Ingress/
│   ├── BoundedIngestChannel.cs                             ← singleton wrapper around Channel<T>
│   ├── PersistenceLoopHostedService.cs                     ← reads channel, persists, lets caller ACK
│   ├── MqttSubscriberHostedService.cs                      ← MQTTnet client, wildcard sub, push-to-channel
│   └── MosquittoConnectionFactory.cs                       ← TLS + credentials wiring
└── EventIngestionInfrastructureModule.cs                   ← AddEventIngestion{Infrastructure,Api}() per ADR-0051

src/EventIngestion/Api/
├── EventsEndpoints.cs                                      ← POST /events/manual, POST /events/webhook/{name}, GET /events, GET /events/{id}, GET /events/dead-letters
├── WebhookIntegrationsEndpoints.cs                         ← POST/GET/DELETE /webhook-integrations
└── EventIngestionApiModule.cs

src/AppHost/
├── AppHost.cs                                              ← adds mosquitto resource + event-ingestion service ref
└── mosquitto/                                              ← dev mosquitto config files
    ├── mosquitto.conf
    ├── passwords.txt                                       ← dev-only seed; prod creates via Helm secret
    └── acl.txt

src/Shared.Contracts/
└── EventIngestion/
    └── FabEventIngestedV1.cs                               ← record carrying full envelope + payload (JSON string up to 64 KB)

tests/EventIngestion.Domain.Tests/                          ← new test project
├── EventTests.cs
├── PayloadTests.cs
├── SourceTests.cs
├── DeviceIdentifierTests.cs
├── KindTests.cs
├── OccurredAtTests.cs
├── WebhookIntegrationTests.cs
├── BearerTokenHashTests.cs
└── *Builder.cs                                             ← hand-written fluent builders (ADR-0054)

tests/EventIngestion.Application.Tests/                     ← new test project
├── Commands/
│   ├── IngestManualEventCommandHandlerTests.cs
│   ├── IngestWebhookEventCommandHandlerTests.cs
│   ├── IngestMqttEventCommandHandlerTests.cs
│   ├── RegisterWebhookIntegrationCommandHandlerTests.cs
│   └── RevokeWebhookIntegrationCommandHandlerTests.cs
├── Queries/
│   ├── GetEventQueryHandlerTests.cs
│   ├── ListEventsQueryHandlerTests.cs                       ← exercises cursor pagination
│   ├── ListDeadLettersQueryHandlerTests.cs
│   └── ListWebhookIntegrationsQueryHandlerTests.cs
├── Ingress/
│   └── BoundedIngestChannelTests.cs                         ← bounded behaviour, drain order, full-channel signalling
└── Fakes/
    ├── InMemoryEventRepository.cs
    ├── InMemoryDeadLetterRepository.cs
    ├── InMemoryWebhookIntegrationRepository.cs
    └── FakeIngestChannel.cs

tests/EventIngestion.Integration.Tests/                     ← new project; Postgres + Mosquitto Testcontainers
├── MqttIngressTests.cs                                     ← publish → persist → outbox round-trip
├── HttpManualIngressTests.cs
├── HttpWebhookIngressTests.cs
├── PartitioningTests.cs                                    ← lazy partition creation on month boundary
├── BackpressureTests.cs                                    ← burst → 429 → drain
└── NFR001_LatencyTests.cs                                  ← warm 20 iters, assert p95 ≤ 50 ms

tests/Shared.Contracts.Tests/
├── FabEventIngestedV1Tests.cs

tests/Architecture.Tests/
└── BoundaryTests.cs                                        ← extends with EventIngestion.Domain framework-free check (no new allow-rules)

docs/adr/
├── 0095-mosquitto-mqtt-broker.md                           ← new
└── 0096-mqttnet-client-library.md                          ← new
```

## Domain Model

### Event (aggregate root)

Single-write aggregate. No state machine: an event is born ingested
and immutable. The only invariants are at construction; afterwards
it's a read-only record.

```csharp
public sealed class Event : AggregateRoot<EventIdentifier>
{
    public FabIdentifier Fab { get; private set; }
    public Source Source { get; private set; }
    public DeviceIdentifier Device { get; private set; }
    public Kind Kind { get; private set; }
    public OccurredAt OccurredAt { get; private set; }
    public IngestedAt IngestedAt { get; private set; }
    public Payload Payload { get; private set; }

    public static Event Ingest(
        EventIdentifier identifier,
        FabIdentifier fab, Source source, DeviceIdentifier device,
        Kind kind, OccurredAt occurredAt, Payload payload,
        IClock clock)
    {
        Ensure.That(occurredAt.Value <= clock.UtcNow.AddMinutes(5),
            "occurredAt cannot be more than 5 minutes in the future.");
        Event @event = new()
        {
            Id = identifier,
            Fab = fab, Source = source, Device = device, Kind = kind,
            OccurredAt = occurredAt, IngestedAt = IngestedAt.From(clock.UtcNow),
            Payload = payload,
        };
        @event.Raise(new EventIngestedDomainEvent(/* … */));
        return @event;
    }
}
```

`EventIngestedDomainEvent` → emitted from the aggregate → caught by
an `EventHandlers/EventIngestedDomainEventHandler` that publishes
`FabEventIngestedV1` via `IEventBus` (Wolverine outbox-backed).

### Source (discriminated VO)

Closed set per spec FR-001:

```csharp
public sealed record Source : IValueObject<string>
{
    public static readonly Source Plc       = new("plc");
    public static readonly Source Inference = new("inference");
    public static readonly Source Manual    = new("manual");
    public static readonly Source Webhook   = new("webhook");

    public string Value { get; }
    private Source(string value) => Value = value;
    public static Source From(string raw) => raw switch
    {
        "plc" => Plc, "inference" => Inference,
        "manual" => Manual, "webhook" => Webhook,
        _ => throw new ArgumentException($"Unknown event source '{raw}'."),
    };
}
```

### Payload (constraints VO)

```csharp
public sealed record Payload : IValueObject<string>
{
    public const int MaxBytes = 64 * 1024;
    public string Value { get; }                              // canonicalised JSON
    public static Payload From(JsonDocument doc)              // checks ≤ 64 KB serialised
    public static Payload From(string raw)                    // parses + validates JSON
}
```

### WebhookIntegration (aggregate root)

```csharp
public sealed class WebhookIntegration : AggregateRoot<WebhookIntegrationName>
{
    public Kind DefaultKind { get; private set; }
    public BearerTokenHash TokenHash { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static (WebhookIntegration, string plainToken) Register(...);  // returns plaintext ONCE
    public void Revoke(IClock clock);
}
```

The plaintext token is returned **only** from the `Register` factory;
storage is SHA-256 hashed. Constant-time compare on auth.

### DeadLetter (aggregate root)

```csharp
public sealed class DeadLetter : AggregateRoot<DeadLetterIdentifier>
{
    public FabIdentifier Fab { get; private set; }
    public string Topic { get; private set; }                 // mqtt topic if applicable
    public string RawPayload { get; private set; }            // up to 64 KB
    public string Error { get; private set; }
    public DateTimeOffset RejectedAt { get; private set; }
}
```

Audit-only. No fan-out.

## Persistence — partitioning DDL

The `events` table is **partitioned** at the database level. EF Core
manages the partitions only loosely; the initial migration runs raw
SQL:

```sql
CREATE TABLE events (
    fab_id          TEXT        NOT NULL,
    event_id        UUID        NOT NULL,
    source          TEXT        NOT NULL,
    device_id       TEXT        NOT NULL,
    kind            TEXT        NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL,
    ingested_at     TIMESTAMPTZ NOT NULL,
    payload         JSONB       NOT NULL,
    PRIMARY KEY (fab_id, event_id, ingested_at)
) PARTITION BY LIST (fab_id);

-- Per-fab list partition:
CREATE TABLE events_munich PARTITION OF events
    FOR VALUES IN ('munich')
    PARTITION BY RANGE (ingested_at);

-- Monthly range partitions under each fab:
CREATE TABLE events_munich_202605 PARTITION OF events_munich
    FOR VALUES FROM ('2026-06-01') TO ('2026-07-01');
-- (created lazily by a worker before first insert each month)

CREATE UNIQUE INDEX idx_events_fab_eventid ON events (fab_id, event_id);
CREATE INDEX idx_events_source_device_occurred
    ON events (fab_id, source, device_id, occurred_at DESC);
CREATE INDEX idx_events_ingested ON events (fab_id, ingested_at DESC);
```

The "create next month's partition" job lives in `MigrationRunner`
(ADR-0067) on a daily cron schedule — not in the request path.

## Mosquitto resource — Aspire + Helm

### Dev (Aspire AppHost)

```csharp
var mosquitto = builder.AddContainer("mosquitto", "eclipse-mosquitto", "2.0")
    .WithBindMount("./mosquitto/mosquitto.conf", "/mosquitto/config/mosquitto.conf")
    .WithBindMount("./mosquitto/passwords.txt", "/mosquitto/config/passwords.txt")
    .WithBindMount("./mosquitto/acl.txt", "/mosquitto/config/acl.txt")
    .WithEndpoint(targetPort: 1883, name: "mqtt", scheme: "tcp")
    .WithEndpoint(targetPort: 8883, name: "mqtts", scheme: "tcp");

var eventIngestion = builder.AddProject<Projects.EventIngestion_Api>("event-ingestion")
    .WithReference(eventIngestionDb)
    .WithReference(rabbitmq)
    .WithReference(mosquitto)
    .WaitFor(mosquitto);
```

### Prod (Helm)

Helm chart fragment generated by `aspire publish --target k8s` plus
a hand-maintained `mosquitto.values.yaml` for the per-fab password +
ACL ConfigMap. Mosquitto runs as a StatefulSet (single replica per
fab) with persistent volume for the QoS-1 persistent session store.

### Auth model

- One device password per source-device pair, generated at
  provisioning time, stored in the operator's secrets manager and
  in Mosquitto's password file (`passwords.txt`).
- ACL file: `user station-4` → `topic write fab/munich/plc/station-4`;
  same shape for every device.
- TLS-only on port 8883 in prod. Dev uses port 1883 plaintext.
- EventIngestion subscribes as a dedicated `event-ingestion`
  account with `topic read fab/+/+/+` ACL.

## Error handling — full taxonomy

| Failure mode | Manifestation | What ingestion does |
|---|---|---|
| MQTT not-JSON / missing field / payload > 64 KB | broker delivers message, parse fails | Insert into `dead_letters`, increment metric, ACK upstream (don't redeliver) |
| MQTT broker disconnects | subscriber loses connection | MQTTnet auto-reconnect with backoff; session persistent so no events lost |
| HTTP body > 64 KB | request rejected at Kestrel limit | 413 Payload Too Large |
| HTTP body not-JSON / missing field | model binding failure | 400 with sealed-record error |
| HTTP webhook bearer missing/wrong | hash compare fails | 401 with generic "unauthorized" message (no integration-existence leak) |
| HTTP manual missing OIDC | middleware rejects | 401 |
| HTTP manual scope missing | policy fails | 403 |
| Duplicate `(fabId, eventId)` | unique constraint hit | 200 OK with `idempotent=true` flag in response body; no `FabEventIngestedV1` republished |
| Channel full | bounded channel rejects | HTTP: 429 + Retry-After: 1; MQTT: subscriber blocks on `WriteAsync`, broker holds depth |
| Postgres unavailable | persistence loop throws | Channel drains stop; HTTP 503; MQTT subscriber blocked — broker queue absorbs while we recover |
| Wolverine outbox dispatch failure | `FabEventIngestedV1` undeliverable | Wolverine's built-in retry + DLQ (outbox row not removed until acked) |

## Performance Validation (NFR-001 = ≤ 50 ms p95)

Plan-phase commitment: the integration test `NFR001_LatencyTests`
asserts p95 ≤ 50 ms over a warm 100-iteration run on a clean
Testcontainers Postgres + Mosquitto. The breakdown is monitored:

| Span | Budget | Approach to hit it |
|---|---|---|
| `ingest.parse` | ≤ 5 ms | `System.Text.Json` source-generated parser for the envelope |
| `ingest.channel-write` | ≤ 1 ms | bounded channel, no contention at sustained rate |
| `ingest.persist` | ≤ 25 ms | single INSERT into `events` + outbox row in one transaction; prepared statement |
| `ingest.outbox-dispatch` | ≤ 15 ms | Wolverine async dispatcher reads outbox row → RabbitMQ |
| headroom | ≤ 5 ms | absorb GC pauses, network jitter |

Sustained throughput target (1 000 ev/s) means per-event budget
≈ 1 ms wall time per event, so the 50 ms p95 is comfortable as long
as the persistence loop runs **concurrent** with parsing — the
hosted service uses `Channel<T>` to decouple them.

## Out of Scope (deferred — re-stated for the plan)

- **Frontend (spec 006a):** management-web pages to browse
  `/events`, `/events/dead-letters`, and to manage webhook
  integrations. Deferred so this PR-stack stays backend-only.
  The API endpoints ship now; the UI lands in a tiny follow-up
  spec.
- **Archive job (spec 007):** Postgres → MinIO Parquet nightly
  worker + cold-read API.
- **Automation (spec 008):** `FabEventIngestedV1` consumer that
  evaluates rules and (eventually) mutates SystemVariables.

## PR shape (Phase 7 preview — drives the task breakdown)

Six PRs against `develop`, in dependency order:

| PR | Title | Scope | Gate |
|---|---|---|---|
| A | `feat(event-ingestion): scaffold + Mosquitto Aspire resource + ADR-0095/0096` | Empty projects, Aspire wiring, ADRs, broker dev config | Aspire boots; `mosquitto` resource green |
| B | `feat(event-ingestion): Domain + Shared.Contracts FabEventIngestedV1` | Aggregates, VOs, V1 contract, domain tests | Domain tests ≥ 90% coverage |
| C | `feat(event-ingestion): Application + bounded channel + Wolverine outbox` | Commands, queries, handlers, bounded channel, in-memory fakes | Application tests ≥ 80%; backpressure test passes |
| D | `feat(event-ingestion): Infrastructure (EF Core + partitioning + MQTT subscriber + HTTP)` | DbContext, migrations with partitioning DDL, MQTTnet client, endpoints, Mosquitto auth | Integration test: MQTT publish → row + outbox |
| E | `feat(event-ingestion): webhook-integrations CRUD + dead-letter API` | Registration / revoke endpoints; dead-letter list | Integration tests for both |
| F | `test(event-ingestion): NFR-001 latency + 5×burst absorbtion + polish` | Coverage gates, latency assertions, boundary test extension, README quickstart | All coverage gates pass; latency p95 ≤ 50 ms |

Phase-5 manual verification (aspire run + publish a real MQTT
event end-to-end through to a `FabEventIngestedV1` on the bus) is
the spec-006 verification note, not a PR gate.

## Gate (Phase 2 → Phase 3)

This plan is ready for the Tasks phase once the architect lead
confirms:

1. The Mosquitto-as-new-infra decision is locked (ADR-0095 to be
   drafted in PR A).
2. The MQTTnet choice is acceptable (ADR-0096 in PR A).
3. The PR shape above (A–F) matches the team's preferred review
   cadence.
4. The 50 ms p95 NFR-001 budget breakdown is plausible to verify
   in CI under Testcontainers.

When the gate is approved, Phase 3 (`/speckit-tasks`) decomposes
this plan into atomic tasks (target ~80–100, one per file/handler/
test/migration), each ≤ 30 minutes of work, with `[P]` markers on
parallelizable tasks and `[US-N]` cross-references back to the
spec's user stories.
