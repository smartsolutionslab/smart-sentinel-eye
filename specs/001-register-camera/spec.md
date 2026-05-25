# Feature Specification: Register a Camera

**Feature Branch:** `001-register-camera`

**Created:** 2026-05-25

**Status:** Clarified (Phase 2 closed)

**Input:** First feature of Smart Sentinel Eye — the smallest end-to-end
vertical slice in the **Camera Catalog** bounded context. Pre-condition
for every downstream feature (Stream Distribution, Layout & Composition,
Overlays, Operator Controls). Selected as the first spec because every
other feature presupposes that cameras exist.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Register a single camera by RTSP URL (Priority: P1)

A fab admin opens the management app, navigates to the camera catalog,
and adds a new camera by entering a unique name and the camera's RTSP
URL. The system assigns the camera a stable identifier, stores it,
publishes an integration event so other parts of the system can react,
and shows the camera in the catalog list.

**Why this priority:** This is the smallest possible vertical slice
that proves the full stack works end-to-end — admin browser →
Identity → Camera Catalog API → Postgres → RabbitMQ → projection. Nothing
else in Smart Sentinel Eye is reachable without cameras existing. It
also exercises every locked architectural decision: maximalist value
objects, hand-rolled command handlers + Wolverine dispatcher,
`Result<T, ApiError>`, **Postgres + EF Core** (state-based; per
ADR-0009, Marten is reserved for event-sourced contexts), RabbitMQ
outbox with per-context queue isolation, RFC 7807 error responses,
Keycloak-issued admin token, React form with React Hook Form + Zod,
and the Aspire-orchestrated dev loop.

**Independent Test:**

1. Start the system locally via `aspire run`.
2. Sign in to `management-web` as an admin operator.
3. Submit a new camera with a unique name and a syntactically valid
   RTSP URL.
4. Observe the camera appears in the catalog list within 2 seconds.
5. Inspect the RabbitMQ outbox (or the inspector UI) and see a
   `CameraRegisteredV1` integration event corresponding to the new
   camera.
6. Replay the test against a real fab Postgres + RabbitMQ stack
   (integration test via Aspire fixture per ADR-0068).

**Acceptance Scenarios:**

1. **Given** an authenticated admin and an empty camera catalog,
   **When** the admin submits `{ name: "Line-1-Entrance",
   rtspUrl: "rtsp://10.0.5.12:554/h264" }`,
   **Then** the system returns `201 Created` with the new camera's
   identifier in the response body and in the `Location` header.
2. **Given** an authenticated admin and an existing camera named
   `Line-1-Entrance`,
   **When** the admin submits a second registration with the same name,
   **Then** the system returns `409 Conflict` (RFC 7807 Problem Details)
   with `code: CAMERA_NAME_TAKEN`, and no second camera is persisted.
3. **Given** an authenticated admin,
   **When** the admin submits a registration with an empty name,
   **Then** the system returns `400 Bad Request` with field-level error
   `name` failing the not-empty constraint, and no camera is persisted.
4. **Given** an authenticated admin,
   **When** the admin submits a registration with an RTSP URL that
   does not start with `rtsp://`,
   **Then** the system returns `400 Bad Request` with field-level error
   `rtspUrl` failing the scheme constraint.
5. **Given** an authenticated admin successfully registers a camera,
   **When** any subscribed bounded context inspects the RabbitMQ
   queue `<consumer-prefix>.SmartSentinelEye.Shared.Contracts.CameraRegisteredV1`,
   **Then** exactly one message corresponding to the new camera is
   present, carrying the `CameraIdentifier`, `name`, and `rtspUrl`.
6. **Given** an unauthenticated request OR an authenticated user
   without the `admin` scope,
   **When** the request is made,
   **Then** the system returns `401 Unauthorized` or `403 Forbidden`
   respectively, with no side effects.

---

### User Story 2 — List registered cameras (Priority: P1)

An admin (or a system that needs to enumerate cameras) requests the
catalog and receives the cameras that have been registered.

**Why this priority:** Equal priority with US-1 because the
"successfully registered" assertion in US-1's acceptance criteria is
verified by listing — without listing, registration is unobservable
from the UI. Together US-1 + US-2 form the smallest publishable slice.

**Independent Test:**

1. Register two cameras via US-1 (`Cam-A`, `Cam-B`).
2. Request the catalog list with default parameters
   (`GET /cameras?sort=registeredAt&order=desc&offset=0&limit=50`).
3. Receive a response containing both cameras with at minimum
   `cameraIdentifier`, `name`, `rtspUrl`, `registeredAt`, plus a
   total `count` so the client can render pagination controls.
4. The list ordering is **deterministic** and **client-controlled**
   via `?sort=name|registeredAt` and `?order=asc|desc`.

**Acceptance Scenarios:**

1. **Given** two cameras have been registered,
   **When** the admin requests `GET /cameras?sort=registeredAt&order=desc`,
   **Then** the response is `200 OK` with `{ items: [...], count: 2,
   offset: 0, limit: 50 }`, items ordered newest-first.
2. **Given** zero cameras registered,
   **When** the catalog is listed,
   **Then** the response is `200 OK` with
   `{ items: [], count: 0, offset: 0, limit: 50 }`.
3. **Given** an unauthenticated request,
   **When** the catalog is listed,
   **Then** the system returns `401 Unauthorized`.
4. **Given** 75 cameras are registered,
   **When** the admin requests `GET /cameras?offset=50&limit=50`,
   **Then** the response is `200 OK` with `items.length == 25` and
   `count == 75`.
5. **Given** the admin requests an invalid sort field
   (`GET /cameras?sort=rtspUrl`),
   **When** the request is processed,
   **Then** the system returns `400 Bad Request` with code
   `CATALOG_INVALID_SORT_FIELD` and `detail` enumerating the
   accepted sort fields (`name`, `registeredAt`).

---

### User Story 3 — Decommission a registered camera (Priority: P3)

An admin marks a registered camera as decommissioned so it is no longer
considered active by downstream services. Pure removal is not the goal —
audit history of the camera's existence must be retained.

**Why this priority:** Out of scope for the walking skeleton but
necessary before v1 GA. Listed here so US-1 and US-2 are designed in a
way that admits a future status flag without rework.

**Independent Test:** Register a camera, decommission it, confirm it
no longer appears in active listings but does appear when including
decommissioned cameras, and a `CameraDecommissionedV1` event is
published. *(Deferred to a separate spec.)*

---

### Edge Cases

- **Duplicate name (case sensitivity):** Two cameras named
  `line-1-entrance` and `Line-1-Entrance` are the **same camera** by
  the uniqueness rule. Comparison is case-insensitive (invariant
  culture). The originally-submitted casing is persisted and shown
  verbatim in the list. The DB enforces this via a unique index on
  `LOWER(name)`.
- **Whitespace in names:** Leading/trailing whitespace is trimmed
  before validation and before the uniqueness check.
- **Maximum length:** Camera name length cap of **200 characters**
  (matches the existing maximalist VO convention for free-text fields).
- **RTSP URL with credentials:** URLs containing `user:password@` are
  **rejected** with `400 Bad Request`, code `CAMERA_INVALID_URL`. The
  catalog never stores camera credentials in this spec; credentialed
  cameras require a follow-up spec that introduces a `CameraSecret`
  aggregate / secret-store reference.
- **Network unreachability at registration time:** Registration does
  **not** probe the camera. The URL is syntax-validated, the camera
  is persisted, the integration event is published. A follow-up
  *Camera Health* feature in the Camera Catalog context performs
  ongoing reachability and surfaces status to operators.
- **Pagination of the catalog list:** Offset/limit pagination is
  **mandatory** from v1. Defaults: `offset=0`, `limit=50`. Maximum
  `limit=200` (anything higher is rejected with `400 Bad Request`,
  code `CATALOG_LIMIT_EXCEEDED`).
- **Sort fields and order:** The list accepts `?sort=name|registeredAt`
  (default `registeredAt`) and `?order=asc|desc` (default `desc`).
  Unknown sort field → `400 Bad Request`, code
  `CATALOG_INVALID_SORT_FIELD`. Unknown order value → `400 Bad Request`,
  code `CATALOG_INVALID_SORT_ORDER`.
- **Concurrent registration of the same name by two admins:**
  Optimistic concurrency at the catalog level — second registration
  receives `409 Conflict`.
- **Empty payload / missing fields:** `400 Bad Request` with a
  Problem Details enumerating the missing fields.
- **Extremely long URL:** Hard cap of 2048 chars (RFC suggested URL
  limit). Rejected with `400 Bad Request`, code `CAMERA_INVALID_URL`.
- **Camera registered while RabbitMQ is down:** Registration succeeds
  in the database; the integration event sits in the Postgres outbox
  until RabbitMQ is reachable again (ADR-0088 outbox guarantees
  at-least-once delivery).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001:** Admin users MUST be able to register a new camera by
  providing a unique name and an RTSP URL.
- **FR-002:** The system MUST assign every newly-registered camera a
  unique, stable, sortable `CameraIdentifier` (Guid v7 per ADR-0090,
  client-generatable so the same identifier can be returned in the
  response and in the integration event).
- **FR-003:** The system MUST validate that the submitted RTSP URL
  uses the `rtsp://` scheme.
- **FR-004:** The system MUST reject duplicate camera names with a
  conflict response (`409` + RFC 7807 Problem Details, code
  `CAMERA_NAME_TAKEN`).
- **FR-005:** The system MUST persist every registered camera durably
  in Postgres via **EF Core** (state-based, per ADR-0009 — Camera
  Catalog is not event-sourced). Migrations are managed by the
  `MigrationRunner` worker (ADR-0067) and execute before any Api
  service starts. The Wolverine transactional outbox shares the same
  EF Core `DbContext` (ADR-0088).
- **FR-006:** The system MUST publish a `CameraRegisteredV1` integration
  event onto the cross-context message bus after the camera is
  persisted, exactly-once relative to acknowledged delivery (transactional
  outbox per ADR-0088).
- **FR-007:** Admin users MUST be able to list the cameras currently in
  the catalog. The response MUST include for each camera:
  `cameraIdentifier`, `name` (original casing), `rtspUrl`, and
  `registeredAt`. The response MUST also include the total `count`
  matching the query, the effective `offset`, and the effective
  `limit`.
- **FR-007a (sorting):** The list endpoint MUST accept `sort` and
  `order` query parameters. Allowed `sort` values: `name`,
  `registeredAt`. Allowed `order` values: `asc`, `desc`. Defaults:
  `sort=registeredAt`, `order=desc`. Invalid values MUST be rejected
  with `400 Bad Request`.
- **FR-007b (pagination):** The list endpoint MUST accept `offset`
  and `limit` query parameters. Defaults: `offset=0`, `limit=50`.
  Maximum `limit=200`. Higher values MUST be rejected with `400 Bad
  Request`. Negative values MUST be rejected.
- **FR-008:** The system MUST enforce field-level validation at the
  API boundary: `name` 1–200 chars after trim and unique
  case-insensitively; `rtspUrl` 1–2048 chars, starting with
  `rtsp://`, and containing no `user:password@` userinfo segment.
- **FR-009:** The system MUST return RFC 7807 Problem Details for every
  validation, conflict, and authorization failure, carrying the error
  `code` and a human-readable `detail`. Codes defined for this spec:
  `CAMERA_NAME_TAKEN`, `CAMERA_NAME_INVALID`, `CAMERA_URL_INVALID`,
  `CATALOG_INVALID_SORT_FIELD`, `CATALOG_INVALID_SORT_ORDER`,
  `CATALOG_LIMIT_EXCEEDED`.
- **FR-010:** Only authenticated users carrying the `admin` scope (per
  ADR-0023) MUST be allowed to register or list cameras. All other
  requests MUST be rejected with `401` (unauthenticated) or `403`
  (authenticated but unauthorized).
- **FR-011:** Every successful registration MUST appear in the audit
  log (Audit & Observability context) with actor, timestamp, and
  camera identifier.
- **FR-012:** Registration latency from request arrival at the API to
  response sent MUST be ≤ 200 ms at the **p95** under nominal load (this
  is the "command-to-state" leg; we already have the larger
  event-to-overlay budget in ADR-0015 — this is the sub-budget for the
  command path).
- **FR-013:** The integration event MUST be visible to subscribers in
  the same Postgres transaction as the database write (outbox
  guarantees), so a successful response implies the event will be
  delivered.

### Key Entities

- **Camera (aggregate root):** A registered IP camera. Owns:
  `cameraIdentifier`, `name`, `rtspUrl`, `registeredAt`,
  `registeredBy` (operator identifier),
  `status` (`Registered` for now; `Decommissioned` reserved for US-3),
  `version` (concurrency control per ADR-0043).
- **CameraName (value object):** Trimmed string, 1–200 chars,
  case-insensitive uniqueness with original casing preserved.
  `IValueObject<string>`.
- **RtspUrl (value object):** Validates `rtsp://` scheme, 1–2048
  chars, no userinfo segment. `IValueObject<string>`. Credentials are
  rejected at the value-object boundary; credentialed cameras are a
  follow-up spec.
- **CameraIdentifier (value object):** `Guid` v7 backed,
  `IValueObject<Guid>`, generated by `Guid.CreateVersion7()`
  per ADR-0090.
- **CameraRegisteredV1 (integration event in `Shared.Contracts`):**
  `{ CameraIdentifier, CameraName, RtspUrl, RegisteredAt,
  RegisteredBy }`. Versioned per ADR-0073.
- **CameraRegisteredDomainEvent (in `CameraCatalog.Domain/Camera/Events/`):**
  Internal domain event raised by the `Camera` aggregate; never leaves
  the context. Translated to `CameraRegisteredV1` by a handler before
  publication (ADR-0040).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001:** Admin can register a camera and see it in the catalog
  list within **2 seconds** of the form submit (UI-perceived latency,
  end-to-end including network).
- **SC-002:** Duplicate name conflict response arrives within
  **200 ms** at p95 from API arrival.
- **SC-003:** **100 %** of validation failures surface a field-level
  error in the UI; no opaque "something went wrong" outcomes.
- **SC-004:** Integration test (Aspire fixture per ADR-0068) verifies
  that after a successful registration, the `CameraRegisteredV1` event
  is delivered to a probe consumer within **1 second**.
- **SC-005:** Architecture tests still pass — no new
  cross-context references introduced (ADR-0027) and the Domain layer
  still has no infrastructure dependencies (ADR-0044).
- **SC-006:** Coverage thresholds from ADR-0065 hold: Camera Catalog
  Domain ≥ 90 %, Application ≥ 80 %, Shared.Contracts ≥ 90 % after
  this slice lands.

## Assumptions

- **Authentication is in place.** Identity bounded context exposes a
  working Keycloak realm with at least one admin operator. The
  walking-skeleton spec assumes a hand-seeded admin; production
  user provisioning is out of scope here.
- **Aspire AppHost orchestrates the dev stack.** Postgres, RabbitMQ,
  and Keycloak start via `aspire run`.
- **The registering operator is identified by an `OperatorIdentifier`
  derived from the validated JWT subject claim.** No new identity work
  is in scope here.
- **Audit log integration is best-effort for the walking skeleton.**
  A dedicated audit handler in the Audit & Observability context can
  subscribe to `CameraRegisteredV1`; if the audit handler is not yet
  in place, the camera is still persisted and the event is still
  published.
- **Decommissioning is deferred (US-3).** The `status` field exists in
  the aggregate but only takes the value `Registered` in this slice.

## Resolved Clarifications (Phase 2)

All six clarification markers are resolved as follows:

| # | Marker | Resolution |
|---|---|---|
| 1 | Persistence flavour | **EF Core** with Postgres provider; migrations via `MigrationRunner` |
| 2 | Name uniqueness comparison | **Case-insensitive** (invariant culture), original casing preserved |
| 3 | Credentials in RTSP URL | **Reject** any URL containing `user:password@`; credentialed cameras deferred to a follow-up spec |
| 4 | Reachability check at registration | **Skip**; reachability is the future Camera Health feature's job |
| 5 | Catalog list ordering | **Client-controlled** via `?sort=name|registeredAt&order=asc|desc`; defaults `registeredAt desc` |
| 6 | Catalog list pagination | **Offset/limit** from v1 — `?offset=0&limit=50`, max `limit=200` |
