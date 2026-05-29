# Smart Sentinel Eye

[![CI](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/workflows/ci.yml)

> Professional camera management system for industrial production fabs.
> 24/7 operation. WebRTC streaming. On-prem first, cloud-ready.

Smart Sentinel Eye unifies hundreds of IP cameras across an industrial
plant into a single low-latency video wall, with dynamic overlays driven
by events from MES, SCADA, and other shop-floor systems.

**Status:** specs `001-register-camera`, `002-watch-camera-live`,
`003-layout-composition`, and `004-overlay-designer` shipped end-to-end.
Admins register cameras, author named layouts, compose WYSIWYG text
overlays, and publish the lot. Operators at a kiosk sign in, pick a
published layout, and see the live WebRTC frame with the bound overlay
composited on top. Republishing the overlay pushes the new label to
every connected kiosk within 1 s via SignalR.

## What it does

- Ingests RTSP / ONVIF streams from up to 250 industrial IP cameras.
- Distributes them as WebRTC to operator browsers and control-room
  video walls (frame-accurate sync via PTP).
- Composites dynamic overlays on top of live streams, driven by
  registered external events and typed system variables.
- Lets operators steer cells, switch cameras, and control PTZ from a
  browser; lets admins author layouts, overlays, and automation rules
  through a draft → preview → publish workflow.
- Runs entirely on-prem per fab; a cloud control plane is the v2
  competitive feature.

## Architecture at a glance

| Layer | Choice |
|---|---|
| Frontend | React + TypeScript + Vite |
| Backend | .NET 10 + ASP.NET Core + **.NET Aspire** |
| Persistence | PostgreSQL (+ Marten for event-sourced contexts) |
| Object store | MinIO |
| Messaging | RabbitMQ |
| Identity | Keycloak (OIDC) per fab |
| Streaming | WebRTC SFU; passthrough + GPU transcode fallback |
| Time sync | PTP (IEEE 1588) per fab |
| Observability | OpenTelemetry → Aspire dashboard + Grafana stack |
| Orchestration | Aspire AppHost (dev) → k3s + Helm (prod) |

## Quickstart — register your first camera

Prerequisites: .NET 10 SDK, Docker Desktop running, Node 20+, pnpm.

```pwsh
# 1. Restore + build
dotnet restore
pnpm install

# 2. Start the full stack (Postgres, RabbitMQ, Keycloak, the
#    camera-catalog API, and both React apps). The Aspire dashboard
#    URL is printed on startup.
dotnet run --project src/AppHost
```

In the Aspire dashboard, wait for `migrations` to reach **Finished**
and `camera-catalog` + `keycloak` + the React apps to reach
**Running**. Then:

1. Open the **management-web** URL from the dashboard (default
   `http://localhost:5173`).
2. Sign in with the seeded admin (Keycloak realm
   `smart-sentinel-eye`): **`admin` / `Admin1234`**.
3. Click **Register camera**, give it a unique name, paste an
   `rtsp://...` URL, submit.
4. The new row appears in the list. Open the **rabbitmq** management
   UI from the dashboard and inspect the
   `camera-catalog.SmartSentinelEye.Shared.Contracts.CameraCatalog.CameraRegisteredV1`
   queue — the integration event is sitting there waiting for the
   first subscriber.

## Quickstart — watch a camera

Once a camera is registered (above), the **Stream** column in the
cameras list polls `/streams` every five seconds and renders a
coloured pill: **Healthy** (green), **Degraded** (yellow),
**Offline** (red), **Provisioning** (grey). Hover the pill for the
last successful frame time and any error message.

1. Wait for the **Stream** column to settle. With a reachable RTSP
   source the badge moves Provisioning → Healthy within ~5 s; with
   an unreachable source it transitions to Degraded.
2. Click the **Watch** button on the row to open the viewer panel.
   A WebRTC peer connection is negotiated through MediaMTX via WHEP
   and a live frame appears within ~3 s (warm cache).
3. Disconnect the camera (pull the cable / stop the RTSP source).
   Within ~10 s the badge flips to **Degraded** and the viewer panel
   shows a "Reconnecting…" overlay. Restore the source and watch the
   badge return to **Healthy**.

## Quickstart — publish a layout and view it on a kiosk

With at least one camera registered, the **Layouts** page in
management-web lets an admin author a layout that pins the camera to a
viewable surface. The **kiosk-web** app then opens that layout on a
shop-floor display, with sub-second push notifications when the admin
archives or replaces the layout.

1. In management-web, click **Layouts** in the top nav, then
   **New layout**. Pick a name and select the registered camera from
   the dropdown. Click **Save as draft**. The new row shows state
   **Draft**.
2. Click **Publish** on the row. The state flips to **Published**
   within ≤ 1 s. ``LayoutRevisionPublishedV1`` lands on the
   integration bus; the SignalR hub broadcasts to any connected kiosk.
3. Open the **kiosk-web** URL from the Aspire dashboard (default
   `http://localhost:5174`). Sign in with the same admin credentials.
   The picker lists the newly Published layout.
4. Tap the layout card. The single-cell view opens and a live
   WebRTC frame from the registered camera appears within ≤ 3 s.
5. In management-web, click **Archive** on the layout row. Within
   ≤ 1 s the kiosk force-disconnects to the picker — the archived
   layout is gone from the list (US-3).
6. *(Optional)* Click **Edit (new draft)** instead of Archive to
   branch a new Draft revision off the Published one (US-4). Edit
   the camera in the dialog, then **Publish** the new revision —
   revision 1 is auto-Archived in the same transaction and any
   connected kiosk force-disconnects.

## Quickstart — compose an overlay on the layout

Spec 004 layers a WYSIWYG text overlay onto a layout's camera view.
Admins author a normalized-coordinate label in the management UI;
kiosks render it over the live frame and pick up republishes via the
same SignalR hub.

1. In management-web, click **Overlays** in the top nav, then
   **New overlay**. Type a name and label text, drag the label into
   position on the preview canvas, and adjust the font-size slider.
   Click **Save as draft**, then **Publish** on the new row — the
   state flips to **Published** within ≤ 1 s and
   ``OverlayRevisionPublishedV1`` lands on the integration bus.
2. Open the **Layouts** page (or create a new layout). Pick the
   newly Published overlay from the **Overlay** dropdown in the
   **New layout** dialog and save. Publish the layout.
3. On the kiosk, the live frame now shows the overlay label at the
   authored position. The label scales with the viewport because the
   coordinates are normalized to 0..1.
4. Edit the overlay (in management-web, **Edit (new draft)** on the
   overlay row), tweak the text or position, and **Publish** the new
   revision. Every connected kiosk that has the overlay bound updates
   its label within ≤ 1 s without a page reload.
5. **Archive** the overlay to clear the binding everywhere — the
   kiosk shows a transient "overlay unavailable" banner and the live
   camera frame keeps streaming on its own.

## Quickstart — bind a variable to an overlay

Spec 005 lights up the `{{placeholder}}` tokens that overlay labels
have always supported. Admins define typed system variables; the
server resolves placeholders at fetch time and pushes the resolved
text to every connected kiosk via the existing SignalR hub —
typically within 200 ms of the value changing.

1. In management-web, click **System variables** in the top nav,
   then **New variable**. Pick a name (e.g. `oeeLine1`), Type
   `Number`, leave the value empty. Click **Define**.
2. Edit (or create) an overlay whose label text references the
   variable — e.g. `OEE: {{oeeLine1}}%` — and publish it. Bind
   the overlay to a layout as usual.
3. On the kiosk, the live frame shows `OEE: {{oeeLine1}}%`
   literally for now (the variable is still `Unset`).
4. In management-web, **System variables** → type `82.4` into the
   row's New value field → click **Set value**. The kiosk's label
   updates to `OEE: 82.4%` within ≤ 200 ms.
5. **Archive** the variable to release the name. Any overlay still
   referencing it reverts to the literal `{{oeeLine1}}` placeholder.
   Re-create the variable later and updates flow again.

## Quickstart — publish an event end-to-end

Spec 006 adds **EventIngestion** — the upstream half of the
camera → event → overlay loop. Four source types (PLC + camera
inference via MQTT, manual operator annotations + external
webhooks via HTTP) all funnel into a single canonical `events`
table and fan out as `FabEventIngestedV1` on RabbitMQ within
≤ 50 ms of arrival.

1. `aspire run` brings up the new `mosquitto` container alongside
   Postgres, RabbitMQ, and Keycloak. Wait for everything to go
   green on the dashboard.
2. **PLC event via MQTT.** v1 still uses the seeded `station-4`
   password file (set the PBKDF2 hash with
   `docker run --rm -v $PWD:/m eclipse-mosquitto:2.0 mosquitto_passwd -b /m/passwords.txt station-4 <pw>`,
   restart the broker). Spec 008 adds the JWT path: once the
   mosquitto-go-auth image (see `src/AppHost/mosquitto/Dockerfile`)
   is in play, the device authenticates with a Keycloak-minted
   service-account token instead — register the device via the
   Identity API first:
   ```bash
   curl -X POST 'http://localhost:5046/devices/register?fabId=munich' \
     -H 'Authorization: Bearer <admin-token>' \
     -H 'Content-Type: application/json' \
     -d '{"deviceType":"plc","deviceIdentifier":"station-4"}'
   ```
   Then publish:
   ```bash
   mosquitto_pub -h localhost -p 1883 -u station-4 -P <pw> \
     -t fab/munich/plc/station-4 \
     -m '{"eventId":"<guid-v7>","kind":"PlcCycleStart","occurredAt":"2026-05-28T08:14:33Z","payload":{"cycleId":"abc"}}'
   ```
3. **Manual annotation via HTTP.** Sign in to the management app
   as admin, then:
   ```bash
   curl -X POST 'http://localhost:5044/events/manual?fabId=munich' \
     -H 'Authorization: Bearer <admin-token>' \
     -H 'Content-Type: application/json' \
     -d '{"deviceId":"kiosk-3","kind":"Annotation","occurredAt":"2026-05-28T08:14:33Z","payload":{"note":"scratch on panel"}}'
   ```
   Expect a 202 with the server-minted `eventId` in the body.
4. **Webhook integration.** Register a webhook first:
   ```bash
   curl -X POST http://localhost:5044/webhook-integrations \
     -H 'Authorization: Bearer <admin-token>' \
     -H 'Content-Type: application/json' \
     -d '{"name":"qa","defaultKind":"QaResult"}'
   ```
   The response carries the plaintext token once. POST to
   `/events/webhook/qa` with `Authorization: Bearer <token>` to
   ingest.
5. **Verify.** `GET /events?fabId=munich` (admin) lists every
   ingested event with cursor pagination. Any malformed MQTT
   delivery lands in `GET /events/dead-letters` instead.

## Quickstart — author and publish an automation rule

Spec 007 adds **Automation** — the rule engine that closes the
camera → event → overlay loop. Declarative rules in Postgres
match `FabEventIngestedV1` events; each match fires
`SystemVariableValueRequestedV1` (consumed by spec 005's
SystemVariables) or `OverlayHighlightRequestedV1` (pushed on
the existing `/hubs/layouts` SignalR hub).

1. Pre-conditions: a `Number` SystemVariable named `oeeLine1`
   exists (see the spec 005 quickstart) and is referenced by a
   published overlay's label (e.g. `OEE: {{oeeLine1}}%`) bound
   to a kiosk's layout. Spec 006's `station-4` MQTT device is
   provisioned in Mosquitto.
2. **Create a rule** (admin). `curl -X POST http://localhost:5045/rules`
   (Automation's port — check the Aspire dashboard) with body:
   ```json
   {
     "name": "high-oee-on-fast-cycle",
     "triggerSource": "plc",
     "triggerKind": "PlcCycleStart",
     "predicate": "$.payload.cycleTime <= 30",
     "actionType": "SetVariableValue",
     "variableName": "oeeLine1",
     "valueExpression": "100 - $.payload.cycleTime * 2"
   }
   ```
   Response: 201 with the new `RuleIdentifier`. The rule starts
   in `Draft` and is NOT yet evaluated.
3. **Publish** the rule: `POST /rules/high-oee-on-fast-cycle/publish`.
   The rule flips to `Active` and lands in the live rule cache.
4. **Trigger** a matching MQTT event:
   ```bash
   mosquitto_pub -h localhost -p 1883 -u station-4 -P <pw> \
     -t fab/munich/plc/station-4 \
     -m '{"eventId":"<guid-v7>","kind":"PlcCycleStart","occurredAt":"2026-05-28T08:14:33Z","payload":{"cycleTime":27}}'
   ```
   The kiosk's overlay should flip to `OEE: 46%` (= 100 − 27 ×
   2) within ≤ 200 ms of MQTT ACK (50 ms ingest + 100 ms rule
   eval + 50 ms broadcast).
5. **Archive** the rule when it has served its purpose:
   `POST /rules/high-oee-on-fast-cycle/archive`. The rule is
   evicted from the cache; subsequent matching events no longer
   fire it. The name is released for re-use on the next
   `POST /rules` with the same name.

## Quickstart — bind a kiosk, register a device, author a scoped rule

Spec 008 lights up Keycloak as the identity provider for every
caller — humans (admin / operator), kiosks, devices (PLC + camera
inference). The dev realm seeds two test users in `/fabs/munich`
plus the `identity-admin` service-account client that the Identity
API uses to mint kiosk + device clients on demand.

1. **Pre-conditions.** `dotnet run --project src/AppHost`; wait for
   `keycloak` and `migrations` to reach **Running** /
   **Finished**.
2. **Sign in as the fab admin.** Browse the management app at
   `http://localhost:5173`, click **Sign in**, and authenticate as
   `admin@munich.test` / `Admin1234`. The JWT carries
   `groups: ["/fabs/munich"]` + every `sse.*.write` scope.
3. **Enroll a kiosk** (admin, `sse.identity.kiosks.write`):
   ```bash
   curl -X POST 'http://localhost:5046/kiosks/enroll?fabId=munich' \
     -H 'Authorization: Bearer <admin-token>' \
     -H 'Content-Type: application/json' \
     -d '{"clientId":"kiosk-pilot"}'
   ```
   Response: `201` with `clientId`, `fab`, and the one-time
   `clientSecret`. The kiosk uses the secret in
   `client_credentials` to mint its own JWT.
4. **Register a PLC device** (admin,
   `sse.identity.devices.write`):
   ```bash
   curl -X POST 'http://localhost:5046/devices/register?fabId=munich' \
     -H 'Authorization: Bearer <admin-token>' \
     -H 'Content-Type: application/json' \
     -d '{"deviceType":"plc","deviceIdentifier":"station-4"}'
   ```
   Response carries `clientId: plc-station-4` and the device's
   one-time secret. The device authenticates over MQTT with a
   Keycloak-minted JWT (see spec 006's quickstart for the JWT-mode
   broker bring-up).
5. **Rotate a webhook integration** off the legacy hash-compare
   bearer onto JWT validation (hard-cut migration, FR-016):
   ```bash
   curl -X POST 'http://localhost:5046/webhook-integrations/qa/rotate' \
     -H 'Authorization: Bearer <admin-token>' \
     -H 'Content-Type: application/json' \
     -d '{"fabId":"munich"}'
   ```
   EventIngestion's `WebhookIntegrationRotatedV1Handler` flips
   the integration's `ValidationMode` on the next event delivery;
   future POSTs to `/events/webhook/qa` must carry a Keycloak JWT
   with `scope: sse.events.write` + `groups: /fabs/munich`.
6. **Author a fab-scoped rule** (admin, `sse.rules.write`). Spec
   007's quickstart still works verbatim — but every `POST` /
   `PATCH` now verifies the JWT scope catalogue from
   `ServiceDefaults.Authorization.Scope` instead of the
   grandfathered `sse.management` bundle.
7. **Verify fab guarding.** Try to call any of the above with
   `?fabId=berlin`. The shared `IFabAuthorizationGuard` returns
   `403 RESOURCE_FAB_NOT_AUTHORIZED` because the admin's `groups`
   claim does not include `/fabs/berlin`.

### AEL — the predicate + value expression language

The AEL (Automation Expression Language; ADR-0099) is a tight
hand-rolled subset:

- **Field access**: `$.source`, `$.kind`, `$.device`, `$.payload.*`.
- **Comparison**: `==`, `!=`, `<`, `<=`, `>`, `>=`, `contains`.
- **Arithmetic**: `+`, `-`, `*`, `/`, `%`, unary `-`. Mixed int+decimal promotes to decimal.
- **Boolean**: `&&`, `||`, `!`; both `&&` and `||` short-circuit.
- **Literals**: int (`42`), decimal (`3.14`), string (`"abc"` or `'abc'`), `true` / `false`.
- **Grouping**: `(…)`.

Examples:

```aelte
$.payload.cycleTime <= 30                                       # predicate
$.kind == "PlcCycleStart" && $.payload.confidence > 0.8         # predicate
100 - $.payload.cycleTime * 2                                   # value expression
$.payload.note contains "defect"                                # predicate
```

## Tests

```pwsh
# Unit + architecture tests (fast, no Docker)
dotnet test --filter "FullyQualifiedName!~SmartSentinelEye.Integration.Tests"

# Integration tests (slow, requires Docker)
dotnet test tests/Integration.Tests/

# Coverage gate per ADR-0065 (also runs all unit tests)
pwsh scripts/coverage-check.ps1
```

Latency budget enforcement (constitution §IV) ships with the
integration suite: `CommandLatencyTests.POST_cameras_p95_stays_within_the_command_path_budget`
asserts `POST /cameras` p95 ≤ 200 ms across 100 sequential calls.

## Documents

- **[.specify/memory/constitution.md](.specify/memory/constitution.md)** —
  principles, locked stack, NFRs, governance.
- **[docs/adr/](docs/adr/)** — every architectural decision with
  reasoning. Start with `0000-initial-decisions.md`.
- **[CLAUDE.md](CLAUDE.md)** — orientation for Claude Code sessions.
- **[specs/](specs/)** — per-feature specs (Spec-Kit), populated as
  features are designed.

## Workflow

This project follows
[Spec-Kit](https://github.com/github/spec-kit) for spec-driven
development:

```
/speckit-constitution   amend principles (rare, requires ADR)
/speckit-specify        write a feature spec
/speckit-clarify        de-risk ambiguities (optional)
/speckit-plan           propose technical approach
/speckit-tasks          break plan into ordered atomic tasks
/speckit-implement      execute tasks
```

Every PR traces back to at least one task ID. Every spec references at
least one ADR. The latency SLO (≤ 800 ms event-to-overlay) is enforced
on every PR that touches the relevant path.

## License

TBD.
