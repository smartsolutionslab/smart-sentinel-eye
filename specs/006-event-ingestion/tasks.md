# Tasks: 006 — EventIngestion

**Input:** Design documents at `specs/006-event-ingestion/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 gate approved
2026-05-28), [plan.md](./plan.md) (Phase 2 gate approved
2026-05-28).

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (PLC MQTT), US2 (Inference MQTT), US3 (Manual HTTP), US4 (Webhook HTTP), US5 (Backpressure / burst), FOUND (foundational), READ (read-API surface), POLISH.
- File paths reference the layout from [plan.md](./plan.md).

## Path conventions

- Backend: `src/EventIngestion/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/EventIngestion/`, `src/MigrationRunner/`, `src/AppHost/`
- ADRs: `docs/adr/0095-mosquitto-mqtt-broker.md`, `docs/adr/0096-mqttnet-client-library.md`
- Tests: `tests/EventIngestion.{Domain,Application,Integration}.Tests/`, `tests/Architecture.Tests/`, `tests/Shared.Contracts.Tests/`

Primitives from prior specs (`Option<T>`, `Result<T,E>`, `Ensure`, `AggregateRoot<TId>`, `IValueObject<T>`, `IEventBus`, `IClock`, `AspireFixture`, etc.) are reused — not repeated as tasks here.

---

## Phase 1: Foundational — Aspire + Mosquitto + V1 contract + ADRs

Blocks every user-story task. Adds the new infrastructure component
(Mosquitto) and the per-context Aspire wiring without touching the
aggregate.

- [ ] **T001 [FOUND]** Draft **ADR-0095** `docs/adr/0095-mosquitto-mqtt-broker.md` recording: Mosquitto chosen as the canonical MQTT broker; alternatives weighed (RabbitMQ MQTT plugin — noisy neighbour vs Wolverine queues; EMQX — heavier than 1k/s target). Topic taxonomy `fab/{fabId}/{source}/{deviceId}`. QoS 1. TLS in prod / plaintext in dev. Per-device passwords + ACL file.
- [ ] **T002 [P] [FOUND]** Draft **ADR-0096** `docs/adr/0096-mqttnet-client-library.md` recording: MQTTnet picked as the .NET MQTT client (most-downloaded .NET MQTT lib, MIT, active maintenance). Alternatives: M2Mqtt (unmaintained), HiveMQ .NET client (commercial).
- [ ] **T003 [P] [FOUND]** Create `src/AppHost/mosquitto/mosquitto.conf` (allow_anonymous false, password_file + acl_file paths, listener 1883 + 8883, persistence true).
- [ ] **T004 [P] [FOUND]** Create `src/AppHost/mosquitto/passwords.txt` with dev-only seed (one PLC device per fab, one inference device per fab, plus the `event-ingestion` subscriber account). Document that prod values come from Helm secrets.
- [ ] **T005 [P] [FOUND]** Create `src/AppHost/mosquitto/acl.txt` matching the dev passwords file. Each device gets `topic write fab/<fab>/<source>/<device>`; the subscriber gets `topic read fab/+/+/+`.
- [ ] **T006 [FOUND]** Add `mosquitto` container resource to `src/AppHost/AppHost.cs` using `AddContainer("mosquitto", "eclipse-mosquitto", "2.0")` + bind-mounts for the three config files + `WithEndpoint(targetPort: 1883, name: "mqtt", scheme: "tcp")`.
- [ ] **T007 [FOUND]** Add `event-ingestion-db` to the existing `postgres` resource: `var eventIngestionDb = postgres.AddDatabase("event-ingestion-db");` + `migrations.WithReference(eventIngestionDb).WaitFor(eventIngestionDb)`.
- [ ] **T008 [FOUND]** Wire the `event-ingestion` API project: `builder.AddProject<Projects.SmartSentinelEye_EventIngestion_Api>("event-ingestion").WithHttpEndpoint().WithReference(eventIngestionDb).WithReference(rabbitmq).WithReference(keycloak).WithReference(mosquitto).WaitFor(mosquitto).WaitForCompletion(migrations)`.
- [ ] **T009 [P] [FOUND]** `EventIngestion.Domain.csproj` mirrors the SystemVariables.Domain shape. NO framework refs beyond `Shared.Kernel`.
- [ ] **T010 [P] [FOUND]** `EventIngestion.Application.csproj` refs: Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts. PackageRef `Microsoft.Extensions.Logging.Abstractions` + `Microsoft.EntityFrameworkCore` (IQueryable seam).
- [ ] **T011 [P] [FOUND]** `EventIngestion.Infrastructure.csproj` refs: EFCore + Npgsql + WolverineFx + `MQTTnet` (~4.3.x) + `MQTTnet.AspNetCore`. FrameworkReference `Microsoft.AspNetCore.App`.
- [ ] **T012 [P] [FOUND]** `EventIngestion.Api.csproj` refs: Infrastructure + ServiceDefaults. Add to the `slnx` solution file.
- [ ] **T013 [P] [FOUND]** Add `MigrationRunner` reference to `EventIngestion.Infrastructure` (so the migration worker picks up the new DbContext per ADR-0067).
- [ ] **T014 [P] [FOUND]** `FabEventIngestedV1` record in `src/Shared.Contracts/EventIngestion/FabEventIngestedV1.cs`: `(Guid EventIdentifier, string Fab, string Source, string Device, string Kind, DateTimeOffset OccurredAt, DateTimeOffset IngestedAt, string Payload) : IIntegrationEvent`. Payload carried as canonicalized JSON string.
- [ ] **T015 [P] [FOUND]** `FabEventIngestedV1Tests` in `tests/Shared.Contracts.Tests/`: positional ctor, `IIntegrationEvent` marker, equality, JSON round-trip with a 60 KB payload.
- [ ] **T016 [FOUND]** Extend `tests/Architecture.Tests/BoundaryTests.cs` with a positive test that `EventIngestion.Domain` has no framework deps (SignalR/EF/Wolverine/Npgsql/MQTTnet). No new `AllowedCrossContext` entries (per plan §Constitution Check §III).

**Checkpoint:** `aspire run` brings up the `mosquitto` container alongside Postgres + RabbitMQ + Keycloak. The `event-ingestion` project resource appears in the dashboard (failing-to-start at this point is fine — no DB schema yet). Architecture tests pass. ADRs merged.

---

## Phase 2: User Story 1 — PLC MQTT ingest (P1)

**Goal:** A PLC gateway publishes JSON to `fab/munich/plc/station-4`; the event lands in `events`, `FabEventIngestedV1` lands on the bus, within ≤ 50 ms p95.

**Independent Test:** `MqttIngressTests.Plc_event_lands_and_fans_out_within_budget`.

### Tests first (TDD per Karpathy guideline #4)

- [ ] **T017 [P] [US1]** `EventIdentifierTests`.
- [ ] **T018 [P] [US1]** `FabIdentifierTests` — grammar `^[a-z][a-z0-9-]{1,31}$`.
- [ ] **T019 [P] [US1]** `SourceTests` — four singletons + `From(string)` round-trip + unknown-string rejection.
- [ ] **T020 [P] [US1]** `DeviceIdentifierTests` — `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`, no whitespace.
- [ ] **T021 [P] [US1]** `KindTests` — `^[A-Z][A-Za-z0-9]{0,127}$` (PascalCase).
- [ ] **T022 [P] [US1]** `OccurredAtTests` — UTC normalization, rejects `> now+5min` skew.
- [ ] **T023 [P] [US1]** `IngestedAtTests` — UTC, server-minted.
- [ ] **T024 [P] [US1]** `PayloadTests` — valid JSON pass; not-JSON rejected; > 64 KB rejected; canonical-form round-trip.
- [ ] **T025 [P] [US1]** `EventTests` aggregate-level: `Ingest` factory produces matching identifiers + raises `EventIngestedDomainEvent`; skew rule enforced.
- [ ] **T026 [P] [US1]** `EventBuilder` fluent test helper (ADR-0054).
- [ ] **T027 [P] [US1]** Application test fakes — `InMemoryEventRepository`, `FakeIngestChannel`, `FakeClock`, `FakeEventBus` (reuse pattern from spec 005).
- [ ] **T028 [P] [US1]** `IngestMqttEventCommandHandlerTests` — happy path raises `FabEventIngestedV1`; duplicate `(fabId, eventId)` returns `EventAlreadyIngested` (idempotent success); skew-too-large returns `OccurredAtTooFarInFuture`.

### Domain layer

- [ ] **T029 [P] [US1]** `EventIdentifier` Guid v7 strongly-typed id.
- [ ] **T030 [P] [US1]** `FabIdentifier` StringValueObject.
- [ ] **T031 [P] [US1]** `Source` discriminated VO (`Plc | Inference | Manual | Webhook`).
- [ ] **T032 [P] [US1]** `DeviceIdentifier` StringValueObject.
- [ ] **T033 [P] [US1]** `Kind` StringValueObject.
- [ ] **T034 [P] [US1]** `OccurredAt` DateTimeOffset VO.
- [ ] **T035 [P] [US1]** `IngestedAt` DateTimeOffset VO.
- [ ] **T036 [P] [US1]** `Payload` VO with `From(string)` + `From(JsonDocument)` + `≤ 64 KB` invariant.
- [ ] **T037 [P] [US1]** `EventIngestedDomainEvent`.
- [ ] **T038 [US1]** `Event` aggregate root with `Ingest` static factory + private setters + raised `EventIngestedDomainEvent`.
- [ ] **T039 [P] [US1]** `IEventRepository` (`GetByIdentifierAsync`, `ExistsAsync(fab, eventId)`, `Add`, `SaveAsync`).

### Application layer — MQTT path

- [ ] **T040 [P] [US1]** `IngestMqttEventCommand` + `IngestMqttEventErrors` (`EventAlreadyIngested`, `OccurredAtTooFarInFuture`, `PayloadTooLarge`, `MalformedPayload`).
- [ ] **T041 [US1]** `IngestMqttEventCommandHandler` — checks dedup via `IEventRepository.ExistsAsync`; constructs `Event` via factory; persists; lets domain event publish `FabEventIngestedV1`.
- [ ] **T042 [P] [US1]** `EventIngestedDomainEventHandler` — converts the domain event into `FabEventIngestedV1` and publishes via `IEventBus` (Wolverine outbox).

### Infrastructure — DB + MQTT subscriber

- [ ] **T043 [P] [US1]** `EventIngestionDbContext` with `DbSet<Event>` only (other aggregates added in later phases).
- [ ] **T044 [US1]** `EventConfiguration` — table `events`, partitioning DDL via raw SQL in the migration; column mappings; unique index `(fab_id, event_id)`.
- [ ] **T045 [US1]** Initial migration `20260528_InitialEventIngestionSchema.cs` — creates `events` partitioned table + per-fab list partition + first monthly range partition + the three documented indexes.
- [ ] **T046 [P] [US1]** `EventRepository`.
- [ ] **T047 [P] [US1]** `MosquittoConnectionFactory` — reads Aspire connection string + credentials; builds an `IMqttClient` with TLS-optional, persistent session, QoS 1.
- [ ] **T048 [US1]** `MqttSubscriberHostedService` — connects on `Start`, subscribes to `fab/+/+/+`, parses inbound payload into `EventEnvelope`, writes to `IIngestChannel`, defers MQTT ACK until persistence loop completes. Reconnect-with-backoff on disconnect.
- [ ] **T049 [P] [US1]** `EventIngestionInfrastructureModule.AddEventIngestionInfrastructure` registers the DbContext, repositories, the bounded channel (placeholder until US5), `MosquittoConnectionFactory`, the MQTT hosted service, and the persistence-loop hosted service (placeholder).
- [ ] **T050 [US1]** `PersistenceLoopHostedService` — reads from `IIngestChannel`, calls `IngestMqttEventCommandHandler`, signals upstream ACK via a callback. For US1 this is a single concurrent reader; tuning waits for US5.

### Integration test (US1 acceptance)

- [ ] **T051 [US1]** `MqttIngressTests.Plc_event_lands_and_fans_out_within_budget` — uses Testcontainers (Postgres + Mosquitto), publishes a PLC JSON event, asserts the row is in `events`, the dedup unique-constraint exists, a `FabEventIngestedV1` arrived on a temporary RabbitMQ queue, all within ≤ 100 ms (CI tolerance; NFR-001's 50 ms p95 is asserted by T103).

**Checkpoint:** Publishing a JSON PLC event via `mosquitto_pub` against the dev Aspire stack lands a row in `events` and a `FabEventIngestedV1` on the integration bus.

---

## Phase 3: User Story 2 — Camera-inference MQTT ingest (P1)

**Goal:** Camera inference events ingest the same way; the envelope-+-opaque-payload model handles bounding-box / snapshot URL payloads with zero schema changes.

**Independent Test:** `MqttIngressTests.Inference_event_with_nested_payload_round_trips_verbatim`.

- [ ] **T052 [P] [US2]** Add an inference-source-publisher entry to dev `passwords.txt` + `acl.txt`.
- [ ] **T053 [P] [US2]** Test fixture: a 4 KB inference JSON payload sample with nested arrays + S3 URL stored at `tests/EventIngestion.Integration.Tests/Fixtures/inference-sample.json`.
- [ ] **T054 [P] [US2]** `MqttIngressTests.Inference_event_with_nested_payload_round_trips_verbatim` — publish the fixture, fetch via `EventRepository.GetByIdentifier`, assert JSONB column equals canonical-form of input byte-for-byte.
- [ ] **T055 [P] [US2]** `MqttIngressTests.Two_events_from_same_device_arrive_FIFO_downstream` — publish A then B from `camera-12`; assert outbox messages for the two `FabEventIngestedV1` are in arrival order.

**Checkpoint:** Inference path is exercised end-to-end; per-`(source, deviceId)` FIFO ordering is asserted.

---

## Phase 4: User Story 3 — Manual operator annotation HTTP ingest (P1)

**Goal:** A kiosk operator submits an annotation via `POST /events/manual`; auth via OIDC bearer; same downstream path as MQTT.

**Independent Test:** `HttpManualIngressTests.Authenticated_operator_can_post_annotation`.

### Tests first

- [ ] **T056 [P] [US3]** `IngestManualEventCommandHandlerTests` — happy path, missing required field returns `MalformedRequest`, payload > 64 KB returns `PayloadTooLarge`.

### Application + API

- [ ] **T057 [P] [US3]** `IngestManualEventCommand` + `IngestManualEventErrors`.
- [ ] **T058 [US3]** `IngestManualEventCommandHandler` — server-mints `EventIdentifier` (Guid v7) + `IngestedAt`; pushes onto `IIngestChannel`; persistence loop does the rest.
- [ ] **T059 [P] [US3]** `EventsEndpoints.PostManual` — `POST /events/manual`, requires `operator` policy (ADR-0007/0008), 64 KB body cap.
- [ ] **T060 [US3]** Wire endpoints in `EventIngestionApiModule` + `Program.cs`; Kestrel `MaxRequestBodySize = 70 * 1024` (envelope + 64 KB payload + framing).

### Integration test

- [ ] **T061 [US3]** `HttpManualIngressTests.Authenticated_operator_can_post_annotation` — POSTs with a Keycloak-issued operator token; asserts row + outbox.
- [ ] **T062 [P] [US3]** `HttpManualIngressTests.Unauthenticated_post_returns_401`.
- [ ] **T063 [P] [US3]** `HttpManualIngressTests.Payload_over_64KB_returns_413`.

**Checkpoint:** Kiosk can POST annotations end-to-end through Keycloak auth.

---

## Phase 5: User Story 4 — Webhook ingest (P2)

**Goal:** External system POSTs to `/events/webhook/{integration}` with a static bearer; same downstream path.

**Independent Test:** `HttpWebhookIngressTests.Registered_integration_can_post_with_token`.

### Tests first

- [ ] **T064 [P] [US4]** `WebhookIntegrationNameTests` — `^[a-z][a-z0-9-]{0,62}$`.
- [ ] **T065 [P] [US4]** `BearerTokenHashTests` — SHA-256 of plaintext, constant-time compare.
- [ ] **T066 [P] [US4]** `WebhookIntegrationTests` — `Register` factory returns plaintext once + hash; `Revoke` is idempotent.
- [ ] **T067 [P] [US4]** `RegisterWebhookIntegrationCommandHandlerTests` — happy path; name collision returns `WebhookIntegrationNameTaken`.
- [ ] **T068 [P] [US4]** `RevokeWebhookIntegrationCommandHandlerTests` — happy path; unknown name returns `WebhookIntegrationNotFound`.
- [ ] **T069 [P] [US4]** `IngestWebhookEventCommandHandlerTests` — happy path mints a new `eventId` each time (no dedup contract for webhook sources); revoked integration returns `WebhookIntegrationRevoked`.

### Domain

- [ ] **T070 [P] [US4]** `WebhookIntegrationName` VO.
- [ ] **T071 [P] [US4]** `BearerTokenHash` VO (SHA-256 of plaintext).
- [ ] **T072 [P] [US4]** `WebhookIntegration` aggregate + `Register` factory + `Revoke`.
- [ ] **T073 [P] [US4]** `WebhookIntegrationRegisteredDomainEvent` + `WebhookIntegrationRevokedDomainEvent`.
- [ ] **T074 [P] [US4]** `IWebhookIntegrationRepository`.

### Application

- [ ] **T075 [P] [US4]** `RegisterWebhookIntegrationCommand` + errors; `RevokeWebhookIntegrationCommand` + errors; `IngestWebhookEventCommand` + errors.
- [ ] **T076 [US4]** Three command handlers.

### Infrastructure + API

- [ ] **T077 [P] [US4]** `WebhookIntegrationConfiguration` + repository + extend DbContext.
- [ ] **T078 [US4]** Migration: adds `webhook_integrations` table.
- [ ] **T079 [P] [US4]** `WebhookIntegrationsEndpoints.cs` — `POST /webhook-integrations` (admin; returns plaintext token in body, once), `GET /webhook-integrations`, `DELETE /webhook-integrations/{name}`.
- [ ] **T080 [US4]** `EventsEndpoints.PostWebhook` — `POST /events/webhook/{integrationName}`, validates bearer via constant-time compare on the registered hash.

### Integration tests

- [ ] **T081 [US4]** `HttpWebhookIngressTests.Registered_integration_can_post_with_token`.
- [ ] **T082 [P] [US4]** `HttpWebhookIngressTests.Wrong_token_returns_401_without_information_leak`.
- [ ] **T083 [P] [US4]** `HttpWebhookIngressTests.Revoked_integration_returns_401`.

**Checkpoint:** Webhook path closed; integration registration + revocation work end-to-end.

---

## Phase 6: User Story 5 — Bounded channel + 429 backpressure (P2)

**Goal:** Bounded channel of 5 000 slots is the single buffer for all three ingress paths; full → HTTP 429 + MQTT subscriber blocks.

**Independent Test:** `BackpressureTests.Burst_above_drain_rate_returns_429_then_recovers`.

### Tests first

- [ ] **T084 [P] [US5]** `BoundedIngestChannelTests` — drain order is FIFO; full-channel `TryWrite` returns false; `WriteAsync` blocks the caller until drain.
- [ ] **T085 [P] [US5]** `ChannelMetricsTests` — counters increment on each path.

### Application + Infrastructure

- [ ] **T086 [P] [US5]** `IIngestChannel` interface + `EventEnvelope` internal DTO.
- [ ] **T087 [US5]** `BoundedIngestChannel` — singleton wrapper around `Channel.CreateBounded<EventEnvelope>(new BoundedChannelOptions(5_000) { FullMode = BoundedChannelFullMode.Wait })`.
- [ ] **T088 [P] [US5]** `ChannelMetrics` — Prometheus counters via the OTEL exporter.
- [ ] **T089 [US5]** MQTT subscriber: when channel is full, `WriteAsync` blocks → broker stops getting ACKs; QoS 1 means the broker holds queue depth.
- [ ] **T090 [US5]** HTTP endpoints: when `IIngestChannel.TryWrite` returns false, return `429 Too Many Requests` with `Retry-After: 1`.

### Integration test

- [ ] **T091 [US5]** `BackpressureTests.Burst_above_drain_rate_returns_429_then_recovers` — load gen pushes 5 000 ev/s for 30 s against the test app; asserts some HTTP requests get 429; after the burst ends, channel drains within 60 s; final `COUNT(*)` in `events` matches the count of accepted ingress requests + accepted MQTT ACKs.

**Checkpoint:** Burst test passes; no events silently lost.

---

## Phase 7: Read API — list/get events + dead letters + integrations

**Goal:** Admin can list events, fetch one, list dead letters, and list registered webhook integrations.

### Tests first

- [ ] **T092 [P] [READ]** `ListEventsQueryHandlerTests` — filter by source / device / kind / occurredAfter / ingestedAfter; cursor pagination returns next-cursor; default page-size 100 / max 1 000.
- [ ] **T093 [P] [READ]** `GetEventQueryHandlerTests` — found → DTO; not-found → `EventNotFound`.
- [ ] **T094 [P] [READ]** `ListDeadLettersQueryHandlerTests`.
- [ ] **T095 [P] [READ]** `ListWebhookIntegrationsQueryHandlerTests` — returns DTOs without token hash.

### Domain (DeadLetter)

- [ ] **T096 [P] [READ]** `DeadLetter` aggregate + `DeadLetterIdentifier`.
- [ ] **T097 [P] [READ]** `IDeadLetterRepository` + EF config + migration adds `dead_letters` table.
- [ ] **T098 [READ]** MQTT subscriber writes to `dead_letters` on parse failure (FR-014 / FR-015).

### Application

- [ ] **T099 [P] [READ]** `GetEventQuery` + handler; `ListEventsQuery` + handler with cursor pagination; `ListDeadLettersQuery` + handler; `ListWebhookIntegrationsQuery` + handler.
- [ ] **T100 [P] [READ]** `EventDto`, `EventPageDto`, `DeadLetterDto`, `WebhookIntegrationDto` (no token hash).

### API + integration

- [ ] **T101 [READ]** `EventsEndpoints` adds `GET /events` (cursor pagination), `GET /events/{id}`, `GET /events/dead-letters` (admin only).
- [ ] **T102 [READ]** `WebhookIntegrationsEndpoints` adds `GET /webhook-integrations` listing.

**Checkpoint:** Admin can navigate the events history via API. No frontend (deferred to spec 006a).

---

## Phase 8: Polish — coverage, latency, README, architecture

- [ ] **T103 [POLISH]** `NFR001_LatencyTests` — warms 20 iterations; asserts p95 ≤ 50 ms over the warm 100-iteration measurement window against Testcontainers Postgres + Mosquitto.
- [ ] **T104 [POLISH]** Extend `scripts/coverage-check.ps1` with three new gated assemblies: `SmartSentinelEye.EventIngestion.Domain >= 90`, `SmartSentinelEye.EventIngestion.Application >= 80`, `SmartSentinelEye.EventIngestion.Infrastructure >= 70` (lower bar because hosted-service + EF wiring is harder to unit-test).
- [ ] **T105 [P] [POLISH]** Add coverlet collector ref to the three new test projects so they show up in the coverage merge.
- [ ] **T106 [P] [POLISH]** README "Publish an event end-to-end" quickstart section: `mosquitto_pub` example for PLC, `curl` example for manual + webhook, `GET /events` to verify.
- [ ] **T107 [P] [POLISH]** Helm chart fragment for Mosquitto (`deploy/helm/mosquitto/` — values.yaml + StatefulSet + PVC + ConfigMap for password/ACL files). Documented but NOT wired into the prod pipeline in spec 006 (Helm full integration is its own ops PR).
- [ ] **T108 [P] [POLISH]** `MigrationRunner` partition-rollover cron job: creates next month's range partition under each `events_<fab>` parent before the 1st of each month.
- [ ] **T109 [POLISH]** Phase-5 manual verification note: `aspire run`, `mosquitto_pub` publishes a PLC event, observe row in `events`, observe message in RabbitMQ management UI. Captured in the spec 006 release PR description.

**Checkpoint:** All coverage gates pass; latency assertion passes; README updated. Spec 006 ready for the Phase-7 release.

---

## Dependencies between phases

```
Phase 1 (Foundational)
   │
   ▼
Phase 2 (US1: PLC MQTT) ──────────────┐
   │                                  │
   ▼                                  │
Phase 3 (US2: Inference MQTT)         │  ← reuses Phase 2 infra
                                      │
   ┌──────────────────────────────────┘
   ▼
Phase 4 (US3: Manual HTTP)            ← independent of US2; can run parallel
Phase 5 (US4: Webhook HTTP)           ← independent of US3 once auth pattern is fixed
   │
   ▼
Phase 6 (US5: Backpressure)           ← retrofit on top of all three ingress paths
   │
   ▼
Phase 7 (Read API)                     ← reads on data produced by Phases 2-6
   │
   ▼
Phase 8 (Polish + NFR-001)
```

## Estimation

- 109 atomic tasks. Of which **[P] = 76** (≈ 70%) can run in parallel within their phase.
- Target PR cadence (per plan): 6 PRs (A–F) mapping roughly to Phases 1 / 2 / 3-4 / 5 / 6-7 / 8.
- Walking-skeleton-style critical path: T001 → T038 → T044 → T045 → T048 → T050 → T051 (PLC MQTT round-trip).
