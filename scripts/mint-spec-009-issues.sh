#!/usr/bin/env bash
# One-shot Phase 3.5 helper: mints the 74 GitHub issues from spec 009 tasks.md.
# Run from repo root: bash scripts/mint-spec-009-issues.sh
# Each issue carries: task + feature:009-audit-observability + story:<bucket>.
# Title format `[Txxx] <description>` matches spec 008's convention.

set -euo pipefail

FEAT_LABEL="feature:009-audit-observability"

issue() {
    local id="$1"; local story="$2"; local title="$3"
    gh issue create \
        --title "[$id] $title" \
        --body "Task $id from \`specs/009-audit-observability/tasks.md\`. Story bucket: $story." \
        --label "task,$FEAT_LABEL,story:$story" >/dev/null
    echo "  $id created"
}

echo "=== Phase 1 — FOUND ==="
issue T001 found 'Draft ADR-0101 docs/adr/0101-timescaledb-for-audit.md; mark Status: Accepted at merge time.'
issue T002 found 'Constitution amendment: add the TimescaleDB line to .specify/memory/constitution.md § Backend.'
issue T003 found 'Bump the AppHost postgres image to timescale/timescaledb-ha:pg17-oss via WithImageTag.'
issue T004 found 'Add audit-db database resource: var auditDb = postgres.AddDatabase("audit-db") + wire into migrations.'
issue T005 found 'Add MinIO Aspire container + persistent volume in run mode + dev seed bucket audit-archive.'
issue T006 found 'Wire the audit-observability API project in AppHost.cs (WithHttpEndpoint + references + waits).'
issue T007 found 'AuditObservability.Domain.csproj (Shared.Kernel only; no framework refs).'
issue T008 found 'AuditObservability.Application.csproj (Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts + EFCore IQueryable seam + Logging).'
issue T009 found 'AuditObservability.Infrastructure.csproj (EFCore + Npgsql + AWSSDK.S3 + WolverineFx + ASP.NET App framework ref + ServiceDefaults).'
issue T010 found 'AuditObservability.Api.csproj (Infrastructure + Application + ServiceDefaults + Shared.* + Microsoft.AspNetCore.OpenApi).'
issue T011 found 'Add the four AuditObservability.* projects + AuditObservability.Domain.Tests / .Application.Tests to SmartSentinelEye.slnx.'
issue T012 found 'Add builder.AddAuditObservabilityPersistence() to MigrationRunner/Program.cs.'
issue T013 found 'Extend ServiceDefaults/Authorization/Scope.cs with sse.audit.read under a new nested Audit class; update Scope.All.'
issue T014 found 'Realm import: add sse.audit.read to the spec 008 admin + operator bundles in src/AppHost/Realms/smart-sentinel-eye-realm.json.'
issue T015 found 'AuditChunkArchivedV1 in src/Shared.Contracts/AuditObservability/AuditChunkArchivedV1.cs.'
issue T016 found 'tests/Shared.Contracts.Tests/AuditObservability/AuditChunkArchivedV1Tests.cs — 4 tests (positional ctor, IIntegrationEvent marker, equality, JSON round-trip).'
issue T017 found 'Extend tests/ServiceDefaults.Tests/Authorization/ScopeTests.cs with an assertion for sse.audit.read.'

echo "=== Phase 2 — US1 (Domain VOs + entity tests + impls) ==="
issue T018 us1 'AuditEventIdentifierTests — Guid v7 + IStronglyTypedId<Guid> marker.'
issue T019 us1 'EventIdentifierTests — non-zero Guid; rejects Guid.Empty.'
issue T020 us1 'EventKindTests — non-empty, max 100 chars, pattern ^[A-Za-z][A-Za-z0-9]*$, equality.'
issue T021 us1 'ResourceKindTests — closed VO over FR-009 vocabulary; unknown strings fail From(string).'
issue T022 us1 'ResourceIdentifierTests — non-empty, max 255 chars, equality.'
issue T023 us1 'ActorIdentifierTests — accepts any Guid; System singleton returns Guid.Empty wrapper.'
issue T024 us1 'AuditEventTests (entity-level) — AuditEvent.From(integrationEvent, envelope, mapping, clock) factory exercised against every field.'
issue T025 us1 'AuditEventIdentifier IStronglyTypedId<Guid> wrapper with New() returning Guid v7.'
issue T026 us1 'EventIdentifier VO + EventKind VO + ResourceKind VO + ResourceIdentifier VO + ActorIdentifier VO (with System static).'
issue T027 us1 'AuditEvent entity in src/AuditObservability/Domain/AuditEvent/AuditEvent.cs with all FR-004 fields + private ctor + From(...) factory.'
issue T028 us1 'IAuditEventRepository interface — Add(AuditEvent audit), Task SaveAsync(CancellationToken).'

echo "=== Phase 2 — US1 (Application — subscriber + queries + DTOs) ==="
issue T029 us1 'V1ResourceMapTests — convention scanner picks up every *V1 in Shared.Contracts; identifier picker; unmatched events return None.'
issue T030 us1 'AuditingMessageHandlerTests — happy path + idempotency (duplicate event_identifier swallowed) + unmapped V1 still stored.'
issue T031 us1 'SearchAuditQueryHandlerTests — FR-008 filter grid + cursor pagination round-trip + empty result.'
issue T032 us1 'GetResourceTimelineQueryHandlerTests — three lifecycle events ascending; unrelated overlays excluded; since-filter narrows.'
issue T033 us1 'GetAuditEventQueryHandlerTests — happy path returns row + payload; unknown identifier returns AuditEventNotFound.'
issue T034 us1 'InMemoryAuditEventRepository + FakeBus + FakeClock fakes under tests/AuditObservability.Application.Tests/Fakes/.'
issue T035 us1 'V1ResourceMap static class — scan IIntegrationEvent assembly into a FrozenDictionary<Type, ResourceMappingEntry>; convention-first.'
issue T036 us1 'AuditingMessageHandler open-generic in src/AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs.'
issue T037 us1 'SearchAuditQuery + SearchAuditError hierarchy (InvalidCursor, InvalidFilter).'
issue T038 us1 'GetResourceTimelineQuery + GetResourceTimelineError (UnknownResourceKind, InvalidCursor).'
issue T039 us1 'GetAuditEventQuery + GetAuditEventError (AuditEventNotFound).'
issue T040 us1 'AuditRowDto + AuditPageDto(IReadOnlyList<AuditRowDto> Rows, string? NextCursor).'
issue T041 us1 'SearchAuditQueryHandler — composes IQueryable, applies cursor predicate, Take(pageSize + 1).'
issue T042 us1 'GetResourceTimelineQueryHandler — same cursor mechanic, ascending order.'
issue T043 us1 'GetAuditEventQueryHandler — single row by primary key.'

echo "=== Phase 2 — US1 (Infrastructure + API + end-to-end test) ==="
issue T044 us1 'AuditObservabilityDbContext in src/AuditObservability/Infrastructure/Persistence/. Single DbSet<AuditEvent>.'
issue T045 us1 'AuditEventConfiguration — table audit_events, column map, no Timescale-specific catalog refs in the EF model.'
issue T046 us1 'Initial EF migration via dotnet ef migrations add InitialAuditObservability; augment Up with hypertable + compression policy SQL from plan.md.'
issue T047 us1 'AuditEventRepository — Add enqueues, SaveAsync runs INSERT ... ON CONFLICT (event_identifier) DO NOTHING.'
issue T048 us1 'DesignTimeDbContextFactory for dotnet ef tooling.'
issue T049 us1 'AuditObservabilityMigrator implementing IMigrator per ADR-0067.'
issue T050 us1 'AuditObservabilityPersistenceModule.AddAuditObservabilityPersistence(IHostApplicationBuilder).'
issue T051 us1 'AuditObservabilityInfrastructureModule.AddAuditObservabilityInfrastructure — repo, clock, bus, query handlers, Wolverine wiring.'
issue T052 us1 'AuditEndpoints in src/AuditObservability/Api/AuditEndpoints.cs — three routes per FR-008/FR-009/FR-010 with sse.audit.read + fab guard.'
issue T053 us1 'AuditObservabilityApiModule.AddAuditObservabilityApi.'
issue T054 us1 'Program.cs: AddServiceDefaults + AddBearerAuthentication + AddAuditObservabilityInfrastructure + AddAuditObservabilityApi + MapAuditEndpoints + UseExceptionHandler.'
issue T055 us1 'EndToEndIngestionIntegrationTests — publish CameraRegisteredV1, wait, GET /audit returns the row with expected resource_kind = "camera".'

echo "=== Phase 3 — US2 (cross-fab guard + management-web Audit page) ==="
issue T056 us2 'CrossFabReadGuardIntegrationTests — single-fab operator hits ?fabId=munich → 200, ?fabId=berlin → 403, no fabId → only munich rows.'
issue T057 us2 'apps/shared/src/api/audit.ts — RTK Query slice with searchAudit, getResourceTimeline, getAuditEvent endpoints.'
issue T058 us2 'apps/management-web/src/pages/AuditPage.tsx — filter form + virtualised DataTable + per-row expand showing JSON payload.'
issue T059 us2 'AuditPage.test.tsx — empty state, populated list, filter-applied state, row-expand state.'
issue T060 us2 'apps/management-web/src/routes.tsx — register /audit route + nav entry; guard with sse.audit.read.'

echo "=== Phase 4 — US3 (retention worker + MinIO archiver + integration test) ==="
issue T061 us3 'IAuditChunkArchiver interface in src/AuditObservability/Application/Retention/ with ArchiveChunkAsync(ChunkArchiveRequest).'
issue T062 us3 'AuditRetentionHostedService — IHostedService with PeriodicTimer + TimeProvider; algorithm per plan.md.'
issue T063 us3 'AuditRetentionHostedServiceTests — happy path + idempotency + archiver-throws path.'
issue T064 us3 'MinioOptions configuration record (endpoint, accessKey, secretKey, bucket).'
issue T065 us3 'MinioAuditChunkArchiver — production IAuditChunkArchiver impl backed by AWSSDK.S3; gzipped NDJSON; Content-MD5 + ETag verification.'
issue T066 us3 'Wire IAuditChunkArchiver → MinioAuditChunkArchiver + the hosted service in AuditObservabilityInfrastructureModule.'
issue T067 us3 'RetentionRoundtripIntegrationTests — back-date a chunk via TimeProvider, trigger worker, assert hypertable drop + MinIO object + AuditChunkArchivedV1.'

echo "=== Phase 5 — POLISH ==="
issue T068 polish 'Extend scripts/coverage-check.ps1 with AuditObservability.Domain >= 90 and AuditObservability.Application >= 80.'
issue T069 polish 'Extend tests/Architecture.Tests/BoundaryTests.cs with AuditObservability.Domain has zero framework deps + .Application references Shared.Contracts only.'
issue T070 polish 'BoundaryTests.V1ResourceMap_covers_every_IIntegrationEvent — every concrete IIntegrationEvent is either mapped or [NoAuditMapping].'
issue T071 polish 'README quickstart "Audit who-did-what" — publish a layout, open Audit page, find the LayoutRevisionPublishedV1 row, click "timeline for this resource".'
issue T072 polish 'NFR001_AuditIngestLatencyTests (Aspire-fixture-based) — warm 100, measure 1 000, assert p99 ≤ 50 ms from publish to row committed.'
issue T073 polish 'NFR002_AuditSearchLatencyTests (Aspire-fixture-based) — seed 100 k rows, GET /audit?since=24h&pageSize=50 measure loop, assert p99 ≤ 200 ms.'
issue T074 polish 'docs/runbooks/audit-observability.md — where the hypertable lives, how to inspect chunks, how to trigger retention manually, how to read an archived NDJSON object.'

echo
echo "Done. 74 issues created."
