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

- [ ] **T001 [FOUND]** Draft **ADR-0101** `docs/adr/0101-timescaledb-for-audit.md` (already drafted from Phase 2 — verify the version in PR A matches the plan exactly; mark `Status: Accepted` at merge time).
- [ ] **T002 [P] [FOUND]** Constitution amendment: add the TimescaleDB line to `.specify/memory/constitution.md` § Backend (already drafted; verify in PR A).
- [ ] **T003 [P] [FOUND]** Bump the AppHost `postgres` image to `timescale/timescaledb-ha:pg17-oss` via `.WithImageTag("pg17-oss")` (or the equivalent `WithImage(...)` call so the registry path is explicit).
- [ ] **T004 [FOUND]** Add `audit-db` database resource: `var auditDb = postgres.AddDatabase("audit-db");` + wire into `migrations`.
- [ ] **T005 [P] [FOUND]** Add MinIO Aspire container: `builder.AddMinio("minio")` (or `AddContainer("minio", "minio/minio", "RELEASE.2025-XX-XX")` if no native extension) + persistent volume in run mode + dev seed bucket `audit-archive`.
- [ ] **T006 [P] [FOUND]** Wire the `audit-observability` API project in `AppHost.cs`: `WithHttpEndpoint().WithReference(auditDb).WithReference(rabbitmq).WithReference(keycloak).WithReference(minio).WaitForCompletion(migrations).WaitFor(rabbitmq).WaitFor(keycloak).WaitFor(minio)`.
- [ ] **T007 [P] [FOUND]** `AuditObservability.Domain.csproj` mirrors Identity.Domain shape (Shared.Kernel only; no framework refs).
- [ ] **T008 [P] [FOUND]** `AuditObservability.Application.csproj`: Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts + `Microsoft.EntityFrameworkCore` (IQueryable seam) + `Microsoft.Extensions.Logging.Abstractions`.
- [ ] **T009 [P] [FOUND]** `AuditObservability.Infrastructure.csproj`: EFCore + Npgsql + `AWSSDK.S3` (or `Minio` SDK if preferred) + WolverineFx + `Microsoft.AspNetCore.App` framework ref + ServiceDefaults + Domain + Application.
- [ ] **T010 [P] [FOUND]** `AuditObservability.Api.csproj`: Infrastructure + Application + ServiceDefaults + Shared.CQRS + Shared.Kernel + `Microsoft.AspNetCore.OpenApi`.
- [ ] **T011 [P] [FOUND]** Add the four `AuditObservability.*` projects + the new `AuditObservability.Domain.Tests` / `AuditObservability.Application.Tests` to `SmartSentinelEye.slnx`.
- [ ] **T012 [FOUND]** Add `builder.AddAuditObservabilityPersistence();` to `MigrationRunner/Program.cs`.
- [ ] **T013 [P] [FOUND]** Extend `src/ServiceDefaults/Authorization/Scope.cs` with `sse.audit.read` under a new nested `Audit` class. Update `Scope.All` to include it.
- [ ] **T014 [P] [FOUND]** Realm import: add `sse.audit.read` to the spec 008 admin + operator bundles in `src/AppHost/Realms/smart-sentinel-eye-realm.json`. Document the change in the file's leading comment block.
- [ ] **T015 [P] [FOUND]** `AuditChunkArchivedV1` in `src/Shared.Contracts/AuditObservability/AuditChunkArchivedV1.cs` (per plan's contract shape).
- [ ] **T016 [P] [FOUND]** `tests/Shared.Contracts.Tests/AuditObservability/AuditChunkArchivedV1Tests.cs` — 4 tests (positional ctor, `IIntegrationEvent` marker, equality, JSON round-trip).
- [ ] **T017 [P] [FOUND]** Extend `tests/ServiceDefaults.Tests/Authorization/ScopeTests.cs` with an assertion for `sse.audit.read`.

**Checkpoint:** `aspire run` brings up the TimescaleDB-extended Postgres + MinIO + `audit-observability` project resource (still empty, just a healthcheck). ADR-0101 + constitution amendment merged. Coverage gates unchanged (no new gated assemblies yet).

---

## Phase 2: User Story 1 — Compliance reviewer searches the audit trail (P1)

**Goal:** Bus subscriber writes audit rows for every `*V1`; `GET /audit` returns them, filtered by the caller's `groups` claim when `fabId` is omitted; per-resource timeline + single-row endpoints work.

**PRs B + C + D** land this story.

### Domain (PR B)

#### Value objects + entity tests first

- [ ] **T018 [P] [US1]** `tests/AuditObservability.Domain.Tests/AuditEvent/AuditEventIdentifierTests.cs` — Guid v7 + strongly-typed wrapper + `IStronglyTypedId<Guid>` marker.
- [ ] **T019 [P] [US1]** `EventIdentifierTests.cs` — non-zero Guid; rejects `Guid.Empty`.
- [ ] **T020 [P] [US1]** `EventKindTests.cs` — non-empty, max 100 chars, allowed pattern `^[A-Za-z][A-Za-z0-9]*$`, equality.
- [ ] **T021 [P] [US1]** `ResourceKindTests.cs` — closed VO over the FR-009 vocabulary `(camera | stream | layout | overlay | variable | rule | event | webhook | device | kiosk | webhook-integration)`. Unknown strings fail `From(string)`.
- [ ] **T022 [P] [US1]** `ResourceIdentifierTests.cs` — non-empty, max 255 chars, equality.
- [ ] **T023 [P] [US1]** `ActorIdentifierTests.cs` — accepts any Guid; `System` singleton returns `Guid.Empty` wrapper; preserves equality.
- [ ] **T024 [P] [US1]** `AuditEventTests.cs` (entity-level) — `AuditEvent.From(integrationEvent, envelope, mapping, clock)` factory: pulls `OccurredAt` from the envelope, stamps `ReceivedAt` from `IClock`, derives `EventKind` from `typeof(T).Name`, applies the optional `ResourceKind` + `ResourceIdentifier` from the mapping, serialises the payload via `JsonSerializer.Serialize` (configured options), sets `PayloadSizeBytes` to the UTF-8 byte count, `SchemaVersion = 1`.

#### Implementation

- [ ] **T025 [P] [US1]** `AuditEventIdentifier` in `src/AuditObservability/Domain/AuditEvent/AuditEventIdentifier.cs` — `IStronglyTypedId<Guid>` wrapper with `New()` returning Guid v7.
- [ ] **T026 [P] [US1]** `EventIdentifier` VO + `EventKind` VO + `ResourceKind` VO + `ResourceIdentifier` VO + `ActorIdentifier` VO (with `System` static).
- [ ] **T027 [US1]** `AuditEvent` entity in `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs` with all FR-004 fields + a private constructor + the `From(...)` factory.
- [ ] **T028 [P] [US1]** `IAuditEventRepository` interface — `Add(AuditEvent audit)`, `Task SaveAsync(CancellationToken)`. Reads go through query handlers; no Get methods.

### Application — V1ResourceMap, subscriber, query handlers (PR C)

#### Tests first

- [ ] **T029 [P] [US1]** `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` — convention scanner picks up every `*V1` in `Shared.Contracts`; namespace-leaf becomes `ResourceKind`; identifier picker resolves the first allow-listed property (`Identifier`, `<X>Identifier`, `Name`); unmatched events return `None`.
- [ ] **T030 [P] [US1]** `AuditingMessageHandlerTests.cs` — happy path: a `RuleCreatedV1` payload + envelope → one `AuditEvent` added with the expected derived fields. Idempotency path: same `event_identifier` re-handled → repo's `Add` called twice but `SaveAsync`'s ON CONFLICT swallows the duplicate (asserted via the in-memory repo's row count). Unknown V1 path: an unmapped event is still stored with null `ResourceKind` / `ResourceIdentifier` + the unmapped-kind counter ticks.
- [ ] **T031 [P] [US1]** `SearchAuditQueryHandlerTests.cs` — happy path with the FR-008 filter grid (actor + event-kind + since/until); cursor pagination round-trip (page 1 + nextCursor → page 2 starts where page 1 left off, no overlap); empty result returns an empty list (not an error).
- [ ] **T032 [P] [US1]** `GetResourceTimelineQueryHandlerTests.cs` — three lifecycle events for one overlay → returns three rows ascending by `OccurredAt`; unrelated events for other overlays are excluded; `since` between events 2 and 3 returns only event 3.
- [ ] **T033 [P] [US1]** `GetAuditEventQueryHandlerTests.cs` — happy path returns the full row + payload string; unknown identifier returns `AuditEventNotFound`.
- [ ] **T034 [P] [US1]** `InMemoryAuditEventRepository` + `FakeBus` + `FakeClock` fakes under `tests/AuditObservability.Application.Tests/Fakes/`.

#### Implementation

- [ ] **T035 [US1]** `V1ResourceMap` static class in `src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs` — at module-init time, scan `typeof(IIntegrationEvent).Assembly` for concrete `IIntegrationEvent` implementations and build a `FrozenDictionary<Type, ResourceMappingEntry>`. Convention-first; hand-tweaks in a sibling `V1ResourceMap.Conventions.cs`.
- [ ] **T036 [P] [US1]** `AuditingMessageHandler` open-generic in `src/AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs` — public `Task Handle(IIntegrationEvent message, Envelope envelope, CancellationToken)`. Builds the row, calls `repo.Add` + `repo.SaveAsync`.
- [ ] **T037 [P] [US1]** `SearchAuditQuery` + `SearchAuditError` (sealed-record hierarchy: `InvalidCursor`, `InvalidFilter`) in `src/AuditObservability/Application/Queries/`.
- [ ] **T038 [P] [US1]** `GetResourceTimelineQuery` + `GetResourceTimelineError` (`UnknownResourceKind`, `InvalidCursor`).
- [ ] **T039 [P] [US1]** `GetAuditEventQuery` + `GetAuditEventError` (`AuditEventNotFound`).
- [ ] **T040 [P] [US1]** `AuditRowDto` (one row) + `AuditPageDto(IReadOnlyList<AuditRowDto> Rows, string? NextCursor)`.
- [ ] **T041 [P] [US1]** `SearchAuditQueryHandler` — composes IQueryable from the filters, applies the cursor predicate (`occurred_at < cursor.OccurredAt OR (occurred_at = cursor.OccurredAt AND audit_id < cursor.AuditId)`), `.Take(pageSize + 1)`, returns the page.
- [ ] **T042 [P] [US1]** `GetResourceTimelineQueryHandler` — same cursor mechanic, ascending order.
- [ ] **T043 [P] [US1]** `GetAuditEventQueryHandler` — single row by primary key.

### Infrastructure + API (PR D)

#### Persistence

- [ ] **T044 [US1]** `AuditObservabilityDbContext` in `src/AuditObservability/Infrastructure/Persistence/`. Single `DbSet<AuditEvent>`.
- [ ] **T045 [US1]** `AuditEventConfiguration` — table `audit_events`, column map, **no** unique constraint on `Id` beyond PK, the EF model knows nothing about Timescale-specific catalog tables (they're created via raw SQL in the migration).
- [ ] **T046 [US1]** Initial EF migration via `dotnet ef migrations add InitialAuditObservability`. Manually augment the generated `Up` with the raw SQL from plan.md (`CREATE EXTENSION timescaledb`, `SELECT create_hypertable(...)`, the three btree indexes, the unique index on `event_identifier`, the compression policy). `Down` drops everything in reverse.
- [ ] **T047 [P] [US1]** `AuditEventRepository` — `Add` enqueues, `SaveAsync` runs `INSERT ... ON CONFLICT (event_identifier) DO NOTHING` via raw SQL (EF Core 9's `ExecuteSqlRawAsync` or `OnConflict` extension if available).
- [ ] **T048 [P] [US1]** `DesignTimeDbContextFactory` for `dotnet ef` tooling.
- [ ] **T049 [P] [US1]** `AuditObservabilityMigrator` implementing `IMigrator` per ADR-0067 (one method: `Task RunAsync(...)`).
- [ ] **T050 [P] [US1]** `AuditObservabilityPersistenceModule.AddAuditObservabilityPersistence(IHostApplicationBuilder)` for MigrationRunner + the API.

#### Infrastructure composition

- [ ] **T051 [US1]** `AuditObservabilityInfrastructureModule.AddAuditObservabilityInfrastructure` — registers `IAuditEventRepository → AuditEventRepository`, `IClock → SystemClock`, `IEventBus → WolverineEventBus`, the three query handlers, Wolverine's `AddWolverineForContext` with `IAuditEventRepository`'s DbContext + the `audit` queue prefix.

#### API

- [ ] **T052 [US1]** `AuditEndpoints` in `src/AuditObservability/Api/AuditEndpoints.cs`. Three routes:
  - `GET /audit` (query params: `fabId?`, `actor?`, `actorUsername?`, `eventKind?`, `resourceKind?`, `resourceIdentifier?`, `since?`, `until?`, `pageSize?`, `cursor?`). Required scope `sse.audit.read`. When `fabId` is supplied, runs `IFabAuthorizationGuard`. When omitted, narrows the IQueryable to the caller's `groups` set.
  - `GET /audit/{resourceKind}/{resourceIdentifier}` (query: `fabId` required, plus `since?`, `until?`, `pageSize?`, `cursor?`). Same scope + fab guard.
  - `GET /audit/{auditIdentifier}` (single-row). Scope `sse.audit.read`; fab guard runs against the row's stored `fab_id`.
- [ ] **T053 [P] [US1]** `AuditObservabilityApiModule.AddAuditObservabilityApi` (per-context API composition extension, ADR-0051; thin in v1).
- [ ] **T054 [US1]** `Program.cs`: `AddServiceDefaults` + `AddBearerAuthentication` + `AddAuditObservabilityInfrastructure` + `AddAuditObservabilityApi` + `MapAuditEndpoints` + `UseExceptionHandler` (picks up `FabAuthorizationException → 403` from spec 008).

#### Wire-in

- [ ] **T055 [US1]** Integration test `tests/Integration.Tests/AuditObservability/EndToEndIngestionIntegrationTests.cs` — publish a `CameraRegisteredV1` via the test producer, wait for the subscriber to drain, `GET /audit?eventKind=CameraRegisteredV1` from the admin client, assert exactly one row with the expected `event_kind`, `resource_kind = "camera"`, `resource_identifier = <cameraIdentifier>`.

**Checkpoint:** PRs B + C + D merged in order. Domain coverage ≥ 90 % asserted; Application coverage ≥ 80 %. Integration test demonstrates end-to-end audit via the bus.

---

## Phase 3: User Story 2 — Operator pivots from an alert to "who touched this overlay?" (P2)

**Goal:** Per-resource timeline lands in management-web; operator role gets `sse.audit.read`.

**PR E** lands this story.

### Tests first

- [ ] **T056 [P] [US2]** `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` — single-fab operator hits `GET /audit/overlay/<id>?fabId=munich` for an `overlay` in munich → 200. Same operator hits `?fabId=berlin` → 403 `RESOURCE_FAB_NOT_AUTHORIZED`. `GET /audit` (no fabId) returns only munich rows.

### Frontend

- [ ] **T057 [P] [US2]** `apps/shared/src/api/audit.ts` — RTK Query slice with `searchAudit`, `getResourceTimeline`, `getAuditEvent` endpoints (mirrors the auto-generated client when applicable).
- [ ] **T058 [P] [US2]** `apps/management-web/src/pages/AuditPage.tsx` — new top-nav page: filter form (actor, kind, since/until, fab), virtualised result table (`DataTable` composite from the design system), per-row expand showing the JSON payload (read-only).
- [ ] **T059 [P] [US2]** `AuditPage.test.tsx` — empty state, populated list, filter-applied state, row-expand state. Mocks the RTK Query slice.
- [ ] **T060 [US2]** `apps/management-web/src/routes.tsx` — register `/audit` route + nav entry. Route guarded by an `sse.audit.read` check (same pattern as other admin-only routes).

**Checkpoint:** management-web `pnpm test` + a manual `aspire run` boots the stack; signing in as admin lands a working Audit page with live data.

---

## Phase 4: User Story 3 — Old chunks ride to MinIO automatically (P3)

**Goal:** A back-dated chunk → export to MinIO → drop via `drop_chunks` → `AuditChunkArchivedV1` on the bus; idempotent on restart.

**PR E** (back end of the same PR) — the worker, MinIO archiver, and integration test ship alongside the web page work.

### Application

- [ ] **T061 [P] [US3]** `IAuditChunkArchiver` interface in `src/AuditObservability/Application/Retention/` with `Task<ChunkArchiveResult> ArchiveChunkAsync(ChunkArchiveRequest, CancellationToken)`.
- [ ] **T062 [P] [US3]** `AuditRetentionHostedService` — `IHostedService` that runs once on startup + then on a daily timer (configurable). Uses `TimeProvider` + a `PeriodicTimer` so tests can advance the clock. Algorithm per plan.md (look up candidate chunks, archive each, publish V1, drop).
- [ ] **T063 [P] [US3]** `tests/AuditObservability.Application.Tests/Retention/AuditRetentionHostedServiceTests.cs` — happy path: seeded chunks past threshold → archiver called once per chunk; idempotency: archiver replays for a previously-archived chunk → existing-object check skips upload, drop proceeds; failure: archiver throws → chunk stays, error logged, next run retries.

### Infrastructure

- [ ] **T064 [P] [US3]** `MinioOptions` configuration record (endpoint, accessKey, secretKey, bucket).
- [ ] **T065 [US3]** `MinioAuditChunkArchiver` — production `IAuditChunkArchiver` impl backed by `AWSSDK.S3` (S3-compatible Minio endpoint). Streams gzipped NDJSON; computes `Content-MD5` pre-upload; verifies via `HeadObject` ETag post-upload.
- [ ] **T066 [US3]** Wire `IAuditChunkArchiver → MinioAuditChunkArchiver` + the hosted service in `AuditObservabilityInfrastructureModule`.

### Integration

- [ ] **T067 [US3]** `tests/Integration.Tests/AuditObservability/RetentionRoundtripIntegrationTests.cs` — uses `TimeProvider` to back-date a chunk past 90 days, triggers the hosted service via a dev-only `IHostedService` `RunOnceAsync` seam (or by reflection — pick the cleaner option in implementation), asserts: chunk is gone from the hypertable (`show_chunks(audit_events)` doesn't list it), MinIO bucket has the expected object with matching row count + ETag, `AuditChunkArchivedV1` is on the bus.

**Checkpoint:** PR E covers both US2 (web page) and US3 (retention). Architecture tests stay green.

---

## Phase 5: Polish — coverage gates, architecture tests, README, NFR

**PR F** lands this phase.

- [ ] **T068 [POLISH]** Extend `scripts/coverage-check.ps1` with `AuditObservability.Domain >= 90` and `AuditObservability.Application >= 80`.
- [ ] **T069 [P] [POLISH]** Extend `tests/Architecture.Tests/BoundaryTests.cs`:
  - Positive: `AuditObservability.Domain` has zero framework dependencies (no EF Core, Wolverine, SignalR, Npgsql, MQTTnet).
  - Positive: `AuditObservability.Application` references `Shared.Contracts` only — no other context's Domain or Application assemblies.
- [ ] **T070 [P] [POLISH]** Extend `BoundaryTests.cs` with a `V1ResourceMap_covers_every_IIntegrationEvent` test that iterates `typeof(IIntegrationEvent).Assembly` and asserts every concrete implementor is either mapped or explicitly opted out via a `[NoAuditMapping]` attribute. Forces the mapping registry to stay in sync as new V1s land.
- [ ] **T071 [P] [POLISH]** README quickstart "Audit who-did-what": admin signs into management-web, publishes a layout, then opens the new Audit page and finds the `LayoutRevisionPublishedV1` row + clicks "timeline for this resource".
- [ ] **T072 [P] [POLISH]** `NFR001_AuditIngestLatencyTests` (Aspire-fixture-based, matches the spec 008 NFR-001 pattern). Warm 100 events, measure 1 000, assert p99 ≤ 50 ms from publish to row committed.
- [ ] **T073 [P] [POLISH]** `NFR002_AuditSearchLatencyTests` (Aspire-fixture-based). Seed the hypertable with 100 k rows (one month's worth at the 100 ev/s target), run a `GET /audit?since=24h&pageSize=50` 1 000-iteration warm + measure loop, assert p99 ≤ 200 ms.
- [ ] **T074 [P] [POLISH]** Document the new `audit-db` resource + MinIO bucket in `docs/runbooks/audit-observability.md` (new file). Covers: where the hypertable lives, how to inspect chunks, how to manually trigger the retention worker, how to read an archived NDJSON object.

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
