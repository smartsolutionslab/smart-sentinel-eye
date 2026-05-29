# Feature Specification: Identity — Keycloak-anchored auth for every persona

**Feature Branch:** `008-identity`

**Created:** 2026-05-29

**Status:** Draft (Phase 1 — Specify)

**Input:** Eighth feature of Smart Sentinel Eye — the first
end-to-end slice through the **Identity** bounded context. Up to
now every backend service has leaned on a thin Keycloak shim
from `ServiceDefaults.AuthenticationDefaults` and a single
`AdminPolicy` mapped to the `sse.management` scope; PLC + camera
devices authenticate against a Mosquitto password file (spec 006
ADR-0095); kiosks bind via the device-flow stub described in
ADR-0008. Spec 008 replaces all of that with a coherent Identity
context: one shared Keycloak realm with **resource-shaped scopes**
gating every endpoint, **four personas** (admin, operator, kiosk,
device) issued by Keycloak, **groups** (`/fabs/<fabId>`) carrying
tenant membership in the JWT, and **device JWTs** validated by
Mosquitto via `mosquitto-go-auth` against the cached JWKS.

v1 is **backend + Keycloak realm bootstrap + admin REST tooling
for runtime provisioning**. The in-app onboarding UI (operator
invitations, kiosk enrollment wizard, device-token issuance UI)
is deferred to a follow-on spec; v1 admins use the existing
Keycloak admin console for static config and the new REST endpoints
for dynamic device/kiosk creation.

The single load-bearing decision is the scope taxonomy:
**`sse.<resource>.<verb>`**. Every existing endpoint across
specs 001–007 is re-mapped to one or more resource scopes, with
the legacy `sse.management` grandfathered as a bundle for the
existing admin clients.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Admin signs in and authors a rule (Priority: P1)

An admin opens management-web, signs in via Keycloak's OIDC
auth-code-with-PKCE flow against the shared realm
`smart-sentinel-eye`. Keycloak emits a JWT carrying the scopes
`sse.cameras.read`, `sse.rules.read`, `sse.rules.write`,
`sse.variables.write`, `sse.layouts.write`, `sse.overlays.write`,
`sse.events.read`, `sse.events.write` (the admin scope bundle) plus
the `groups: ["/fabs/munich"]` claim. They click **Rules → New
rule**, fill in the form, and submit. The `POST /rules` endpoint
verifies the JWT, asserts the `sse.rules.write` scope, asserts
the rule's target fab is in the caller's `groups`, and lets the
spec 007 handler do its job.

**Why this priority:** All other authenticated flows derive from
this one. Exercises Keycloak issuance, the cached JWKS validator,
the resource-scope policy, the groups-based fab guard, and the
expected end-to-end shape.

**Independent Test:**

1. `aspire run` brings up the shared Keycloak realm with the
   v1 scope catalogue + a seed admin user
   (`admin@munich.test`, group `/fabs/munich`).
2. Open `management-web`, click **Sign in**, complete the
   Keycloak prompt.
3. Click **Rules → New rule**, fill in the form, save. Expect
   201; check the JWT payload in the network tab carries
   `sse.rules.write` + `groups: ["/fabs/munich"]`.

**Acceptance Scenarios:**

1. **Given** an admin user in group `/fabs/munich` with the
   admin scope bundle, **when** they POST a rule scoped to
   `munich`, **then** the request succeeds (201).
2. **Given** the same admin, **when** they try to POST a rule
   targeting fab `berlin` (not in their `groups`), **then** the
   request is rejected with 403 + a `RULE_FAB_NOT_AUTHORIZED`
   error code.
3. **Given** an admin user missing the `sse.rules.write` scope
   (e.g. a read-only admin), **when** they POST a rule, **then**
   the request is rejected with 403 + `SCOPE_MISSING`.

---

### User Story 2 — Operator submits a manual event from the kiosk (Priority: P1)

An operator at the kiosk taps **Annotate**, picks "Defect", types
a note, submits. The kiosk-bound session JWT (issued via the kiosk
flow in US3) carries `sse.events.write` plus the kiosk's fab group
membership. `POST /events/manual` accepts; the spec 006 handler
runs.

**Why this priority:** The spec 006 kiosk write path currently
falls back to `AdminPolicy` because no operator scope exists.
This story carves it out — operators have `sse.events.write`
without the broader `sse.rules.write` / `sse.variables.write`
authority an admin holds.

**Independent Test:**

1. Pre-conditions: an operator user `op-3@munich.test` exists in
   group `/fabs/munich` with the operator scope bundle
   (`sse.events.write`, `sse.cameras.read`, `sse.layouts.read`,
   `sse.overlays.read`, `sse.variables.read`).
2. Operator signs in to kiosk-web; the kiosk binding handshake
   from US3 yields a session JWT.
3. Operator taps **Annotate**, submits.
4. `POST /events/manual?fabId=munich` returns 202; the spec 006
   read API shows the new event with `source: manual`,
   `device: kiosk-3`.

**Acceptance Scenarios:**

1. **Given** an operator with `sse.events.write` + group
   `/fabs/munich`, **when** they POST a manual event scoped to
   `munich`, **then** the request succeeds (202).
2. **Given** the same operator, **when** they try to POST to
   `/rules` (admin-only), **then** the request is rejected with
   403 + `SCOPE_MISSING` (operators don't get rule authorship).

---

### User Story 3 — Kiosk binds to a physical screen and gets a session JWT (Priority: P1)

A new physical kiosk boots a stock Chromium against
`kiosk-web/?bind=true`. The admin scans the kiosk's displayed
QR code in `management-web`, clicks **Bind kiosk**, selects the
target fab + camera assignment. The backend (Identity API) calls
Keycloak Admin API to create a new `kiosk-<id>` client with the
kiosk scope bundle (`sse.cameras.read`, `sse.layouts.read`,
`sse.overlays.read`, `sse.variables.read`, `sse.events.write`)
and the `/fabs/<fabId>` group attribute. The Identity API
returns a one-time enrollment JWT; the kiosk uses it to
exchange for a long-lived refresh token + short-lived access token.

**Why this priority:** Kiosks are the most security-sensitive
client (they're physically accessible, run unattended). ADR-0008
sketched this; spec 008 ships it.

**Independent Test:**

1. Boot a fresh `kiosk-web` instance pointed at the dev Identity
   API. The UI shows an enrollment QR code.
2. In management-web, an admin opens **Kiosks → Bind**, scans
   the code (or pastes the displayed identifier), picks fab
   `munich`, picks layout `line-A`, clicks **Bind**.
3. The kiosk auto-refreshes; the layout starts rendering.

**Acceptance Scenarios:**

1. **Given** a fresh kiosk session, **when** an admin binds it,
   **then** the kiosk receives an enrollment JWT and successfully
   exchanges for a session token within ≤ 2 s.
2. **Given** an already-bound kiosk, **when** anyone tries to
   re-bind it without an explicit "unbind" first, **then** the
   re-bind is rejected with 409.
3. **Given** a kiosk's session token expires (15 min TTL),
   **when** the kiosk uses its refresh token (7 d TTL), **then**
   it transparently receives a new session token without admin
   intervention.

---

### User Story 4 — PLC device publishes via MQTT using a Keycloak-issued JWT (Priority: P1)

A factory floor PLC at `station-4` connects to Mosquitto with
`Username=plc-station-4`, `Password=<JWT>` where the JWT is a
Keycloak client-credentials token for the `plc-station-4` service
account. The `mosquitto-go-auth` plugin verifies the JWT against
the cached Keycloak JWKS (24 h cache), asserts the scope
`sse.events.publish`, and confirms the JWT's `groups` claim
includes `/fabs/munich`. ACL: only topics under
`fab/munich/plc/station-4` are publishable. PLC publishes; spec
006 ingests; downstream pipeline proceeds.

**Why this priority:** Replaces the Mosquitto password file
(spec 006). This is the biggest operational change in spec 008
and the highest-stakes from a security perspective.

**Independent Test:**

1. In Identity API, `POST /devices/register` with
   `{ "deviceType": "plc", "deviceId": "station-4", "fabId": "munich" }`.
   Identity calls Keycloak Admin API to create the
   `plc-station-4` client, returns the client secret.
2. In a shell, run a `client_credentials` grant against Keycloak
   with the client's secret to mint a 24 h device JWT.
3. `mosquitto_pub -h localhost -p 1883 -u plc-station-4
   -P <jwt> -t fab/munich/plc/station-4 -m '<json>'`.
4. Expect the publish to succeed; spec 006 `GET /events` shows
   the row.

**Acceptance Scenarios:**

1. **Given** a registered `plc-station-4` device, **when** it
   publishes a valid JWT-authenticated message to its own topic,
   **then** the publish succeeds (Mosquitto ACK).
2. **Given** the same device, **when** it tries to publish to
   `fab/berlin/plc/station-4` (outside its `/fabs/munich` group),
   **then** the publish is rejected by the ACL.
3. **Given** an expired device JWT (24 h ago), **when** it
   reconnects, **then** Mosquitto rejects the connection;
   the device must mint a fresh JWT via client_credentials.

---

### User Story 5 — Webhook integration rotates its bearer to a Keycloak JWT (Priority: P2)

An existing spec 006 webhook integration (`qa`) is grandfathered
on a static bearer. The admin clicks **Rotate token** in the
existing webhook-integrations UI; the rotation flow now goes
through Identity: it creates a `webhook-qa` Keycloak client with
the `sse.events.write` scope + `/fabs/munich` group, returns a
fresh client secret. The integration owner updates their config
to use the client-credentials grant and gets a new JWT. The
endpoint `POST /events/webhook/qa` validates the JWT instead of
the static bearer.

**Why this priority:** P2 because the migration is **hard-cut**
(no dual-validate window) — only integrations that rotate get the
new flow. v1 is the rotation path; existing static bearers stay
working until rotated.

**Independent Test:**

1. Pre-conditions: webhook integration `qa` was created in spec
   006 with a static bearer. POST to `/events/webhook/qa` with
   that bearer still works.
2. Admin clicks **Rotate** on `qa`; the new flow returns a
   client secret + sample `curl` snippet for the
   client_credentials grant.
3. The integration owner runs the grant, gets a JWT, POSTs to
   `/events/webhook/qa` with `Authorization: Bearer <jwt>`.
   Expect 202; the spec 006 dedup unique-constraint applies as
   before.

**Acceptance Scenarios:**

1. **Given** a rotated integration, **when** it presents a valid
   JWT, **then** the request succeeds.
2. **Given** an un-rotated integration, **when** it presents its
   legacy static bearer, **then** the request still succeeds
   (grandfathered).
3. **Given** a rotated integration, **when** it presents the
   old static bearer, **then** the request is rejected with
   401 (rotation invalidates the legacy bearer).

---

### User Story 6 — Cross-fab admin sees both fabs (Priority: P2)

A corporate admin `super-admin@hq.test` is in two groups:
`/fabs/munich` AND `/fabs/berlin`. Their JWT carries
`groups: ["/fabs/munich", "/fabs/berlin"]`. When they
`GET /rules?fabId=munich`, they see munich rules; when they
`GET /rules?fabId=berlin`, they see berlin rules. When they
try to access a third fab they don't belong to, 403.

**Why this priority:** P2 because cross-fab admins are a smaller
constituency, but the groups-based design assumes this works.

**Acceptance Scenarios:**

1. **Given** a multi-fab admin, **when** they query each fab's
   data with the corresponding `fabId`, **then** each query
   succeeds.
2. **Given** the same admin, **when** they query a fab outside
   their groups, **then** 403.

---

## Functional Requirements

### Realm + scope catalogue
- **FR-001** A single Keycloak realm `smart-sentinel-eye`
  is bootstrapped via `src/AppHost/Realms/sse-realm.json`. The
  realm defines:
  - Standard OIDC client `management-web` (auth-code+PKCE).
  - Standard OIDC client `kiosk-web` (auth-code+PKCE for the
    admin-driven binding flow).
  - Service-account base client `identity-admin` (used by the
    Identity API to call Keycloak's Admin REST API).
  - Scope catalogue (FR-002).
  - Default groups: `/fabs/munich` (seeded; additional fab
    groups created at provisioning time).
- **FR-002** The scope catalogue uses the shape
  `sse.<resource>.<verb>`. v1 ships these scopes:

  | Scope                       | Granted to                          |
  | --------------------------- | ----------------------------------- |
  | `sse.cameras.read`          | admin, operator, kiosk              |
  | `sse.cameras.write`         | admin                               |
  | `sse.streams.read`          | admin, kiosk                        |
  | `sse.streams.write`         | admin                               |
  | `sse.layouts.read`          | admin, operator, kiosk              |
  | `sse.layouts.write`         | admin                               |
  | `sse.overlays.read`         | admin, operator, kiosk              |
  | `sse.overlays.write`        | admin                               |
  | `sse.variables.read`        | admin, operator, kiosk              |
  | `sse.variables.write`       | admin                               |
  | `sse.rules.read`            | admin                               |
  | `sse.rules.write`           | admin                               |
  | `sse.events.read`           | admin                               |
  | `sse.events.write`          | admin, operator (HTTP `/events/manual`) |
  | `sse.events.publish`        | device (MQTT publish)               |
  | `sse.webhooks.write`        | admin                               |
  | `sse.kiosks.write`          | admin                               |
  | `sse.identity.devices.write`| admin (creates device clients)      |
  | `sse.identity.kiosks.write` | admin (creates kiosk clients)       |

  The legacy `sse.management` scope is grandfathered as a bundle
  of every `*.write` scope above except `events.publish`.

### Tenant membership
- **FR-003** Fab membership is encoded as Keycloak groups of the
  form `/fabs/<fabId>`. Group membership is emitted into the JWT
  via the standard `groups` claim.
- **FR-004** Every fab-scoped endpoint checks that the caller's
  `groups` claim contains `/fabs/<fabIdFromRequest>`. Mismatch
  → 403 with `RESOURCE_FAB_NOT_AUTHORIZED`. The check lives in a
  shared `IFabAuthorizationGuard` in `ServiceDefaults`.

### Personas + tokens
- **FR-005** Four personas exist in v1: admin, operator, kiosk,
  device. Each has a Keycloak scope bundle (FR-002) and a
  conventional refresh-token lifetime:

  | Persona  | Access TTL | Refresh TTL | Notes |
  | -------- | ---------- | ----------- | ----- |
  | admin    | 15 min     | 8 h         | auth-code+PKCE |
  | operator | 15 min     | 8 h         | auth-code+PKCE |
  | kiosk    | 15 min     | 7 d         | enrollment-token + client_credentials |
  | device   | 24 h       | 30 d        | client_credentials only |

- **FR-006** Token revocation latency is **bounded by the access
  TTL** because every backend caches the Keycloak JWKS for 24 h.
  Emergency revocation = rotate the Keycloak signing key
  (`kid` rolls; every outstanding token invalidates at the next
  JWKS refresh). Documented in the runbook.

### MQTT device auth (mosquitto-go-auth)
- **FR-007** Mosquitto is configured with the `mosquitto-go-auth`
  plugin in JWT mode: `Username=<keycloakClientId>`,
  `Password=<jwt>`. The plugin caches the realm's JWKS for 24 h
  and validates the JWT's signature + expiry on every connect.
- **FR-008** The plugin's ACL is JSON-from-JWT: a publish to
  topic `fab/<fabId>/<source>/<deviceId>` is allowed iff the
  JWT carries scope `sse.events.publish` AND
  `groups` contains `/fabs/<fabId>` AND `clientId` ends with
  `<source>-<deviceId>` (e.g. `plc-station-4`,
  `inference-camera-12`).
- **FR-009** Devices that have not registered fall back to
  legacy Mosquitto password-file auth for one release cycle (a
  follow-on spec hard-cuts the password file).

### Identity API
- **FR-010** `POST /devices/register` (admin only,
  `sse.identity.devices.write`) creates a Keycloak service-account
  client for a new device. Body: `{ deviceType, deviceId, fabId }`.
  Returns: `{ clientId, clientSecret }` — the secret is shown
  exactly once. Idempotent on
  `(deviceType, deviceId)` — re-registering returns 409.
- **FR-011** `DELETE /devices/{clientId}` (admin only,
  `sse.identity.devices.write`) disables the Keycloak client. The
  next time the device's JWT expires, the device can no longer
  reconnect.
- **FR-012** `POST /kiosks/enroll` (admin only,
  `sse.identity.kiosks.write`) creates a kiosk Keycloak client
  with kiosk scope bundle + fab group. Returns
  `{ clientId, clientSecret, enrollmentToken }`. The
  `enrollmentToken` is a one-time-use JWT (15 min TTL) the kiosk
  presents to exchange for a refresh+access pair.
- **FR-013** `DELETE /kiosks/{clientId}` (admin only) disables
  the kiosk client.
- **FR-014** `POST /webhook-integrations/{name}/rotate` (admin
  only, `sse.webhooks.write`) replaces the integration's existing
  static bearer with a Keycloak service-account client. Returns
  `{ clientId, clientSecret, sampleCurl }`. The static bearer is
  invalidated atomically with the client creation; the next
  `/events/webhook/{name}` call must present a JWT.
- **FR-015** `GET /devices` and `GET /kiosks` (admin only) list
  the registered clients with their scopes + fab.

### Migration / grandfathering
- **FR-016** Existing webhook integrations (created in spec 006)
  continue to accept their static bearer indefinitely until the
  admin invokes `POST /webhook-integrations/{name}/rotate`.
  After rotation, the static bearer is permanently dropped.
- **FR-017** Existing Mosquitto password-file users (PLCs +
  inference cameras created before spec 008) continue to
  authenticate via the password file for one release cycle. The
  follow-on spec (008a) hard-cuts the password file.

### Authorization helpers in ServiceDefaults
- **FR-018** A new `RequireScope` extension on
  `AuthorizationOptions` lets endpoints declare:
  `.RequireAuthorization(Scope.SSE.Rules.Write)`. The
  `Scope` static class is the single source of truth for the
  catalogue.
- **FR-019** A new `IFabAuthorizationGuard` is registered in
  `ServiceDefaults`; consumed by every fab-scoped endpoint:
  `await fabGuard.EnsureAccessAsync(user, fabId);` Throws
  `FabAuthorizationException` (mapped to 403) on mismatch.

### Existing-endpoint scope mapping
- **FR-020** Every existing endpoint across specs 001–007 is
  re-mapped to one or more resource scopes via FR-002. The
  legacy `AdminPolicy` (now bundled as `sse.management`) stays
  configured but is marked deprecated; specific scope checks
  layer on top in PR-by-PR migration.

## Non-Functional Requirements

- **NFR-001** JWT validation overhead: **≤ 500 µs p99 per
  request** for HTTP endpoints (cached JWKS, no Keycloak
  round-trip in the hot path).
- **NFR-002** MQTT connect-time auth: **≤ 5 ms p99 per device
  reconnect** (cached JWKS; signature verification + claim
  extraction). Per-message overhead is **zero** (auth is
  per-connect, not per-message).
- **NFR-003** Keycloak Admin API calls (device/kiosk creation):
  **≤ 200 ms p95** for the create/rotate operations.
- **NFR-004** JWKS cache TTL: 24 h. Forced refresh on signature
  validation failure (handles signing-key rotation transparently).
- **NFR-005** Scale: 1 000 device clients per fab; 50 kiosks
  per fab; 500 human users per realm. Above this revisit the
  Keycloak deployment shape (single instance per env in v1).

## Out of Scope (deferred or rejected)

- **In-app onboarding UI** — operator invitation flow, kiosk
  enrollment wizard, device-token issuance form. v1 admins use
  the Keycloak admin console for static config + the new REST
  endpoints for dynamic provisioning. Custom UI = spec 008a.
- **Hard-cut of the Mosquitto password file** — kept as a
  fallback for one release. Removal = spec 008b.
- **Dual-validate window for webhook migration** — hard-cut on
  rotation only (admins migrate at their own pace).
- **Per-fab realms** — single shared realm for v1.
- **SCIM / external IdP federation** — humans authenticate
  natively against Keycloak in v1.
- **Self-service human signup** — admin-provisioned only in
  v1.
- **Fine-grained Keycloak Authorization Services (RBAC/PBAC)**
  — scope-based gating is enough for v1; consider PBAC when the
  scope count exceeds 30.
- **Audit log of authentication events** — Keycloak's own event
  log covers this in v1; surfacing it in management-web is spec
  009 (AuditObservability).

## Cross-Context Reach

Identity touches **every** existing context's API surface
because every endpoint gets a new scope mapping. The reach is
**additive only**: the new `Scope` constants + `IFabAuthorizationGuard`
live in `ServiceDefaults`, so no new cross-context project
references are needed. The Keycloak Admin client lives in
`Identity.Infrastructure`; only the Identity API consumes it.

Two new V1 contracts in `Shared.Contracts/Identity/`:

- `DeviceRegisteredV1` — published when a device client is
  created (audit hook for spec 009).
- `KioskEnrolledV1` — published when a kiosk is bound (drives
  the live kiosk inventory in management-web).

No new `AllowedCrossContext` entries.

## Constitution Check

- **§I (walking skeleton):** Identity formalises the auth surface
  the walking skeleton has been faking with `sse.management`.
- **§II (locked tech stack):** No tech additions — Keycloak is
  already locked. The `mosquitto-go-auth` plugin **is** a new
  Mosquitto-side component and requires **ADR-0100**.
- **§III (bounded-context isolation):** Identity has no
  cross-context project refs. The `Scope` constants in
  `ServiceDefaults` are shared infrastructure (allowed per
  ADR-0051).
- **§IV (latency budget):** Per-request JWT validation
  ≤ 500 µs p99; well under any context's per-request budget.
  MQTT connect-time ≤ 5 ms p99; per-message overhead zero so
  spec 006's 50 ms ingest budget is preserved.
- **§V (spec-driven):** This spec. Plan + tasks follow.
- **§VI (Aspire composition root):** The shared realm is
  bootstrapped via `WithRealmImport` (already wired in AppHost).
  Identity API gets a new `identity` Aspire project.
- **§VII (no event sourcing without justification):** Keycloak
  is the system of record for identity; Identity's own DB stores
  only the dedup/audit table mapping `(deviceType, deviceId)` →
  `clientId`.
- **§VIII (safe at trust boundaries):** Every endpoint asserts
  the right scope + fab group at the edge. JWT validation
  happens in `ServiceDefaults` middleware.
- **§IX (forward-compatible interfaces):** The `Scope` catalogue
  is additive. New scopes don't break callers; missing scopes
  surface as typed 403 errors.

## Gate (Phase 1 → Phase 2)

This spec is ready for the Plan phase once the architect lead
confirms:

1. No `[NEEDS CLARIFICATION]` markers remain. ✅
2. The six user stories cover the v1 product surface.
3. The scope catalogue (FR-002) is correct + complete for every
   existing endpoint.
4. The hard-cut webhook migration model is acceptable (no
   dual-validate window).
5. The mosquitto-go-auth choice is acceptable as a new
   Mosquitto-side component (ADR-0100 in PR A).
6. The 15-min / 24-h TTL choices are acceptable given the
   24 h JWKS cache → revocation-via-TTL constraint.

When the gate is approved, Phase 2 (`/speckit-plan`) drafts
`plan.md` against the locked stack — `identity-admin` Keycloak
service client, an Identity API project hosting
`/devices`, `/kiosks`, `/webhook-integrations/.../rotate`,
the `Scope` catalogue + `IFabAuthorizationGuard` in
ServiceDefaults, the `mosquitto-go-auth` config, and the
per-endpoint scope migration across specs 001–007.
