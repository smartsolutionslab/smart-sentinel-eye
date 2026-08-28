# Tasks: 009 — AuditObservability

**Input:** Design documents at `specs/009-audit-observability/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 gate approved
2026-05-29), [plan.md](./plan.md) (Phase 2 gate approved
2026-05-29), [ADR-0101](../../docs/adr/0101-timescaledb-for-audit.md)
(TimescaleDB extension scoped to this context; constitution §
Backend amended).

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (Reviewer search), US2 (Operator pivot), US3 (Retention archive), FOUND, POLISH.

## Path conventions

- Backend: `src/AuditObservability/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/AuditObservability/`, `src/MigrationRunner/`, `src/AppHost/`
- ServiceDefaults: `src/ServiceDefaults/Authorization/Scope.cs` (one new constant only)
- Tests: `tests/AuditObservability.{Domain,Application}.Tests/`, `tests/Integration.Tests/AuditObservability/`, `tests/Architecture.Tests/`, `tests/Shared.Contracts.Tests/`
- ADRs: `docs/adr/0101-timescaledb-for-audit.md`
- Web: `apps/management-web/src/pages/AuditPage.*`, `apps/shared/src/api/audit.ts`

Primitives from prior specs (`Option<T>`, `Result<T,E>`, `Ensure`, `IValueObject<T>`, `IClock`, `IEventBus`, `AspireFixture`, etc.) are reused — not repeated.

---

## Phase 1: Foundational — Aspire + V1 contract + Scope addition + ADR-0101

**PR A** lands everything in this phase.

- [x] **T001 [FOUND]** Draft **ADR-0101** `docs/adr/0101-timescaledb-for-audit.md` (already drafted from Phase 2 — verify the version in PR A matches the plan exactly; mark `Status: Accepted` at merge time).
- [x] **T002 [P] [FOUND]** Constitution amendment: add the TimescaleDB line to `.specify/memory/constitution.md` § Backend (already drafted; verify in PR A).
- [x] **T003 [P] [FOUND]** Bump the AppHost `postgres` image to `timescale/timescaledb-ha:pg17-oss` via `.WithImageTag("pg17-oss")` (or the equivalent `WithImage(...)` call so the registry path is explicit).
- [x] **T004 [FOUND]** Add `audit-db` database resource: `var auditDb = postgres.AddDatabase("audit-db");` + wire into `migrations`.
- [x] **T005 [P] [FOUND]** Add MinIO Aspire container: `builder.AddMinio("minio")` (or `AddContainer("minio", "minio/minio", "RELEASE.2025-XX-XX")` if no native extension) + persistent volume in run mode + dev seed bucket `audit-archive`.
- [x] **T006 [P] [FOUND]** Wire the `audit-observability` API project in `AppHost.cs`: `WithHttpEndpoint().WithReference(auditDb).WithReference(rabbitmq).WithReference(keycloak).WithReference(minio).WaitForCompletion(migrations).WaitFor(rabbitmq).WaitFor(keycloak).WaitFor(minio)`.
- [x] **T007 [P] [FOUND]** `AuditObservability.Domain.csproj` mirrors Identity.Domain shape (Shared.Kernel only; no framework refs).
- [x] **T008 [P] [FOUND]** `AuditObservability.Application.csproj`: Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts + `Microsoft.EntityFrameworkCore` (IQueryable seam) + `Microsoft.Extensions.Logging.Abstractions`.
- [x] **T009 [P] [FOUND]** `AuditObservability.Infrastructure.csproj`: EFCore + Npgsql + `AWSSDK.S3` (or `Minio` SDK if preferred) + WolverineFx + `Microsoft.AspNetCore.App` framework ref + ServiceDefaults + Domain + Application.
- [x] **T010 [P] [FOUND]** `AuditObservability.Api.csproj`: Infrastructure + Application + ServiceDefaults + Shared.CQRS + Shared.Kernel + `Microsoft.AspNetCore.OpenApi`.
- [x] **T011 [P] [FOUND]** Add the four `AuditObservability.*` projects + the new `AuditObservability.Domain.Tests` / `AuditObservability.Application.Tests` to `SmartSentinelEye.slnx`.
- [x] **T012 [FOUND]** Add `builder.AddAuditObservabilityPersistence();` to `MigrationRunner/Program.cs`.
- [x] **T013 [P] [FOUND]** Extend `src/ServiceDefaults/Authorization/Scope.cs` with `sse.audit.read` under a new nested `Audit` class. Update `Scope.All` to include it.
- [x] **T014 [P] [FOUND]** Realm import: add `sse.audit.read` to the spec 008 admin + operator bundles in `src/AppHost/Realms/smart-sentinel-eye-realm.json`. Document the change in the file's leading comment block.
- [x] **T015 [P] [FOUND]** `AuditChunkArchivedV1` in `src/Shared.Contracts/AuditObservability/AuditChunkArchivedV1.cs` (per plan's contract shape).
- [x] **T016 [P] [FOUND]** `tests/Shared.Contracts.Tests/AuditObservability/AuditChunkArchivedV1Tests.cs` — 4 tests (positional ctor, `IIntegrationEvent` marker, equality, JSON round-trip).
- [x] **T017 [P] [FOUND]** Extend `tests/ServiceDefaults.Tests/Authorization/ScopeTests.cs` with an assertion for `sse.audit.read`.

**Checkpoint:** `aspire run` brings up the TimescaleDB-extended Postgres + MinIO + `audit-observability` project resource (still empty, just a healthcheck). ADR-0101 + constitution amendment merged. Coverage gates unchanged (no new gated assemblies yet).

---

## Phase 2: User Story 1 — Compliance reviewer searches the audit trail (P1)

**Goal:** Bus subscriber writes audit rows for every `*V1`; `GET /audit` returns them, filtered by the caller's `groups` claim when `fabId` is omitted; per-resource timeline + single-row endpoints work.

**PRs B + C + D** land this story.

### Domain (PR B)

#### Value objects + entity tests first

- [x] **T018 [P] [US1]** `tests/AuditObservability.Domain.Tests/AuditEvent/AuditEventIdentifierTests.cs` — Guid v7 + strongly-typed wrapper + `IStronglyTypedId<Guid>` marker.
- [x] **T019 [P] [US1]** `EventIdentifierTests.cs` — non-zero Guid; rejects `Guid.Empty`.
- [x] **T020 [P] [US1]** `EventKindTests.cs` — non-empty, max 100 chars, allowed pattern `^[A-Za-z][A-Za-z0-9]*$`, equality.
- [x] **T021 [P] [US1]** `ResourceKindTests.cs` — closed VO over the FR-009 vocabulary `(camera | stream | layout | overlay | variable | rule | event | webhook | device | kiosk | webhook-integration)`. Unknown strings fail `From(string)`.
- [x] **T022 [P] [US1]** `ResourceIdentifierTests.cs` — non-empty, max 255 chars, equality.
- [x] **T023 [P] [US1]** `ActorIdentifierTests.cs` — accepts any Guid; `System` singleton returns `Guid.Empty` wrapper; preserves equality.
- [x] **T024 [P] [US1]** `AuditEventTests.cs` (entity-level) — `AuditEvent.From(integrationEvent, envelope, mapping, clock)` factory: pulls `OccurredAt` from the envelope, stamps `ReceivedAt` from `IClock`, derives `EventKind` from `typeof(T).Name`, applies the optional `ResourceKind` + `ResourceIdentifier` from the mapping, serialises the payload via `JsonSerializer.Serialize` (configured options), sets `PayloadSizeBytes` to the UTF-8 byte count, `SchemaVersion = 1`.

#### Implementation

- [x] **T025 [P] [US1]** `AuditEventIdentifier` in `src/AuditObservability/Domain/AuditEvent/AuditEventIdentifier.cs` — `IStronglyTypedId<Guid>` wrapper with `New()` returning Guid v7.
- [x] **T026 [P] [US1]** `EventIdentifier` VO + `EventKind` VO + `ResourceKind` VO + `ResourceIdentifier` VO + `ActorIdentifier` VO (with `System` static).
- [x] **T027 [US1]** `AuditEvent` entity in `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs` with all FR-004 fields + a private constructor + the `From(...)` factory.
- [x] **T028 [P] [US1]** `IAuditEventRepository` interface — `Add(AuditEvent audit)`, `Task SaveAsync(CancellationToken)`. Reads go through query handlers; no Get methods.

### Application — V1ResourceMap, subscriber, query handlers (PR C)

#### Tests first

- [x] **T029 [P] [US1]** `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` — convention scanner picks up every `*V1` in `Shared.Contracts`; namespace-leaf becomes `ResourceKind`; identifier picker resolves the first allow-listed property (`Identifier`, `<X>Identifier`, `Name`); unmatched events return `None`.
- [x] **T030 [P] [US1]** `AuditingMessageHandlerTests.cs` — happy path: a `RuleCreatedV1` payload + envelope → one `AuditEvent` added with the expected derived fields. Idempotency path: same `event_identifier` re-handled → repo's `Add` called twice but `SaveAsync`'s ON CONFLICT swallows the duplicate (asserted via the in-memory repo's row count). Unknown V1 path: an unmapped event is still stored with null `ResourceKind` / `ResourceIdentifier` + the unmapped-kind counter ticks.
- [x] **T031 [P] [US1]** `SearchAuditQueryHandlerTests.cs` — happy path with the FR-008 filter grid (actor + event-kind + since/until); cursor pagination round-trip (page 1 + nextCursor → page 2 starts where page 1 left off, no overlap); empty result returns an empty list (not an error).
- [x] **T032 [P] [US1]** `GetResourceTimelineQueryHandlerTests.cs` — three lifecycle events for one overlay → returns three rows ascending by `OccurredAt`; unrelated events for other overlays are excluded; `since` between events 2 and 3 returns only event 3.
- [x] **T033 [P] [US1]** `GetAuditEventQueryHandlerTests.cs` — happy path returns the full row + payload string; unknown identifier returns `AuditEventNotFound`.
- [x] **T034 [P] [US1]** `InMemoryAuditEventRepository` + `FakeBus` + `FakeClock` fakes under `tests/AuditObservability.Application.Tests/Fakes/`.

#### Implementation

- [x] **T035 [US1]** `V1ResourceMap` static class in `src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs` — at module-init time, scan `typeof(IIntegrationEvent).Assembly` for concrete `IIntegrationEvent` implementations and build a `FrozenDictionary<Type, ResourceMappingEntry>`. Convention-first; hand-tweaks in a sibling `V1ResourceMap.Conventions.cs`.
- [x] **T036 [P] [US1]** `AuditingMessageHandler` open-generic in `src/AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs` — public `Task Handle(IIntegrationEvent message, Envelope envelope, CancellationToken)`. Builds the row, calls `repo.Add` + `repo.SaveAsync`.
- [x] **T037 [P] [US1]** `SearchAuditQuery` + `SearchAuditError` (sealed-record hierarchy: `InvalidCursor`, `InvalidFilter`) in `src/AuditObservability/Application/Queries/`.
- [x] **T038 [P] [US1]** `GetResourceTimelineQuery` + `GetResourceTimelineError` (`UnknownResourceKind`, `InvalidCursor`).
- [x] **T039 [P] [US1]** `GetAuditEventQuery` + `GetAuditEventError` (`AuditEventNotFound`).
- [x] **T040 [P] [US1]** `AuditRowDto` (one row) + `AuditPageDto(IReadOnlyList<AuditRowDto> Rows, string? NextCursor)`.
- [x] **T041 [P] [US1]** `SearchAuditQueryHandler` — composes IQueryable from the filters, applies the cursor predicate (`occurred_at < cursor.OccurredAt OR (occurred_at = cursor.OccurredAt AND audit_id < cursor.AuditId)`), `.Take(pageSize + 1)`, returns the page.
- [x] **T042 [P] [US1]** `GetResourceTimelineQueryHandler` — same cursor mechanic, ascending order.
- [x] **T043 [P] [US1]** `GetAuditEventQueryHandler` — single row by primary key.

### Infrastructure + API (PR D)

#### Persistence

- [x] **T044 [US1]** `AuditObservabilityDbContext` in `src/AuditObservability/Infrastructure/Persistence/`. Single `DbSet<AuditEvent>`.
- [x] **T045 [US1]** `AuditEventConfiguration` — table `audit_events`, column map, **no** unique constraint on `Id` beyond PK, the EF model knows nothing about Timescale-specific catalog tables (they're created via raw SQL in the migration).
- [x] **T046 [US1]** Initial EF migration via `dotnet ef migrations add InitialAuditObservability`. Manually augment the generated `Up` with the raw SQL from plan.md (`CREATE EXTENSION timescaledb`, `SELECT create_hypertable(...)`, the three btree indexes, the unique index on `event_identifier`, the compression policy). `Down` drops everything in reverse.
- [x] **T047 [P] [US1]** `AuditEventRepository` — `Add` enqueues, `SaveAsync` runs `INSERT ... ON CONFLICT (event_identifier) DO NOTHING` via raw SQL (EF Core 9's `ExecuteSqlRawAsync` or `OnConflict` extension if available).
- [x] **T048 [P] [US1]** `DesignTimeDbContextFactory` for `dotnet ef` tooling.
- [x] **T049 [P] [US1]** `AuditObservabilityMigrator` implementing `IMigrator` per ADR-0067 (one method: `Task RunAsync(...)`).
- [x] **T050 [P] [US1]** `AuditObservabilityPersistenceModule.AddAuditObservabilityPersistence(IHostApplicationBuilder)` for MigrationRunner + the API.

#### Infrastructure composition

- [x] **T051 [US1]** `AuditObservabilityInfrastructureModule.AddAuditObservabilityInfrastructure` — registers `IAuditEventRepository → AuditEventRepository`, `IClock → SystemClock`, `IEventBus → WolverineEventBus`, the three query handlers, Wolverine's `AddWolverineForContext` with `IAuditEventRepository`'s DbContext + the `audit` queue prefix.

#### API

- [x] **T052 [US1]** `AuditEndpoints` in `src/AuditObservability/Api/AuditEndpoints.cs`. Three routes:
  - `GET /audit` (query params: `fabId?`, `actor?`, `actorUsername?`, `eventKind?`, `resourceKind?`, `resourceIdentifier?`, `since?`, `until?`, `pageSize?`, `cursor?`). Required scope `sse.audit.read`. When `fabId` is supplied, runs `IFabAuthorizationGuard`. When omitted, narrows the IQueryable to the caller's `groups` set.
  - `GET /audit/{resourceKind}/{resourceIdentifier}` (query: `fabId` required, plus `since?`, `until?`, `pageSize?`, `cursor?`). Same scope + fab guard.
  - `GET /audit/{auditIdentifier}` (single-row). Scope `sse.audit.read`; fab guard runs against the row's stored `fab_id`.
- [x] **T053 [P] [US1]** `AuditObservabilityApiModule.AddAuditObservabilityApi` (per-context API composition extension, ADR-0051; thin in v1).
- [x] **T054 [US1]** `Program.cs`: `AddServiceDefaults` + `AddBearerAuthentication` + `AddAuditObservabilityInfrastructure` + `AddAuditObservabilityApi` + `MapAuditEndpoints` + `UseExceptionHandler` (picks up `FabAuthorizationException → 403` from spec 008).

#### Wire-in

- [x] **T055 [US1]** Integration test `tests/Integration.Tests/AuditObservability/EndToEndIngestionIntegrationTests.cs` — publish a `CameraRegisteredV1` via the test producer, wait for the subscriber to drain, `GET /audit?eventKind=CameraRegisteredV1` from the admin client, assert exactly one row with the expected `event_kind`, `resource_kind = "camera"`, `resource_identifier = <cameraIdentifier>`.

**Checkpoint:** PRs B + C + D merged in order. Domain coverage ≥ 90 % asserted; Application coverage ≥ 80 %. Integration test demonstrates end-to-end audit via the bus.

---

## Phase 3: User Story 2 — Operator pivots from an alert to "who touched this overlay?" (P2)

**Goal:** Per-resource timeline lands in management-web; operator role gets `sse.audit.read`.

**PR E** lands this story.

### Tests first

- [x] **T056 [P] [US2]** `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` — single-fab operator hits `GET /audit/overlay/<id>?fabId=munich` for an `overlay` in munich → 200. Same operator hits `?fabId=berlin` → 403 `RESOURCE_FAB_NOT_AUTHORIZED`. `GET /audit` (no fabId) returns only munich rows.

### Frontend

- [x] **T057 [P] [US2]** `apps/shared/src/api/audit.ts` — RTK Query slice with `searchAudit`, `getResourceTimeline`, `getAuditEvent` endpoints (mirrors the auto-generated client when applicable).
- [x] **T058 [P] [US2]** `apps/management-web/src/pages/AuditPage.tsx` — new top-nav page: filter form (actor, kind, since/until, fab), virtualised result table (`DataTable` composite from the design system), per-row expand showing the JSON payload (read-only).
- [x] **T059 [P] [US2]** `AuditPage.test.tsx` — empty state, populated list, filter-applied state, row-expand state. Mocks the RTK Query slice.
- [x] **T060 [US2]** `apps/management-web/src/routes.tsx` — register `/audit` route + nav entry. Route guarded by an `sse.audit.read` check (same pattern as other admin-only routes).

**Checkpoint:** management-web `pnpm test` + a manual `aspire run` boots the stack; signing in as admin lands a working Audit page with live data.

---

## Phase 4: User Story 3 — Old chunks ride to MinIO automatically (P3)

**Goal:** A back-dated chunk → export to MinIO → drop via `drop_chunks` → `AuditChunkArchivedV1` on the bus; idempotent on restart.

**PR E** (back end of the same PR) — the worker, MinIO archiver, and integration test ship alongside the web page work.

### Application

- [x] **T061 [P] [US3]** `IAuditChunkArchiver` interface in `src/AuditObservability/Application/Retention/` with `Task<ChunkArchiveResult> ArchiveChunkAsync(ChunkArchiveRequest, CancellationToken)`.
- [x] **T062 [P] [US3]** `AuditRetentionHostedService` — `IHostedService` that runs once on startup + then on a daily timer (configurable). Uses `TimeProvider` + a `PeriodicTimer` so tests can advance the clock. Algorithm per plan.md (look up candidate chunks, archive each, publish V1, drop).
- [x] **T063 [P] [US3]** `tests/AuditObservability.Application.Tests/Retention/AuditRetentionHostedServiceTests.cs` — happy path: seeded chunks past threshold → archiver called once per chunk; idempotency: archiver replays for a previously-archived chunk → existing-object check skips upload, drop proceeds; failure: archiver throws → chunk stays, error logged, next run retries.

### Infrastructure

- [x] **T064 [P] [US3]** `MinioOptions` configuration record (endpoint, accessKey, secretKey, bucket).
- [x] **T065 [US3]** `MinioAuditChunkArchiver` — production `IAuditChunkArchiver` impl backed by `AWSSDK.S3` (S3-compatible Minio endpoint). Streams gzipped NDJSON; computes `Content-MD5` pre-upload; verifies via `HeadObject` ETag post-upload.
- [x] **T066 [US3]** Wire `IAuditChunkArchiver → MinioAuditChunkArchiver` + the hosted service in `AuditObservabilityInfrastructureModule`.

### Integration

- [x] **T067 [US3]** `tests/Integration.Tests/AuditObservability/RetentionRoundtripIntegrationTests.cs` — uses `TimeProvider` to back-date a chunk past 90 days, triggers the hosted service via a dev-only `IHostedService` `RunOnceAsync` seam (or by reflection — pick the cleaner option in implementation), asserts: chunk is gone from the hypertable (`show_chunks(audit_events)` doesn't list it), MinIO bucket has the expected object with matching row count + ETag, `AuditChunkArchivedV1` is on the bus.

**Checkpoint:** PR E covers both US2 (web page) and US3 (retention). Architecture tests stay green.

---

## Phase 5: Polish — coverage gates, architecture tests, README, NFR

**PR F** lands this phase.

- [x] **T068 [POLISH]** Extend `scripts/coverage-check.ps1` with `AuditObservability.Domain >= 90` and `AuditObservability.Application >= 80`.
- [x] **T069 [P] [POLISH]** Extend `tests/Architecture.Tests/BoundaryTests.cs`:
  - Positive: `AuditObservability.Domain` has zero framework dependencies (no EF Core, Wolverine, SignalR, Npgsql, MQTTnet).
  - Positive: `AuditObservability.Application` references `Shared.Contracts` only — no other context's Domain or Application assemblies.
- [x] **T070 [P] [POLISH]** Extend `BoundaryTests.cs` with a `V1ResourceMap_covers_every_IIntegrationEvent` test that iterates `typeof(IIntegrationEvent).Assembly` and asserts every concrete implementor is either mapped or explicitly opted out via a `[NoAuditMapping]` attribute. Forces the mapping registry to stay in sync as new V1s land.
- [x] **T071 [P] [POLISH]** README quickstart "Audit who-did-what": admin signs into management-web, publishes a layout, then opens the new Audit page and finds the `LayoutRevisionPublishedV1` row + clicks "timeline for this resource".
- [ ] **T072 [P] [POLISH]** `NFR001_AuditIngestLatencyTests` (Aspire-fixture-based, matches the spec 008 NFR-001 pattern). Warm 100 events, measure 1 000, assert p99 ≤ 50 ms from publish to row committed.
- [x] **T073 [P] [POLISH]** `NFR002_AuditSearchLatencyTests` (Aspire-fixture-based). Seed the hypertable with 100 k rows (one month's worth at the 100 ev/s target), run a `GET /audit?since=24h&pageSize=50` 1 000-iteration warm + measure loop, assert p99 ≤ 200 ms.
- [x] **T074 [P] [POLISH]** Document the new `audit-db` resource + MinIO bucket in `docs/runbooks/audit-observability.md` (new file). Covers: where the hypertable lives, how to inspect chunks, how to manually trigger the retention worker, how to read an archived NDJSON object.

**Checkpoint:** Coverage gates pass; NFR-001 + NFR-002 land in CI; README + runbook describe the operational shape.

---

## Dependency graph (visual)

```
Phase 1 (Aspire + Scope + V1 contract + ADR-0101)
   │
   ▼
Phase 2 (US1 — Reviewer search)
   ├── Domain (PR B) → Application (PR C) → Infrastructure + API (PR D)
   │
   ▼
Phase 3 (US2 — Operator pivot, management-web)
   │
   ▼
Phase 4 (US3 — Retention archive)
   │
   ▼
Phase 5 (Polish + NFR + README + runbook)
```

## Parallelisation strategy

- **Within Phase 1**: every `[P]` task is independent of every other (different files, no shared types). Realistic concurrency ≈ 12 tasks at once.
- **Within Phase 2**: VO test files + impls (T018-T028) are fully parallel. The handler stack (T035-T043) has linear chains (e.g. the query handlers depend on the DTOs but not on each other).
- **Phase 5**: every task touches a different file; full concurrency.

## PR mapping

| PR | Phase coverage | Task IDs |
|---|---|---|
| A — scaffold + ADR-0101 + V1 contract + Scope addition | Phase 1 | T001–T017 |
| B — Domain (AuditEvent + VOs) | Phase 2 (Domain) | T018–T028 |
| C — Application (subscriber + queries + retention service shell) | Phase 2 (Application) | T029–T043, T061–T063 |
| D — Infrastructure + read API + persistence migration + end-to-end test | Phase 2 (Infra+API) | T044–T055 |
| E — management-web Audit page + retention infra + retention integration test | Phases 3 + 4 | T056–T060, T064–T067 |
| F — coverage gates + arch tests + README + NFR | Phase 5 | T068–T074 |

## Gate (Phase 3 → Phase 4)

This task list is ready for the Implement phase once the architect lead confirms:

1. **Task atomicity** — no task hides ≥ ½ day of work; subdivide further if needed.
2. **PR-to-task mapping** matches the team's review cadence (~10–15 tasks per PR; PR C is the largest at ~15 + retention shell).
3. **GitHub issues** can be created from these (Phase 3.5 work via `/speckit-taskstoissues`).

---

## T072 — the test is written; the task stays open, 2026-08-28

`tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs`
exists, runs, and **does not pass**. It is tagged `[Trait("Category", "Measurement")]`
so CI excludes it, and T072 stays unticked, because what it measures is nowhere
near what NFR-001 asks for:

| Events | p50 | p99 | max |
|---|---|---|---|
| 1 000 (~20 ev/s) | **4 800 ms** | **9 469 ms** | 9 586 ms |
| 100 | 4 624 ms | 5 037 ms | 5 045 ms |

against NFR-001's **p99 ≤ 50 ms at a sustained 100 ev/s**. Latency *grows*
through the run, so the consumer drains slower than the writes arrive — at about
a fifth of the specified rate. Filed as **1956** (no hash: a mention would close
it on merge).

**Two deliberate choices, so neither reads as an oversight.**

**The budget stays at 50 ms.** Moving it to whatever the fixture produces would
report the requirement as met when it is not. The test fails honestly.

**Excluding it from CI deviates from this phase's checkpoint** — "NFR-001 +
NFR-002 land in CI". It is recorded here rather than taken quietly. NFR-002
(T073) does run in CI and passes; the precedent for excluding a measurement is
`IngestThroughputMeasurementTests`.

**What the generator is, and why.** Repeated variable value sets, publishing
`SystemVariableValueChangedV1`, whose `Metadata.OccurredAt` is stamped as the
aggregate mutates. A plant-floor event was the obvious alternative and the wrong
one: `FabEventIngestedV1` carries the *device's* timestamp, so it measures the
whole MQTT chain — p50 30 ms / p99 63 ms on a dev stack, against p50 10 ms /
p99 24 ms for events stamped at publish. The same budget, the wrong leg.

**Run mode under load, measured 2026-08-28.** This was the open question — run-mode
spot checks put the same event kind at 13–33 ms, but at roughly one write per
second, which is not a comparison at load. It has now been taken, driving the
same generator against the run-mode stack at several sustained rates (each rate
run twice; the pairs agree):

| achieved | p50 | p99 | max |
|---|---|---|---|
| 0.6 ev/s | 15 ms | — | 901 ms |
| 24 ev/s | 31 ms | 142 ms | 203 ms |
| 48–49 ev/s | 37 ms | 258–280 ms | 332 ms |
| 68 ev/s | 52 ms | 342 ms | 393 ms |
| 86–95 ev/s | 3 066–4 936 ms | 6 350–6 730 ms | 6 774 ms |
| 158 ev/s | 9 870 ms | 14 221 ms | 14 288 ms |

**The gap is not a fixture artefact.** Run mode misses NFR-001 too, and at the
rate the NFR names it misses by two orders of magnitude. It also misses at every
rate measured, near-idle included: p99 141 ms at 24 ev/s is already ~3× the
budget, and the only regime where 50 ms holds is one where essentially nothing is
happening.

**Where the seconds go: the consumer, not the outbox.** Sampled through a
158 ev/s burst, `wolverine_system_variables.wolverine_outgoing_envelopes` held
**0 rows at every sample** — messages leave the publisher immediately — while the
audit queue on RabbitMQ backed up to 468 then 643. So the flush cadence is not
the mechanism, and the "ceiling just above 5 s looks like a polling interval"
guess in 1956 is **wrong**: the run-mode latency histogram is a smooth queueing
tail, not a spike at multiples of five seconds.

The consumer's ceiling is **~100 rows/s for that queue** (peak seconds observed:
102, 101, 98). NFR-001 asks for 100 ev/s sustained, so the requirement sits
exactly on the measured ceiling with no headroom — which is why latency is stable
to ~68 ev/s and collapses by ~86. Per message the audit side does a durable-inbox
write (`wolverine_audit.wolverine_incoming_envelopes`, which had accumulated
5 343 rows) plus the audit row's own `SaveAsync` — one transaction per row — on a
single listener at prefetch 100.

**Parallel listeners taken (ADR-0124), and NFR-001 is still not met.** The audit
queues now run four listeners each. Re-measured the same way:

| achieved | p50 (1 listener) | p50 (4) | p99 (1 listener) | p99 (4) |
|---|---|---|---|---|
| ~30 ev/s | 31 ms | 10 ms | 142 ms | 58 ms |
| ~50–58 ev/s | 37 ms | 15 ms | 258–280 ms | 62 ms |
| ~85–115 ev/s | 3 066–4 936 ms | 34–66 ms typical | 6 350–6 730 ms | 214–420 ms typical |

Peak drain 100 → 270 rows/s; the knee moved from ~75 ev/s to past 110. Two of six
runs at ~100 ev/s spiked to p99 2 119 / 3 786 ms — that rate sits close enough to
the new knee that the outcome depends on what else the box is doing, and the
scatter is reported rather than averaged away.

**So T072 stays unticked and the test stays out of CI.** p99 ≤ 50 ms is not held
even at 30 ev/s (58 ms), and at 100 ev/s the typical p99 is 214–420 ms. The gap
is now ~5× where it was ~130×, which is a different conversation but not a
finished one.

Two things came out of the re-measurement that are not about listeners:

- **The shared Postgres was at 97 of its 100 connections before any load.** The
  first re-measurement looked like the change had barely helped (p50 6 458 ms)
  because the cluster ran out of connections under load — and the service that
  failed was **system-variables**, not audit. `AppHost` now starts Postgres with
  `max_connections=400`.
- **Eight listeners is worse than four**, for the same reason: `audit-db` alone
  took 22 connections and pushed the cluster past its limit.

**Still open on 1956.** Whether the remaining gap closes via the other lever
(dropping the durable inbox, where the audit row's own `event_identifier`
conflict already gives idempotency), via production topology — audit gets its own
pod and database node, which this measurement did not — or by moving NFR-001.
