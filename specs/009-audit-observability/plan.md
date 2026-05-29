# Implementation Plan: 009 — AuditObservability

**Branch:** `009-audit-observability` | **Date:** 2026-05-29 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from
`specs/009-audit-observability/spec.md` (Phase 1, eight Q&A
clarifications resolved across two rounds, zero
`[NEEDS CLARIFICATION]` markers). Phase-1 gate approved 2026-05-29.
The constitution amendment that gated Phase 2 is in place:
**ADR-0101** records the TimescaleDB decision and § Backend of
the constitution now permits TimescaleDB in time-series-shaped
contexts.

## Summary

Lights up the **AuditObservability** bounded context — a
bus-fed audit trail of every `*V1` integration event with hot
search + per-resource timelines + a per-chunk cold archive on
MinIO. v1 is **backend + read API + management-web Audit page**.

- **`audit-observability` Aspire project** already scaffolded
  from spec 008; this spec puts code in it.
- **`audit-db` (TimescaleDB extension)** — new Postgres
  database on the existing Aspire `postgres` server resource;
  the dev image bumps to `timescale/timescaledb-ha:pg17-oss`
  per ADR-0101. The `audit_events` table is a hypertable with a
  1-month chunk interval, declarative compression after 30 days,
  and a unique index on `event_identifier` for Wolverine-replay
  idempotency.
- **Open-generic Wolverine subscriber** — a single
  `AuditingMessageHandler<TIntegrationEvent>` binds to every
  type implementing `IIntegrationEvent` via reflection at
  startup. New V1s in `Shared.Contracts` are audited
  automatically on next deploy; no per-context plumbing.
- **Resource-mapping registry** — a static
  `V1ResourceMap` reads the V1's namespace + name pattern to
  populate `resource_kind` and pluck the aggregate identifier.
  Unknown shapes still audit (FR-005) with null resource fields
  + a counter increment.
- **HTTP read API** (Identity API-style minimal endpoints,
  spec 008 patterns):
  - `GET /audit` — flat search with the FR-008 filter set.
  - `GET /audit/{resourceKind}/{resourceIdentifier}` —
    per-resource timeline (FR-009).
  - `GET /audit/{auditIdentifier}` — full row + payload
    (FR-010).
  - All three behind `sse.audit.read` + the existing
    `IFabAuthorizationGuard` from spec 008.
- **Retention worker** (`AuditRetentionHostedService`) — daily
  cron-ish background service. For each chunk past 90 days:
  stream rows → upload gzipped NDJSON to MinIO with
  `Content-MD5` ETag verification → emit
  `AuditChunkArchivedV1` → `drop_chunks()`. Idempotent under
  mid-flight failure (re-checks for the object before
  re-uploading).
- **Management-web Audit page** — new top-nav entry, search
  bar wired to RTK Query + virtualised result table. Builds on
  the existing design-system primitives (ADR-0077/0078).
- **Scope catalogue addition** — `sse.audit.read` joins the
  spec 008 `Scope` catalogue + the admin / operator bundles in
  the realm import.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Hot persistence | EF Core on **TimescaleDB-extended PostgreSQL** (per-context DB `audit-db`) | ADR-0009, **ADR-0101** |
| Cold archive | MinIO via the AWS SDK for .NET `S3` client | ADR-0009 |
| Messaging | RabbitMQ via Wolverine; open-generic subscriber on `IIntegrationEvent`; Postgres outbox | ADR-0088 |
| Idempotency | Unique index on `event_identifier`; `INSERT … ON CONFLICT (event_identifier) DO NOTHING` | spec FR-006 |
| Read auth | `sse.audit.read` scope + `IFabAuthorizationGuard` (groups-claim filtered when `fabId` omitted) | spec 008 FR-002 / FR-019 |
| Read API style | Minimal APIs only | ADR-0070 |
| Errors | `Result<T, ApiError>` with sealed-record error hierarchies | ADR-0047, ADR-0089 |
| Retention engine | TimescaleDB `drop_chunks()` called by the app after MinIO export confirms | ADR-0101 |
| Compression | TimescaleDB native column compression, `compress_after => INTERVAL '30 days'` | ADR-0101 |
| Performance | Ingest ≤ 50 ms p99 / search ≤ 200 ms p99 / per-resource ≤ 100 ms p99 | spec NFR-001/002 |
| Scale | 100 ev/s sustained, 90 d hot window ≈ 78 GB hypertable + indexes | spec NFR-003 |

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| §I On-prem first | Postgres + MinIO already on-prem per fab. No new external dependencies. | ✅ |
| §II DDD + VOs | `AuditIdentifier`, `EventIdentifier`, `ResourceKind`, `EventKind` VOs. `AuditEvent` is a CRUD entity (not an aggregate), Repository pattern stays. | ✅ |
| §III Bounded-context isolation | All new code in `SmartSentinelEye.AuditObservability.*`. Reads `Shared.Contracts` only — never any other context's Domain or Application. **No new `AllowedCrossContext` entries.** | ✅ |
| §IV Latency budget | Audit subscription is async + decoupled from the originating context's request path. Read endpoints have their own NFR budget (200 ms p99 search) well clear of any context's hot path. | ✅ |
| §V Spec-driven | Spec gate approved 2026-05-29. This plan. Tasks follow. | ✅ |
| §VI Aspire composition root | New AppHost resources: `audit-db` (database on existing `postgres` server, image bumped per ADR-0101) + MinIO bucket `audit-archive` (new bucket on existing object store). The `audit-observability` project already exists in AppHost from spec 008 (stubbed). | ✅ |
| §VII No event sourcing without justification | `AuditEvent` is the simplest possible CRUD entity — single `INSERT … ON CONFLICT`. No aggregate state. | ✅ |
| §VIII Safe at trust boundaries | JWT validation + scope check + fab guard at every read endpoint via `ServiceDefaults` (reuses spec 008 plumbing). | ✅ |
| §IX Forward-compat | `schema_version` column reserves the slot; unknown V1 shapes audit with `resource_kind = null`. | ✅ |
| §X Locked tech stack (post-amendment) | TimescaleDB is now permitted per ADR-0101 + constitution § Backend amendment. | ✅ |

**Result:** No violations. No Complexity Tracking entries.

**Tech-stack additions requiring ADR before Phase 4:**
- **ADR-0101** — TimescaleDB hypertable for the
  AuditObservability hot tier (already drafted; lands in PR A).

## Project Structure

### Documentation

- `specs/009-audit-observability/spec.md` — Phase 1.
- `specs/009-audit-observability/plan.md` — this file.
- `specs/009-audit-observability/tasks.md` — Phase 3 output.
- `docs/adr/0101-timescaledb-for-audit.md` — drafted; lands in
  PR A.

### Source code — files added / modified

```
src/
  ServiceDefaults/
    Authorization/
      Scope.cs                        # + sse.audit.read

  Shared.Contracts/
    AuditObservability/
      AuditChunkArchivedV1.cs         # new V1 (P3)

  AuditObservability/
    Domain/
      AuditEvent/
        AuditEvent.cs                 # plain CRUD entity (no aggregate state)
        AuditEventIdentifier.cs       # Guid v7 IStronglyTypedId<Guid>
        EventIdentifier.cs            # mirrors the V1's globally-unique key
        EventKind.cs                  # text VO, length-bounded, regex-validated
        ResourceKind.cs               # closed VO over the FR-009 vocabulary
        ResourceIdentifier.cs         # text VO
        ActorIdentifier.cs            # Guid (zero-Guid for system)
        IAuditEventRepository.cs

    Application/
      EventHandlers/
        AuditingMessageHandler.cs     # open-generic Wolverine handler
        V1ResourceMap.cs              # static map V1 type → (ResourceKind, identifier-picker delegate)
        V1ResourceMap.Conventions.cs  # convention-based fallback (read namespace + first Guid-shaped property)
      Queries/
        SearchAuditQuery.cs
        GetResourceTimelineQuery.cs
        GetAuditEventQuery.cs
        Handlers/
          SearchAuditQueryHandler.cs
          GetResourceTimelineQueryHandler.cs
          GetAuditEventQueryHandler.cs
      DTOs/
        AuditRowDto.cs
        AuditPageDto.cs
      Retention/
        IAuditChunkArchiver.cs        # interface; production impl in Infrastructure
        AuditRetentionHostedService.cs

    Infrastructure/
      Persistence/
        AuditObservabilityDbContext.cs
        Configurations/
          AuditEventConfiguration.cs  # hypertable creation SQL + indexes
        AuditEventRepository.cs       # INSERT ON CONFLICT
        DesignTimeDbContextFactory.cs
        AuditObservabilityMigrator.cs
        Migrations/                   # EF migrations; 0001 creates extension + hypertable
      Archive/
        MinioAuditChunkArchiver.cs    # S3 client wired to MinIO
        MinioOptions.cs
      AuditObservabilityInfrastructureModule.cs
      AuditObservabilityPersistenceModule.cs

    Api/
      AuditEndpoints.cs               # GET /audit*, GET /audit/{resourceKind}/{id}, GET /audit/{auditId}
      AuditObservabilityApiModule.cs
      Program.cs                      # AddServiceDefaults + AddBearerAuth + AddAudit*

  AppHost/
    AppHost.cs                        # bump postgres image → timescaledb-ha, add audit-db,
                                       # MinIO container resource, wire audit-observability project

  MigrationRunner/
    Program.cs                        # + builder.AddAuditObservabilityPersistence()

apps/management-web/
  src/
    pages/
      AuditPage.tsx                   # new top-nav route
      AuditPage.test.tsx
    api/
      audit.ts                        # RTK Query slice
    routes.tsx                        # + /audit route, sse.audit.read guard

tests/
  AuditObservability.Domain.Tests/
    AuditEvent/
      AuditEventTests.cs
      EventKindTests.cs
      ResourceKindTests.cs
      ActorIdentifierTests.cs
  AuditObservability.Application.Tests/
    EventHandlers/
      AuditingMessageHandlerTests.cs
      V1ResourceMapTests.cs
    Queries/
      SearchAuditQueryHandlerTests.cs
      GetResourceTimelineQueryHandlerTests.cs
      GetAuditEventQueryHandlerTests.cs
    Retention/
      AuditRetentionHostedServiceTests.cs
    Fakes/
      FakeAuditChunkArchiver.cs
      InMemoryAuditEventRepository.cs
      FakeBus.cs
      FakeClock.cs
  Integration.Tests/
    AuditObservability/
      EndToEndIngestionIntegrationTests.cs   # publish a V1, observe it in /audit
      CrossFabReadGuardIntegrationTests.cs
      RetentionRoundtripIntegrationTests.cs  # back-dates a chunk, runs the worker, asserts MinIO + drop
  Architecture.Tests/
    BoundaryTests.cs                  # extended to assert AuditObservability.Domain has zero framework deps
                                       # + AuditObservability.* references Shared.Contracts ONLY (no other context)
```

## Domain Model

### AuditEvent (CRUD entity)

`AuditEvent` is **not** an aggregate root. It carries no state
machine; once written it is immutable. The repository exposes
`Add(audit)` + `Save()`; reads go through query handlers
directly against the DbContext (no aggregate hydration cost
on the read path).

| Field                | Type                       | Source |
| -------------------- | -------------------------- | ------ |
| `Id`                 | `AuditEventIdentifier`     | mints client-side at handler — Guid v7, monotonic |
| `OccurredAt`         | `DateTimeOffset`           | bus message's `OccurredAt` (every V1 carries one) |
| `ReceivedAt`         | `DateTimeOffset`           | handler-local `IClock.UtcNow` |
| `Fab`                | `Option<FabIdentifier>`    | extracted from the V1 if present |
| `EventKind`          | `EventKind`                | `nameof(TIntegrationEvent)` |
| `ResourceKind`       | `Option<ResourceKind>`     | from `V1ResourceMap.Lookup(type)`; `None` for unmapped |
| `ResourceIdentifier` | `Option<ResourceIdentifier>` | mapper-picked from the V1 payload |
| `Actor`              | `ActorIdentifier`          | from `actor` property convention; system events get `ActorIdentifier.System` |
| `ActorUsername`      | `Option<string>`           | from `actorUsername` property if present |
| `EventIdentifier`    | `EventIdentifier`          | `eventId` convention property or the `Id` field if name matches |
| `Payload`            | `string` (validated jsonb) | `JsonSerializer.Serialize(envelope)` — verbatim |
| `PayloadSizeBytes`   | `int`                      | UTF-8 byte count of `Payload` |
| `SchemaVersion`      | `short`                    | always `1` in v1 |

### V1ResourceMap

A static `Dictionary<Type, ResourceMappingEntry>` populated at
startup by scanning `Shared.Contracts`:

```csharp
public sealed record ResourceMappingEntry(
    ResourceKind Kind,
    Func<object, ResourceIdentifier?> PickIdentifier);
```

Convention-first: for each `IIntegrationEvent` type in
`Shared.Contracts`:

- `ResourceKind` = the namespace's leaf (e.g.
  `Shared.Contracts.Automation.RuleCreatedV1` → `"rule"`).
- `PickIdentifier` walks reflection for a property whose name
  matches a small allow-list (`Identifier`, `<Aggregate>Identifier`,
  `Name`); the first match returns its `.ToString()`.

Hand-tweaks for edge cases sit in `V1ResourceMap.Conventions.cs`
as explicit `Register<TIntegrationEvent>(kind, picker)` calls.

The `EventKind` is always `typeof(T).Name` so the audit query
stays predictable even when a future V1 lacks a mapper entry.

## TimescaleDB schema

```sql
CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE audit_events (
    audit_id              uuid          PRIMARY KEY,
    occurred_at           timestamptz   NOT NULL,
    received_at           timestamptz   NOT NULL,
    fab_id                text,
    event_kind            text          NOT NULL,
    resource_kind         text,
    resource_identifier   text,
    actor_identifier      uuid          NOT NULL,
    actor_username        text,
    event_identifier      uuid          NOT NULL,
    payload               jsonb         NOT NULL,
    payload_size_bytes    integer       NOT NULL,
    schema_version        smallint      NOT NULL DEFAULT 1
);

SELECT create_hypertable(
    'audit_events',
    'occurred_at',
    chunk_time_interval => INTERVAL '1 month',
    if_not_exists => true);

-- Idempotency: absorb Wolverine at-least-once redeliveries.
CREATE UNIQUE INDEX ux_audit_event_identifier
    ON audit_events (event_identifier);

-- Cross-cutting search (occurred_at is the hypertable key,
-- so already partitioned; this btree covers the FR-008 filters).
CREATE INDEX ix_audit_actor_occurred
    ON audit_events (actor_identifier, occurred_at DESC);

CREATE INDEX ix_audit_fab_occurred
    ON audit_events (fab_id, occurred_at DESC)
    WHERE fab_id IS NOT NULL;

CREATE INDEX ix_audit_kind_occurred
    ON audit_events (event_kind, occurred_at DESC);

-- Per-resource timeline.
CREATE INDEX ix_audit_resource_occurred
    ON audit_events (resource_kind, resource_identifier, occurred_at DESC)
    WHERE resource_identifier IS NOT NULL;

-- 30-day window compresses behind the read path.
ALTER TABLE audit_events SET (timescaledb.compress);
SELECT add_compression_policy('audit_events', INTERVAL '30 days');
```

The compression policy runs in Timescale's own job scheduler;
the retention worker handles the export-then-drop boundary
explicitly so application logic owns the
"upload to MinIO succeeds before chunk goes away" invariant.

## Subscriber

A single Wolverine handler registered for the open generic
`IIntegrationEvent`:

```csharp
public sealed class AuditingMessageHandler(
    IAuditEventRepository repo,
    IClock clock,
    ILogger<AuditingMessageHandler> log)
{
    public async Task Handle(
        IIntegrationEvent message, Envelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        ResourceMappingEntry? map = V1ResourceMap.Lookup(message.GetType());
        AuditEvent row = AuditEvent.From(message, envelope, map, clock);
        repo.Add(row);
        await repo.SaveAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

The Wolverine `Postgres outbox + EF transactions` pair (per
ADR-0088) makes the row insert + bus ack atomic. The repository's
SaveAsync wraps `INSERT … ON CONFLICT (event_identifier) DO
NOTHING` so duplicate deliveries are silent.

## Read API

| Endpoint | Required scope | Behaviour |
|---|---|---|
| `GET /audit?…` | `sse.audit.read` | Cursor-paginated flat search; `fabId` optional. When omitted, the result is restricted to rows whose `fab_id` is in the caller's `groups` claim. |
| `GET /audit/{resourceKind}/{resourceIdentifier}?…` | `sse.audit.read` | Per-resource timeline; `fabId` mandatory (matches the spec 008 fab-guard rule on per-resource endpoints). |
| `GET /audit/{auditIdentifier}` | `sse.audit.read` | Single row + full payload JSON. The caller must belong to the row's fab via the existing `IFabAuthorizationGuard`. |

Cursor format: base64-encoded `(occurredAtTicks, auditIdentifier)`
tuple; the query handler decodes + applies a strict-greater-than
predicate so concurrent inserts don't shift the pagination
window.

## Retention worker

`AuditRetentionHostedService` runs once at startup + then
nightly at 02:00 fab-local time (configurable via
`AuditObservability:RetentionWindow`).

```
foreach chunk in show_chunks(audit_events, older_than => '90 days'):
    objectKey = format(fab_id, occurred_at_month)
    if MinIO has objectKey with matching ETag:
        // already archived; just drop
        drop_chunks(older_than => chunk.end)
        continue

    md5  = computeMd5(rows)
    upload(objectKey, gzippedNdjsonStream, contentMd5 = md5)
    verifyETag(uploaded.ETag == md5)
    publishV1(AuditChunkArchivedV1(...))
    drop_chunks(older_than => chunk.end)
```

The S3 `Content-MD5` header is the integrity contract; ETag
round-trip confirms the object survived the network without
silent truncation. If verification fails, the upload retries
twice with exponential backoff before logging an error + leaving
the chunk in place (storage cost for one extra day, recoverable
on next run).

## Cross-context wire-in — surfaces created

### New V1 contract

```csharp
namespace SmartSentinelEye.Shared.Contracts.AuditObservability;

public sealed record AuditChunkArchivedV1(
    Guid ChunkIdentifier,
    string? FabId,
    int RowCount,
    DateTimeOffset OccurredFrom,
    DateTimeOffset OccurredUntil,
    DateTimeOffset ArchivedAt,
    string MinioObjectKey,
    string ContentMd5) : IIntegrationEvent;
```

No subscribers in v1. The event is published for future
observability dashboards (spec 010) + the runbook's "audit
chunk did/didn't archive" alerting.

## Performance Validation

- **`AuditingMessageHandler` micro-benchmark.** A 1 000-event
  warm loop against an in-memory repo + `V1ResourceMap`
  lookup, asserting p99 ≤ 1 ms (well inside the 50 ms NFR-001
  ingest budget — the rest is for the bus + Postgres insert).
- **`SearchAuditQueryHandler` integration test.** Seed a 90-day
  hypertable with 100 ev/s of synthetic events (8 640 000
  rows), exercise the FR-008 filter grid, assert p99 search
  ≤ 200 ms with `since` set to the last 24 h.
- **`RetentionRoundtripIntegrationTests`.** Back-date a chunk
  to 91 days old via `TimeProvider`. Run the worker. Assert
  the chunk is gone from Postgres, the corresponding MinIO
  object exists with matching ETag + row count, and
  `AuditChunkArchivedV1` lands on the bus.

## Out of Scope (deferred — re-stated for the plan)

- Cold-read HTTP API. v1 archives only; investigators retrieve
  via direct MinIO download.
- Audit-log signing / tamper evidence. Standard Postgres
  durability + ETag-checked archive is enough for v1.
- Multi-replica subscriber w/ leader election. v1
  single-replica; outage replays from the bus.
- Audit-driven alerting in Grafana. The Grafana panel work
  sits inside the observability stack (ADR-0026), not in
  this context.
- PII redaction. v1 V1 payloads are assumed PII-free per the
  shared-contracts convention; if that changes, the emitting
  context adds redaction at source.

## PR shape (Phase 7 preview — drives the task breakdown)

Six PRs against `develop`, in dependency order:

| PR | Title | Scope | Gate |
|---|---|---|---|
| A | `feat(audit): scaffold + Aspire + ADR-0101 + V1 contract + Scope addition` | Empty `AuditObservability.{Domain,Application,Infrastructure,Api}` projects, `audit-db` (TimescaleDB-extended) wired in AppHost, MinIO bucket wired, ADR-0101 lands, `sse.audit.read` added to the spec 008 `Scope` catalogue + admin/operator bundles in the realm import, `AuditChunkArchivedV1` in `Shared.Contracts` + tests. | `aspire start` boots the new image; scope policy registered; V1 contract test passes. |
| B | `feat(audit): Domain — AuditEvent + VOs + V1ResourceMap` | All Domain VOs (`AuditEventIdentifier`, `EventKind`, `ResourceKind`, `ResourceIdentifier`, `ActorIdentifier`, `EventIdentifier`), the `AuditEvent` entity, `V1ResourceMap` with convention-based + hand-tweaked entries, Domain tests. | Domain tests ≥ 90 % coverage |
| C | `feat(audit): Application — subscriber + query handlers + retention service` | `AuditingMessageHandler` (open-generic), three query handlers, `AuditRetentionHostedService` with `FakeAuditChunkArchiver` for tests, Application tests. | Application tests ≥ 80 % coverage |
| D | `feat(audit): Infrastructure + read API endpoints + MinIO archiver` | `AuditObservabilityDbContext` (hypertable via migration), `AuditEventRepository`, `MinioAuditChunkArchiver`, full `AuditObservabilityInfrastructureModule`, three `/audit*` endpoints, `Program.cs`, MigrationRunner wire-in. | Integration test: publish a `RuleCreatedV1` → row appears in `GET /audit`. |
| E | `feat(audit): management-web Audit page + retention integration test` | New `AuditPage` in management-web with the design-system search form + virtualised table + per-row expand. RTK Query slice. Route guarded by `sse.audit.read`. `RetentionRoundtripIntegrationTests`. | Aspire boot → admin signs into management-web → audit page renders + lists recent events. |
| F | `feat(audit): coverage gates + arch tests + README quickstart + NFR tests` | `scripts/coverage-check.ps1` adds `AuditObservability.Domain ≥ 90` and `AuditObservability.Application ≥ 80`. `Architecture.Tests` asserts the bounded-context isolation + that the V1ResourceMap covers every `IIntegrationEvent` in `Shared.Contracts`. README "Audit who-did-what" quickstart. `NFR001_AuditIngestLatencyTests` + `NFR002_AuditSearchLatencyTests` (Aspire-fixture-based per the user's preference on spec 008). | All coverage gates pass; latency tests pass under the Aspire fixture. |

Phase-5 verification (run the spec 009 release end-to-end with
real bus traffic) is the release-PR gate.

## Gate (Phase 2 → Phase 3)

This plan is ready for the Tasks phase once the architect lead
confirms:

1. The PR shape above (A–F) matches the team's preferred review
   cadence.
2. **ADR-0101** + the constitution amendment are accepted (the
   text in PR A lands as drafted).
3. The open-generic `IIntegrationEvent` subscriber pattern is
   acceptable (vs explicit per-V1 subscribers — discussed in
   spec FR-001/FR-002).
4. The `V1ResourceMap` convention-first + hand-tweak pattern is
   the right ergonomic trade.
5. The retention worker contract — **always export before drop;
   never drop without confirmed-archived** — is what we want
   for the v1 compliance posture.
