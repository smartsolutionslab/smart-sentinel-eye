# Feature Specification: EventIngestion — accept, persist, and fan out fab events at scale

**Feature Branch:** `006-event-ingestion`

**Created:** 2026-05-28

**Status:** Draft (Phase 1 — Specify)

**Input:** Sixth feature of Smart Sentinel Eye — the first end-to-end
slice through the **EventIngestion** bounded context. It opens the
upstream half of the camera → event → overlay loop: PLC events,
camera-derived inference events, manual operator annotations, and
generic external webhooks all flow into a single ingestion edge that
persists them durably (hot 30 days in Postgres) and fans them out to
downstream consumers — primarily **Automation** (real-time, rule
evaluation) and **audit/query** (durable, partitioned). Spec 005's
``SystemVariables`` already showed the resolution + push pipeline;
EventIngestion is what eventually drives variable values from real
fab signals instead of the management UI. v1 stops at the ingest +
persist + fan-out edges; the actual rule evaluation that mutates
SystemVariables values is **spec 008 (Automation)**, and the
Postgres → MinIO Parquet archive job is **spec 007**.

The single most load-bearing decision is the latency budget:
**event arrival at our edge → durable persistence + fan-out
completes in ≤ 50 ms p95**. EventIngestion claims a quarter of the
200 ms "event → overlay state" leg of the 800 ms NFR; the remaining
≤ 150 ms is reserved for Automation's rule evaluation and the
broadcaster bridge to ``/hubs/layouts`` (already proven by spec 005).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A PLC gateway publishes a cycle-start event via MQTT (Priority: P1)

A factory-floor PLC gateway (already configured per fab) publishes
a JSON message to the MQTT topic
``fab/munich/plc/station-4`` with payload ``{ kind:
"PlcCycleStart", at: "2026-05-28T08:14:33Z", cycleId: "abc123" }``.
EventIngestion's MQTT subscriber accepts the message, persists it
to Postgres, publishes a ``FabEventIngestedV1`` integration event,
and acknowledges the broker.

**Why this priority:** The MQTT path is the bulk of the 1 000 ev/s
volume target. If this works, the whole ingestion edge is proven —
auth (TLS + per-device password), topic taxonomy, the bounded-channel
backpressure, and Postgres write rate all exercised in one slice.

**Independent Test:**

1. Start the system locally via ``aspire run`` (now includes the
   new ``mosquitto`` resource).
2. Open ``mosquitto_pub`` (or any MQTT client) and publish a JSON
   PLC event to ``fab/munich/plc/station-4`` authenticated as the
   ``station-4`` device.
3. Hit ``GET /events?source=plc&deviceId=station-4`` from the
   management app as admin. The event you just published appears
   in the response within ≤ 1 second.
4. Check ``rabbitmqctl list_queues`` (or the Aspire dashboard's
   RabbitMQ exporter) — there's one ``FabEventIngestedV1`` message
   visible on Automation's subscription queue.

**Acceptance Scenarios:**

1. **Given** the broker is running and the device ``station-4`` is
   provisioned, **when** the device publishes a well-formed JSON
   PLC event to ``fab/munich/plc/station-4``, **then** the event
   is persisted in Postgres with envelope fields
   ``{ eventId, source: "plc", deviceId: "station-4", fabId:
   "munich", kind, occurredAt, ingestedAt, payload }`` and a
   ``FabEventIngestedV1`` is published on RabbitMQ within ≤ 50 ms
   of MQTT ACK.
2. **Given** an unauthenticated MQTT client, **when** it tries to
   publish to ``fab/munich/plc/station-4``, **then** the broker
   refuses the connection (Mosquitto ACL); no event lands.
3. **Given** a malformed payload (not JSON, or missing ``kind`` or
   ``occurredAt``), **when** it arrives on the topic, **then** the
   ingestion subscriber persists it to a ``dead_letters`` table
   with the parse error, increments a Prometheus counter, and
   continues processing the next message (one bad apple does not
   stop the queue).
4. **Given** the same ``eventId`` arrives a second time (broker
   redelivery on a flaky network), **when** ingestion processes
   it, **then** the second insert is a no-op (unique constraint
   on ``(fabId, eventId)`` hit, idempotent 200) and no second
   ``FabEventIngestedV1`` fires.

---

### User Story 2 — A camera-derived inference event arrives via MQTT (Priority: P1)

An on-device AI model running on ``camera-12`` detects "person in
restricted zone" and the camera publishes a JSON message to
``fab/munich/inference/camera-12`` with payload
``{ kind: "PersonInRestrictedZone", at: "...", confidence: 0.92,
boundingBox: [...], snapshotUrl: "s3://..." }``. EventIngestion
ingests it the same way as US1; the only difference is that
Automation may bind this to higher-priority overlay actions.

**Why this priority:** Validates that the envelope-+-opaque-payload
model handles wildly different payload shapes (bounding boxes,
snapshot URLs) with zero schema changes in ingestion.

**Independent Test:**

1. Pre-condition: ``camera-12`` provisioned in Mosquitto with
   publish ACL on ``fab/munich/inference/camera-12``.
2. Use ``mosquitto_pub`` to publish a JSON payload representing a
   ``PersonInRestrictedZone`` event.
3. ``GET /events?source=inference&deviceId=camera-12`` returns the
   event with its full payload preserved as JSONB.

**Acceptance Scenarios:**

1. **Given** a 4 KB JSON payload with nested arrays and an S3 URL,
   **when** ingested, **then** the entire payload is preserved
   verbatim in the JSONB column (no schema validation beyond the
   envelope; payload schema is the producer's contract with
   downstream consumers).
2. **Given** two inference events from ``camera-12`` published in
   order ``A``, ``B``, **when** both are ingested, **then**
   downstream consumers receive ``FabEventIngestedV1`` in the
   same order ``A``, ``B`` (FIFO per ``(source, deviceId)``).

---

### User Story 3 — An operator submits a manual annotation from the kiosk (Priority: P1)

A kiosk operator notices a defect at station 4, presses an
**Annotate** button on the kiosk UI, picks "defect" from a small
predefined list, types an optional note, and submits. The kiosk
POSTs to ``/events/manual`` with the operator's OIDC bearer token.
EventIngestion accepts it the same as any other event.

**Why this priority:** Closes the human-in-the-loop story for v1.
Manual events have a different auth path (OIDC bearer, not MQTT
TLS) so they exercise the HTTP ingress code-path independently.

**Independent Test:**

1. Sign in to ``kiosk-web`` as an operator.
2. Click **Annotate** on a CameraViewer, pick "Defect", add a
   note "scratch on panel", submit.
3. ``GET /events?source=manual`` as admin returns the annotation
   within ≤ 1 second, with the operator's identifier in
   ``createdBy`` and the kiosk's cameraIdentifier captured as the
   ``deviceId``.

**Acceptance Scenarios:**

1. **Given** an authenticated operator, **when** they POST to
   ``/events/manual``, **then** the event is accepted and
   persisted with ``source: "manual"`` and ``deviceId`` derived
   from the kiosk-supplied camera identifier.
2. **Given** an unauthenticated client, **when** it POSTs to
   ``/events/manual``, **then** it gets 401.
3. **Given** a free-text note exceeding 1024 chars, **when** it's
   submitted, **then** the request is rejected with a 400 and a
   clear error; nothing is persisted.

---

### User Story 4 — An external system fires a webhook (Priority: P2)

The fab's QA tooling fires an HTTP POST to ``/events/webhook/qa``
with a static bearer token in the ``Authorization`` header and
JSON in the body. EventIngestion validates the bearer, looks up
the integration (``qa``), persists the event as
``source: "webhook"`` with ``deviceId: "qa"``, and fans out.

**Why this priority:** P2 because it's the least operationally
critical of the four sources in v1 — there are no known external
integrations on day one. But it's table stakes for any "industrial"
system and the surface is small enough to ship in v1.

**Independent Test:**

1. Register a new webhook integration ``qa`` via an admin endpoint
   (creates a row with a generated bearer token).
2. Use ``curl`` to POST a JSON body to ``/events/webhook/qa`` with
   the bearer token in the header.
3. ``GET /events?source=webhook`` returns the event.

**Acceptance Scenarios:**

1. **Given** a registered webhook with a known bearer token,
   **when** a POST arrives with that token, **then** the event is
   accepted (200) and persisted with ``source: "webhook"`` and
   ``deviceId`` equal to the integration name.
2. **Given** a missing or wrong token, **when** a POST arrives,
   **then** the response is 401 with no information leakage about
   whether the integration exists.
3. **Given** the same payload re-sent (network retry), **when**
   the second POST arrives, **then** ingestion server-generates an
   ``eventId`` for each request (no caller-supplied id for
   webhooks), so the second event lands as a separate row —
   webhook-side dedup is the integration owner's responsibility.

---

### User Story 5 — Burst above 1 000 ev/s sustained triggers backpressure (Priority: P2)

A misbehaving PLC gateway floods the broker with 5 000 events/s
for 30 seconds. EventIngestion's bounded in-memory channel
(5 000 slots per instance) fills up; the MQTT subscriber stops
ACKing until drain, and the HTTP ingress returns ``429 Too Many
Requests`` with ``Retry-After: 1``. No events are silently lost.

**Why this priority:** P2 because steady-state 1 000 ev/s is the
sizing target; burst handling is a correctness story for the
unhappy day. It is, however, the difference between "production
ready" and "fragile prototype" so it must ship in v1.

**Independent Test:**

1. Run a small load generator publishing 5 000 ev/s for 30 s
   against the local broker.
2. Observe the Prometheus metric ``ingest_channel_depth`` saturate
   at 5 000 and ``ingest_429_total`` increment for HTTP sources.
3. Stop the load generator; the channel drains within ≤ 60 s.
4. ``COUNT(*)`` in Postgres matches the count of accepted events
   (every published event that was not 429'd or NACK'd is in
   the store).

**Acceptance Scenarios:**

1. **Given** sustained ingress above the bounded channel's drain
   rate, **when** the channel is full, **then** new HTTP requests
   receive 429 with ``Retry-After`` and MQTT subscribers stop
   ACKing (the broker holds the queue depth).
2. **Given** the burst ends, **when** the channel drains, **then**
   new ingress is accepted again with no operator intervention.

---

## Functional Requirements

### Envelope + payload
- **FR-001** Every ingested event lands in a single row in the
  ``events`` table with the canonical envelope:
  ``{ eventId, fabId, source, deviceId, kind, occurredAt,
  ingestedAt, payload }``. ``source`` is one of
  ``plc | inference | manual | webhook``. ``payload`` is opaque
  JSONB.
- **FR-002** ``eventId`` is a ``Guid``. For ``plc`` and
  ``inference``, the device supplies it in the MQTT payload
  (``"eventId": "<guid v7>"``); for ``manual`` and ``webhook``,
  the server mints a Guid v7. The pair ``(fabId, eventId)`` is
  the dedup key.
- **FR-003** ``occurredAt`` (when the source observed the event)
  and ``ingestedAt`` (when we accepted it) are both required and
  separate; downstream consumers can choose which to sort by.
- **FR-004** ``deviceId`` carries the per-source identifier:
  PLC station name, camera id, kiosk camera id, or webhook
  integration name. It is **always present**; events without a
  device id are rejected (FR-014).
- **FR-005** ``payload`` schema is **not validated** by ingestion
  beyond "is valid JSON ≤ 64 KB"; payload contracts are
  consumer-side (Automation + audit views).

### Ingress — MQTT (PLC + inference)
- **FR-006** ``mosquitto`` is the canonical broker. Aspire's
  AppHost composes it as a resource for dev; production deploys
  via Helm. Per-device passwords (Mosquitto ``passwords.txt`` +
  ACL file) authenticate publishers.
- **FR-007** Topic taxonomy: ``fab/{fabId}/{source}/{deviceId}``.
  ``plc`` and ``inference`` are the only ``source`` values
  carried over MQTT in v1. Each device may publish only to its
  own topic; the ACL enforces this.
- **FR-008** EventIngestion runs an MQTT subscriber per fab that
  listens on the wildcard topic ``fab/{fabId}/+/+``. Messages are
  parsed into the envelope, persisted, and fanned out. The
  subscriber uses QoS 1 (at-least-once delivery, dedup via
  ``(fabId, eventId)``).

### Ingress — HTTP (manual + webhook)
- **FR-009** ``POST /events/manual`` accepts a JSON body
  ``{ deviceId, kind, occurredAt, payload }``. Requires an OIDC
  bearer with the ``operator`` or ``admin`` policy. The server
  mints the ``eventId`` and ``ingestedAt``.
- **FR-010** ``POST /events/webhook/{integrationName}`` accepts
  any JSON body. Requires ``Authorization: Bearer <token>`` where
  ``<token>`` is the static token stored for the integration. The
  server mints ``eventId``, ``ingestedAt``, sets ``deviceId`` to
  ``integrationName``, ``kind`` to the integration's configured
  default kind (or ``Webhook``).
- **FR-011** Both HTTP endpoints are governed by the same bounded
  channel as MQTT; full channel ⇒ 429.

### Persistence
- **FR-012** The ``events`` table is partitioned by ``fabId`` and
  range-partitioned on ``ingestedAt`` (monthly partitions, created
  lazily on first insert per month). Indexes:
  ``UNIQUE (fabId, eventId)``, ``(fabId, source, deviceId,
  occurredAt DESC)``, ``(fabId, ingestedAt DESC)``.
- **FR-013** Partitioning is set up; the archive-then-DROP job is
  **out of scope** for spec 006 and deferred to spec 007. The 30-
  day retention contract is documented but enforced manually in
  v1.

### Validation + error handling
- **FR-014** A message is **rejected** (HTTP 400 / MQTT
  ``dead_letters`` row) if any of: missing ``kind``,
  missing/invalid ``occurredAt``, missing ``deviceId``, JSON
  payload > 64 KB, JSON parse error.
- **FR-015** Rejected MQTT messages are persisted to a separate
  ``dead_letters`` table (envelope columns plus the raw payload
  bytes and the parse error) so operators can post-mortem without
  re-deploying the subscriber.

### Fan-out
- **FR-016** Every successfully-persisted event publishes
  ``FabEventIngestedV1`` on RabbitMQ via Wolverine's outbox. The
  event carries the full envelope **and the payload** (so
  Automation does not need to round-trip to Postgres). Max
  message size: 70 KB (envelope + 64 KB payload + framing).
- **FR-017** Order: per ``(fabId, source, deviceId)``,
  ``FabEventIngestedV1`` messages are published in
  ``ingestedAt`` order. Wolverine routes by ``deviceId`` hash to
  preserve per-source FIFO on the consumer side.

### Read model + API
- **FR-018** ``GET /events`` lists events with filters:
  ``source``, ``deviceId``, ``kind``, ``occurredAfter``,
  ``occurredBefore``, ``ingestedAfter``, ``ingestedBefore``.
  Pagination: cursor-based on ``(ingestedAt, eventId)``. Default
  page size 100, max 1 000.
- **FR-019** ``GET /events/{eventId}`` returns one event.
- **FR-020** ``GET /events/dead-letters`` (admin only) returns
  rejected MQTT messages from the dead-letter table.

### Backpressure
- **FR-021** A bounded ``Channel<EventEnvelope>`` of 5 000 slots
  per instance buffers between ingress and the persistence loop.
- **FR-022** When the channel is full: HTTP returns ``429 Too
  Many Requests`` with ``Retry-After: 1``; the MQTT subscriber
  stops calling ``PublishAsync`` upstream-of-the-channel until
  drain (so broker queue depth becomes the buffer of last
  resort). Prometheus counters ``ingest_429_total`` and
  ``ingest_channel_full_seconds`` are exposed.

### Webhook integrations
- **FR-023** ``POST /webhook-integrations`` (admin only) registers
  a new integration: body ``{ name, defaultKind }``; returns the
  generated bearer token (shown once).
- **FR-024** ``GET /webhook-integrations`` lists registered
  integrations (name + defaultKind, never the token).
- **FR-025** ``DELETE /webhook-integrations/{name}`` revokes an
  integration; subsequent POSTs to that integration return 401.

### Authorization
- **FR-026** Every write endpoint requires admin or operator
  policy (per ADR-0007 + ADR-0008): MQTT auth is broker-level
  per-device passwords + ACL; HTTP manual is OIDC bearer with the
  ``operator`` policy; HTTP webhook is static bearer matching a
  registered integration; webhook-integration CRUD is admin only.
  Reads (``GET /events``, ``GET /webhook-integrations``) require
  ``admin`` only.

## Non-Functional Requirements

- **NFR-001** Event arrival → durable persistence + ``FabEventIngestedV1``
  on the bus: **≤ 50 ms p95** measured from MQTT ACK / HTTP
  request body fully read to outbox-row visible. Budget:
  ≤ 5 ms parse + envelope build, ≤ 25 ms Postgres insert (with
  outbox row), ≤ 15 ms Wolverine outbox dispatch, ≤ 5 ms
  headroom.
- **NFR-002** Sustained throughput: **1 000 events/sec** per fab
  with the 50 ms p95 budget intact. Mixed ratio sized at 70%
  inference, 25% PLC, 4% manual, 1% webhook. Above this rate
  backpressure kicks in (FR-021/FR-022) without latency
  regression on accepted events.
- **NFR-003** Burst tolerance: an additional 5 000 ev/s burst
  above sustained for ≤ 30 seconds is absorbed by the broker
  queue + bounded channel without data loss; recovery to steady
  state within ≤ 60 seconds of burst end.
- **NFR-004** Postgres footprint under steady state: at 1 000
  ev/s × 86 400 s × 30 days ≈ 2.6 B rows hot. Monthly partitioning
  keeps each partition ≤ ~85 GB; indexes ≤ ~30 GB per partition.
  Plan must confirm these numbers against the deployment
  hardware envelope.
- **NFR-005** Process restart: subscriber resumes from the broker
  (QoS 1, persistent session), no ingestion gap on rolling
  deploys. HTTP returns 503 only between ASP.NET ``Started`` and
  ``Listening`` (< 1 s on a warm container).
- **NFR-006** Replay-safety: the ``(fabId, eventId)`` unique
  constraint makes the whole ingress idempotent. A device may
  re-publish the same ``eventId`` after a network blip without
  duplicating downstream effects.

## Out of Scope (deferred or rejected)

- **Archive to MinIO Parquet** — partitioning is set up; the
  nightly DROP-and-write-Parquet worker is **spec 007**.
- **Cold-read across archived partitions** — same; spec 007 owns
  the cold-read API.
- **Automation / rule evaluation** — ingestion only persists +
  fans out; rule evaluation is **spec 008 (Automation)**.
- **Variable-value updates from events** — the bridge from
  ``FabEventIngestedV1`` to ``SetVariableValue`` is part of
  Automation. EventIngestion neither knows nor cares about
  SystemVariables.
- **Schema validation of payloads** — opaque JSONB only. Any
  per-kind schema enforcement is downstream (a consumer's
  problem) or a follow-on spec.
- **gRPC ingress** — HTTP + MQTT only. Revisit if a producer
  explicitly needs it.
- **mTLS for webhooks** — static bearer per integration only in
  v1. mTLS path is a follow-on if a customer needs it.
- **Multi-fab broker federation** — each fab runs its own
  Mosquitto + its own EventIngestion instance. No cross-fab
  event flow.
- **Per-source rate limits beyond the global channel cap** —
  one bounded channel for everything. Per-source quotas are a
  follow-on.
- **Webhook payload schema registry** — integration owners
  document their own payload contract out-of-band.

## Cross-Context Reach

EventIngestion is **self-contained**. It publishes
``FabEventIngestedV1`` on the integration bus (in
``Shared.Contracts``) and that's the only contract it exposes.
Downstream consumers (Automation, future audit views) subscribe
on their own terms with no reference to EventIngestion's
projects. No new ``AllowedCrossContext`` entries are needed.

The MQTT broker is the only new shared infrastructure
component — it lives at the same layer as RabbitMQ and Postgres
(an Aspire-composed resource).

## Constitution Check

- **§I (walking skeleton):** EventIngestion is the upstream half
  of the camera → event → overlay loop. Without it, every
  variable value has to be typed by an operator (spec 005's
  limitation); with it, the system reacts to real fab signals.
- **§IV (latency budget):** 50 ms p95 spends a quarter of the
  200 ms "event → overlay state" leg of the 800 ms NFR. The
  remaining ≤ 150 ms is for Automation's rule eval + the spec-005
  broadcaster bridge to ``/hubs/layouts`` (already proven).
- **§VII (no event sourcing without justification):** events are
  CRUD inserts in a partitioned Postgres table; the fan-out is
  Wolverine outbox, not event sourcing. The audit replay use case
  is satisfied by querying the events table directly.
- **§IX (forward-compat):** ``FabEventIngestedV1`` is intentionally
  generic enough that Automation can evolve its rule-DSL without
  forcing schema changes in EventIngestion. ``source`` +
  ``kind`` are extensible string fields, not closed enums.
- **§II (locked tech stack):** introduces Mosquitto as a new
  fab-side infra component. **This is a tech-stack addition and
  requires a new ADR** (to be drafted as part of Phase 2 — Plan)
  recording the choice + rejected alternatives (RabbitMQ MQTT
  plugin, EMQX). Per the constitution governance rules,
  significant tech-stack additions must be ADR'd before Phase 4
  begins.

## Gate (Phase 1 → Phase 2)

This spec is ready for the Plan phase once the architect lead
confirms:

1. No ``[NEEDS CLARIFICATION]`` markers remain. ✅
2. The five user stories cover the v1 product surface; each is
   independently testable.
3. The Mosquitto-as-new-infra decision is acceptable as a
   tech-stack addition (an ADR will be drafted in Phase 2).
4. The 50 ms p95 NFR is achievable on the chosen stack; the
   plan will verify the breakdown numbers against benchmarks of
   the target Postgres + Wolverine outbox path.
5. Cross-context reach is empty by design — Automation will pick
   up ``FabEventIngestedV1`` on its own subscriber queue without
   any new allow-rule.

When the gate is approved, Phase 2 (``/speckit-plan``) drafts
``plan.md`` against the locked tech stack — a new
``EventIngestion.Domain`` aggregate (``Event``), envelope value
objects (``EventId``, ``DeviceId``, ``Source``, ``Kind``,
``OccurredAt``, ``IngestedAt``), a new
``FabEventIngestedV1`` integration event in ``Shared.Contracts``,
a Mosquitto Aspire resource + Helm chart fragment, an MQTT
subscriber hosted service, two HTTP endpoints (``/events/manual``
+ ``/events/webhook/{integration}``), the bounded-channel
backpressure machinery, and the partitioned ``events`` table +
read API.
