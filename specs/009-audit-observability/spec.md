# Feature Specification: AuditObservability — bus-fed audit trail across every context

**Feature Branch:** `009-audit-observability`

**Created:** 2026-05-29

**Status:** Draft (Phase 1 — Specify)

**Input:** The ninth and final bounded context of Smart Sentinel
Eye. Up to now every domain mutation lands in its own context's
Postgres + emits a `*V1` integration event on RabbitMQ. There is
no cross-cutting record of **who did what, when, in which fab** —
investigations rely on git-grepping logs or replaying RabbitMQ
queues if the retention is long enough. Spec 009 introduces the
**AuditObservability** context: a single subscriber to every
existing `*V1` contract, storing one normalised audit row per
delivery in a 90-day hot Postgres tier (TimescaleDB hypertable)
plus a per-chunk cold archive on MinIO, with a search +
per-resource timeline HTTP API and a management-web Audit page
gated by the spec 008 scope catalogue.

The single load-bearing decision is **subscribe to every `*V1`
event uniformly** rather than per-context opt-in. Coverage is
automatic for every future V1 once it ships in `Shared.Contracts`;
no per-context plumbing required. The price is the largest single
RabbitMQ subscriber surface in the system — a constitution check
warrants that this scale is fine.

This spec also introduces **TimescaleDB** as a new dependency,
which is constitution-amendment territory (the locked tech stack
in ADR-0024 is plain PostgreSQL). The Phase 2 plan must clear
the amendment + a new ADR-XXXX before any code is written.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Compliance reviewer searches the audit trail (Priority: P1)

A compliance reviewer at Smart Solutions HQ holds a JWT issued
by the shared Keycloak realm with `groups: ["/fabs/munich",
"/fabs/berlin"]` and scope `sse.audit.read`. They open the
management-web Audit page, type **"adm@munich.test"** into the
actor filter and **"rule"** into the event-kind filter, set the
date range to "last 7 days", and hit search.

Management-web calls
`GET /audit?actor=adm@munich.test&eventKind=Rule*&since=2026-05-22T00:00:00Z`.
AuditObservability:

1. Verifies the JWT scope (`sse.audit.read`) at the edge.
2. Reads the caller's `groups` claim; only returns rows whose
   `fabId` is in the caller's fab set (Munich + Berlin both
   match in this case).
3. Queries the `audit_events` TimescaleDB hypertable with the
   filter pushed down; cursor-paginates the result with a hard
   cap of 200 rows.
4. Returns each row's indexed metadata + a preview of the
   `payload` JSON; the full payload is fetched lazily on row
   expand.

The reviewer clicks the third result — a
`RuleArchivedV1(name="oee-fast-cycle")` from yesterday — and
the row expands to show: occurred at `2026-05-28T16:14:33Z`,
fab `munich`, actor sub `91d3…`, actor username
`adm@munich.test`, payload JSON verbatim. They click
**"timeline for this rule"** which pivots to
`GET /audit/rule/oee-fast-cycle?fabId=munich` and surfaces every
event touching that rule's lifecycle in occurrence order
(created → published → archived).

**Why this priority:** The whole point of the context. Without
P1 there is no AuditObservability.

**Independent Test:** Stand up the AuditObservability subscriber
+ Postgres + Identity stub. Publish three lifecycle V1s for one
rule across two fabs via a test bus producer. Query the search
endpoint with admin credentials; verify exactly the rows the
admin's `groups` claim allows are returned. Switch the admin to
a single-fab user; verify cross-fab rows are filtered out.

#### Acceptance Scenarios

1. **Given** an audit row exists for fab `munich` and the caller's
   `groups` claim contains `/fabs/munich`, **when** they call
   `GET /audit?fabId=munich`, **then** the row is included in
   the response.
2. **Given** the same row, **when** a caller whose `groups`
   claim contains only `/fabs/berlin` queries
   `GET /audit?fabId=munich`, **then** the response is `403
   RESOURCE_FAB_NOT_AUTHORIZED`.
3. **Given** the same row, **when** the caller in (2) queries
   `GET /audit` (no `fabId` filter), **then** the response
   excludes the row (no membership), and the rest of the
   response is shaped exactly like case (1) with the
   `fabId=berlin` rows that the caller IS authorised for.
4. **Given** an inbound `RuleCreatedV1` is redelivered twice by
   Wolverine within 30 s (subscriber restart simulation), **when**
   AuditObservability ingests both copies, **then** exactly one
   row exists in `audit_events` (unique-`eventIdentifier`
   constraint).
5. **Given** the reviewer expands the row in (1), **when** the
   UI fetches `GET /audit/{auditIdentifier}`, **then** the
   verbatim payload JSON from the bus is returned plus the
   indexed columns.

### User Story 2 — Operator pivots from an alert to "who touched this overlay?" (Priority: P2)

An operator viewing a kiosk notices an overlay has the wrong
label. They click into the management-web Overlays page and
choose **"audit timeline"** from the overlay's row menu. The UI
calls `GET /audit/overlay/{overlayIdentifier}?fabId=munich` and
returns the full lifecycle: `OverlayCreatedV1`,
`OverlayRevisionPublishedV1(rev=1)`, then `OverlayRevisionPublishedV1(rev=2)`
two hours ago by `op-3@munich.test`. The operator escalates the
incident with that screenshot.

**Why this priority:** Operations payoff — the audit trail
becomes useful for live troubleshooting, not just compliance.

**Independent Test:** Publish a 3-event lifecycle for one
overlay. Call the per-resource endpoint with operator
credentials. Verify the events return in ascending
`occurred_at` order with millisecond precision and that
unrelated overlay events are excluded.

#### Acceptance Scenarios

1. **Given** three lifecycle events for overlay X published over
   2 hours, **when** the operator hits
   `GET /audit/overlay/X?fabId=munich`, **then** they get a 3-row
   array in occurrence order with `occurredAt` deltas matching
   the bus timestamps to ≤ 1 ms.
2. **Given** the operator queries with `?since=` set to between
   event 2 and 3, **then** they get a 1-row array containing
   only the third event.

### User Story 3 — Old chunks ride to MinIO automatically (Priority: P3)

It is 02:00 fab-local time. The TimescaleDB hypertable's
oldest chunk (the calendar month from 92 days ago) crosses the
90-day boundary. The retention worker:

1. Streams the chunk's rows to a `gzip`-compressed NDJSON
   buffer in memory (or a temp file if the chunk exceeds 256
   MB).
2. Uploads to MinIO at
   `s3://audit/archive/fab=*/year=YYYY/month=MM/chunk.ndjson.gz`
   with a manifest checksum.
3. Verifies the upload by reading the object's ETag back.
4. Calls TimescaleDB's `drop_chunks()` on that one chunk.
5. Emits `AuditChunkArchivedV1` on the bus for downstream
   observability.

A cold-read API endpoint stays out of v1 — investigators
retrieve archived chunks by direct MinIO download for now.

**Why this priority:** Hot Postgres can't grow forever. Without
P3 the table balloons after 6 months; that's a slow problem,
not a launch blocker.

**Independent Test:** Use the test-time `TimeProvider` to
back-date a chunk to 91 days old, trigger the retention worker
manually via a dev-only endpoint, then assert: the chunk is
absent from the hypertable, an object exists in MinIO with the
expected row count, and an `AuditChunkArchivedV1` is on the
bus.

#### Acceptance Scenarios

1. **Given** a hypertable chunk older than 90 d containing N
   rows, **when** the retention worker runs, **then** MinIO
   contains an object with N gzipped NDJSON rows, ETag matches
   the local checksum, the chunk is dropped from Postgres, and
   `AuditChunkArchivedV1` is on the bus with the object key +
   row count.
2. **Given** the worker fails after upload but before drop (CI
   simulates with a process kill), **when** the worker
   restarts, **then** the next run is idempotent — the upload
   step short-circuits because the object exists with a
   matching ETag, and the drop proceeds.

## Functional Requirements *(mandatory)*

### Subscription

- **FR-001** AuditObservability subscribes to **every concrete
  `IIntegrationEvent` type defined in `Shared.Contracts`**
  (current count: ~25 across specs 001-008, growing). The
  binding happens via Wolverine conventional routing using a
  single bus subscriber that catches an open generic
  `IIntegrationEvent` envelope.
- **FR-002** A new V1 added in `Shared.Contracts` is audited
  automatically the next deployment. No per-context registration
  required.
- **FR-003** Inbound deliveries are processed with **Wolverine's
  Postgres outbox** so a single TX writes the audit row +
  acknowledges the bus message + dispatches outbound
  `AuditChunkArchivedV1` (P3) without dual-write hazard.

### Row shape + de-duplication

- **FR-004** Every audit row carries the columns:

  | Column                | Type                        | Notes |
  | --------------------- | --------------------------- | ----- |
  | `audit_id`            | `uuid` (Guid v7)            | PK; mints client-side at handler |
  | `occurred_at`         | `timestamptz`               | Bus-emitted timestamp; hypertable partition key |
  | `received_at`         | `timestamptz`               | Subscriber-set ingest time; for processing-latency diagnostics |
  | `fab_id`              | `text`                      | From V1 if present, else `null` for cross-fab events |
  | `event_kind`          | `text`                      | `nameof(TIntegrationEvent)` (e.g. `RuleCreatedV1`) |
  | `resource_kind`       | `text`                      | `"rule"`, `"overlay"`, `"camera"`, … pulled from a V1-to-resource map |
  | `resource_identifier` | `text`                      | Aggregate identifier from the V1 (Guid as string OR business name) |
  | `actor_identifier`    | `uuid`                      | JWT `sub` propagated by the originating context in the V1 payload |
  | `actor_username`      | `text`                      | Cached `preferred_username` at write time; nullable for system actors |
  | `event_identifier`    | `uuid`                      | Globally-unique idempotency key — `UNIQUE` index |
  | `payload`             | `jsonb`                     | Verbatim V1 body for forensic replay |
  | `payload_size_bytes`  | `integer`                   | For storage analytics |
  | `schema_version`      | `smallint`                  | Always `1` in v1; reserved for forward-compat |

- **FR-005** The handler **never throws on unknown V1 shape**.
  An unrecognised event kind is stored with `resource_kind = null`
  + `resource_identifier = null`; a metric counter ticks so the
  V1-to-resource map can be updated in a follow-up. Audit
  coverage stays at 100 % even if the resource-mapping registry
  drifts.
- **FR-006** Idempotency: `event_identifier` carries a unique
  index. `INSERT … ON CONFLICT (event_identifier) DO NOTHING`
  absorbs Wolverine at-least-once redeliveries silently.
- **FR-007** Actor identity is extracted from the V1 payload
  itself (every spec 001-008 V1 already carries the originating
  caller's `sub` per existing convention). When the V1 carries
  no actor (e.g. system-emitted `AuditChunkArchivedV1` itself,
  background reconcilers), the row records `actor_identifier =
  NULL_GUID` + `actor_username = "system"`.

### Read API

- **FR-008** `GET /audit?fabId=…&actor=…&actorUsername=…&eventKind=…&resourceKind=…&resourceIdentifier=…&since=…&until=…&pageSize=…&cursor=…`
  is the cross-cutting search endpoint. `pageSize` is bounded
  `[1, 200]`; default 50. The response carries a `nextCursor`
  field — opaque base64-encoded `(occurred_at, audit_id)` tuple
  so pagination is stable under concurrent inserts.
- **FR-009** `GET /audit/{resourceKind}/{resourceIdentifier}?fabId=…&since=…&until=…&pageSize=…&cursor=…`
  is the per-resource timeline. Same pagination shape.
  `resourceKind` matches the v1 vocabulary
  (`camera | stream | layout | overlay | variable | rule |
  event | webhook | device | kiosk | webhook-integration`).
- **FR-010** `GET /audit/{auditIdentifier}` returns the single
  row with full `payload` JSON.
- **FR-011** Every read endpoint requires the
  `sse.audit.read` scope (new entry in the spec 008 `Scope`
  catalogue). Writes via the bus subscriber are server-internal
  — no public write endpoint.
- **FR-012** Fab guarding: when the caller's request omits
  `fabId`, the response is restricted to rows whose `fab_id` is
  in the caller's `groups` claim (`/fabs/<fabId>` set). When
  `fabId` is supplied, the existing `IFabAuthorizationGuard`
  returns `403 RESOURCE_FAB_NOT_AUTHORIZED` if the caller is
  not a member.

### Retention + cold archive

- **FR-013** The hot Postgres hypertable has a 90-day retention
  window. A daily background worker
  (`AuditRetentionHostedService`) detects chunks whose end
  boundary has crossed the threshold + exports them to MinIO
  before TimescaleDB's `drop_chunks()` removes them.
- **FR-014** Exported objects land at
  `s3://audit-archive/fab=<fabId>/year=YYYY/month=MM/<chunkId>.ndjson.gz`
  with the `Content-MD5` ETag computed pre-upload and verified
  post-upload. Cross-fab events (no `fab_id`) export to
  `fab=_unscoped/…`.
- **FR-015** Each completed export emits
  `AuditChunkArchivedV1(chunkIdentifier, fabId, rowCount,
  archivedAt, minioObjectKey, etag)` on the bus.
- **FR-016** Recovery: on subscriber startup, the worker
  inspects each pending chunk (those past threshold but not yet
  archived). It looks up `s3://audit-archive/…/<chunkId>.…`
  first; if present and ETag matches, skip upload + proceed to
  drop. If absent or mismatched, re-export. Idempotent under
  arbitrary mid-flight failures.

### Constitution amendment

- **FR-017** Before implementation begins, **ADR-0024 (locked
  tech stack)** is amended to add **TimescaleDB** as a
  permitted PostgreSQL extension scoped to the
  AuditObservability context, with the rationale documented in
  a new **ADR-XXXX** (number TBD). Amendment goes via
  `/speckit-constitution`; no code in PR D onwards until the
  amendment is accepted.

## Non-Functional Requirements *(mandatory)*

- **NFR-001** Ingest latency: ≤ 50 ms p99 from
  RabbitMQ deliver-ack to audit row committed, under sustained
  100 ev/s load. Single-table insert + jsonb store comfortably
  clears this; the budget exists so a future schema bloat or
  bad index does not silently regress.
- **NFR-002** Search latency: `GET /audit` with a tight time
  filter (`since` within the last 24 h) returns the first 50
  rows within p99 ≤ 200 ms on the warm hot tier. Per-resource
  timeline ≤ 100 ms p99 (most aggregates have < 1 000 lifetime
  events).
- **NFR-003** Storage growth: ~250 bytes/row indexed columns
  + 1-4 KB jsonb payload. 90-day hot at sustained 100 ev/s ≈
  78 GB hypertable + indexes. Cold archive is 5-10× smaller
  per chunk after gzip.
- **NFR-004** Subscriber availability: a single-replica
  AuditObservability service is acceptable for v1 (audit lag
  during downtime ≤ 5 min; events accumulate on the bus and
  process when the service returns). High-availability deploy
  is a spec 010 concern.
- **NFR-005** Read endpoints honour the spec 008 latency
  contract (sub-millisecond JWT validation amortised; fab
  guard ≤ 50 µs).

### Personas + tokens (reused from spec 008)

| Persona            | Scope                  | Visibility |
| ------------------ | ---------------------- | ---------- |
| compliance-reviewer | `sse.audit.read`      | All fabs in their `groups` claim |
| admin              | `sse.audit.read` (added to admin bundle) | Their fabs |
| operator           | `sse.audit.read` (read-only; added to operator bundle) | Their fab |
| kiosk / device     | — (no audit access)   | — |

`sse.audit.read` joins the catalogue. No `sse.audit.write` —
audits are bus-fed only.

## Constitution Check

| Principle                        | This spec                                                                  | OK? |
| -------------------------------- | -------------------------------------------------------------------------- | --- |
| §II DDD + value objects          | `AuditIdentifier`, `EventIdentifier`, `ResourceKind`, `EventKind` VOs.    | ✅ |
| §III Bounded-context isolation   | All code in `SmartSentinelEye.AuditObservability.*`. Reads `Shared.Contracts` only — never another context's Domain or Application. **No new `AllowedCrossContext` entries.** | ✅ |
| §IV Latency budget               | NFR-001 / NFR-002 keep audit off every hot path; subscriber is async. Search read is its own budget, well under any context budget. | ✅ |
| §V Spec-driven                   | This spec + plan + tasks.                                                  | ✅ |
| §VI Aspire composition root      | New AppHost resources: `audit-observability` (project, already scaffolded) + `audit-db` (TimescaleDB extension on the existing `postgres` server) + MinIO (new bucket on existing object store). | ✅ |
| §VII No event sourcing without justification | `AuditEvent` is the simplest possible CRUD entity — single `INSERT … ON CONFLICT`, no aggregate state. ES would add complexity for zero gain. | ✅ |
| §VIII Safe at trust boundaries   | JWT validation + scope check + fab guard at every read endpoint via `ServiceDefaults`. Subscriber side has no public surface. | ✅ |
| §IX Forward-compat               | `schema_version` column reserves the forward compat slot; new V1s onboard automatically. | ✅ |
| **§X Locked tech stack (ADR-0024)** | **Requires amendment + ADR-XXXX for TimescaleDB.** | ⚠ |

The constitution amendment is gating: Phase 2 (Plan) cannot
finalise until `/speckit-constitution` accepts the TimescaleDB
addition + a new ADR records the rationale.

## Out of Scope (v1)

- Cold-read API (download archived chunks from MinIO via HTTP).
  v1 archives only; investigators retrieve archives via direct
  MinIO credentials. Spec 010.
- High-availability subscriber (multi-replica with leader
  election). v1 single-replica is acceptable per NFR-004.
- Audit-log signing / tamper evidence (Merkle log, AWS QLDB-
  style). Compliance reviewer accepts the hypertable's standard
  Postgres durability for v1; signed-log is a future spec.
- Audit-driven alerting (Grafana panels, anomaly detection on
  audit volume). Sits in the OpenTelemetry / Grafana stack
  (ADR-0026), not in AuditObservability itself.
- Per-row redaction for PII. Spec 009 assumes V1 payloads do
  not contain PII subject to access-control beyond
  `sse.audit.read` + fab membership. If a future V1 carries
  PII, that context's spec must add a redaction layer at emit
  time.
- `AuditChunkArchivedV1` consumers other than diagnostics
  logging. The event is published for future fan-out; no other
  context subscribes in v1.

## Dependencies + Risks

- **TimescaleDB** is the load-bearing dependency. If the
  constitution amendment is rejected, fall back to native
  PostgreSQL declarative range partitioning + a custom retention
  worker. Same data shape, more bespoke ops; NFR targets still
  achievable. Plan documents both branches.
- **`Shared.Contracts` V1 stability**. Renaming an integration
  event type breaks the audit kind taxonomy (old rows still
  carry the old `event_kind` string). Mitigation: V1 contracts
  are already locked under ADR-0040; a rename is a `V2` not an
  edit.
- **Single-replica subscriber** during a multi-hour outage
  accumulates events on the bus past RabbitMQ retention →
  audit gap. Mitigation: RabbitMQ persistence is ≥ 30 days in
  the Helm chart; the runbook documents the recovery path.
- **MinIO availability** during the retention window. If MinIO
  is down at the daily archive time, the worker retries with
  exponential backoff; chunks accumulate in Postgres past 90
  days (storage overhead acceptable for days, not weeks). Run-
  book covers the MinIO-down case.

## Gate (Phase 1 → Phase 2)

This spec is ready for the Plan phase once the architect lead
confirms:

1. The **`*V1`-uniformly** subscription model (vs curated
   opt-in) is the right trade — automatic coverage vs storage
   volume.
2. The **TimescaleDB amendment** is acceptable in principle
   (the formal amendment lands in Phase 2 alongside the new
   ADR).
3. The **90-day / hot-cold split** matches the team's
   compliance + storage-cost expectations.
4. The **cross-fab read model** (rows filtered by the caller's
   `groups` claim when `fabId` is omitted) is the desired
   reviewer experience.
5. The **out-of-scope list** is complete — no missing v1
   requirements lurking.

When (1)–(5) are approved, this spec freezes; Phase 2 produces
`plan.md`; Phase 3 produces `tasks.md` + GitHub issues.
