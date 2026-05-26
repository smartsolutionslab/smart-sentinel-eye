# Feature Specification: Watch a Camera Live

**Feature Branch:** `002-watch-camera-live`

**Created:** 2026-05-26

**Status:** Draft (Phase 1 — Specify)

**Input:** Second feature of Smart Sentinel Eye — the first end-to-end
slice through the **Stream Distribution** bounded context, building on
spec 001's registered cameras. Pre-condition for every overlay /
operator / kiosk feature (Layout Composition, Overlay Designer,
Automation): without a watchable stream, none of those have a substrate
to render onto. Selected as spec 002 because the constitution's
load-bearing latency budget (≤ 800 ms event → overlay rendered) only
becomes measurable once a real video frame exists.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Watch a registered camera's live feed (Priority: P1)

A fab admin opens the management app, navigates to a registered camera,
clicks **Watch**, and sees the live RTSP feed rendered in their browser
as a WebRTC stream within a few seconds. The stream stays connected for
as long as the page is open; if the camera goes offline, the system
attempts to reconnect transparently and the UI surfaces a non-blocking
"reconnecting" banner.

**Why this priority:** Smallest possible vertical slice through the new
**Stream Distribution** context. Exercises every locked architectural
decision the streaming path requires: MediaMTX as the SFU (anchored
this spec — see Resolved Clarifications), Aspire-orchestrated container
resource, RTSP → WebRTC fan-out, integration-event-driven stream
provisioning (subscribing to `CameraRegisteredV1` from spec 001),
StreamDistribution.Api as the .NET control plane, and a frontend
WebRTC client component that will become the "cell" host for spec 003
(Layout Composition).

**Independent Test:**

1. Start the system locally via `aspire run` (which now also brings up
   the MediaMTX container).
2. Sign in to `management-web` as an admin operator.
3. Register a camera with an RTSP URL pointing at a reachable H.264
   source (a local RTSP test server or a real IP camera). Use the
   existing US1 register flow.
4. Wait briefly (≤ 5 seconds) for the new camera's row to show its
   stream as **Healthy**.
5. Click the camera row. A viewer panel opens and the live video is
   visible within ≤ 3 seconds of the click (this is "click-to-first-
   frame"; full per-leg latency budget below).
6. Disconnect the camera (pull the cable or block the RTSP port).
   Within ≤ 10 seconds the viewer shows a "reconnecting…" banner and
   the camera row's stream badge flips to **Degraded**.
7. Reconnect the camera. Within ≤ 10 seconds the banner clears and
   playback resumes; the badge returns to **Healthy**.
8. Replay the test against a real fab Postgres + RabbitMQ + MediaMTX
   stack via the Aspire fixture (ADR-0068).

**Acceptance Scenarios:**

1. **Given** a camera has been registered via spec 001 and its RTSP
   source is reachable and serving H.264,
   **When** an authenticated admin requests
   `GET /streams/{cameraIdentifier}`,
   **Then** the response is `200 OK` with
   `{ cameraIdentifier, state: "Healthy", whepUrl, lastSuccessAt }`,
   where `whepUrl` is a WHEP (WebRTC HTTP Egress Protocol) endpoint
   the browser can `POST` an SDP offer to.
2. **Given** the admin clicks the camera row in the management UI,
   **When** the browser POSTs an SDP offer to the `whepUrl`,
   **Then** the WebRTC peer connection reaches the `connected` state
   within **≤ 3 seconds** at p95 (click-to-first-frame), and the
   `<video>` element begins playing.
3. **Given** an RTSP source is reachable but serves a non-H.264 codec
   (H.265 or MJPEG),
   **When** the stream is provisioned,
   **Then** the StreamDistribution context invokes MediaMTX's FFmpeg
   path to transcode to H.264, and the stream behaves identically from
   the browser's perspective. The stream's `transcodeMode` field
   reports `"software"`.
4. **Given** an RTSP source stops responding mid-stream,
   **When** the SFU fails to read frames for **≥ 10 seconds**,
   **Then** the Stream aggregate transitions
   `Healthy → Degraded`, the integration event
   `StreamHealthChangedV1` is published, the browser viewer renders a
   "reconnecting…" banner, and the SFU retries the RTSP connect with
   exponential backoff (1 s, 2 s, 5 s, 10 s, 30 s — capped) for up to
   **5 minutes** before halting retries and emitting
   `StreamHealthChangedV1` with state `Offline`.
5. **Given** an RTSP source recovers,
   **When** the next retry attempt succeeds and three consecutive
   frames are decoded,
   **Then** the Stream transitions back to `Healthy`,
   `StreamHealthChangedV1` is published, and the browser banner
   clears without the user re-subscribing.
6. **Given** an unauthenticated request OR an authenticated user
   without the `sse.management` scope,
   **When** the user attempts `GET /streams/{cameraIdentifier}` or
   tries to open the WHEP endpoint,
   **Then** both endpoints return `401 Unauthorized` or `403 Forbidden`
   respectively, with no side effects.
7. **Given** a camera has been registered but its RTSP source has
   never been reachable,
   **When** the admin requests `GET /streams/{cameraIdentifier}`,
   **Then** the response is `200 OK` with state `Provisioning` (initial
   state). After up to 30 seconds of failed connects, the state
   transitions to `Degraded`.

---

### User Story 2 — See live stream health alongside the camera list (Priority: P1)

The cameras list in the management UI shows each camera's live stream
health (Healthy / Degraded / Offline / Provisioning) so admins can spot
camera problems at a glance without opening each viewer.

**Why this priority:** Equal priority with US-1 because without the
list-level health badge, admins have to click into every camera to know
if it's working — useless for a 250-camera fab. Together US-1 + US-2
form the smallest publishable streaming slice. Crosses the
StreamDistribution / CameraCatalog context boundary on the read side
**without** writing to CameraCatalog (per Resolved Clarification #2).

**Independent Test:**

1. Register five cameras via spec 001. Two have reachable RTSP, three
   are pointed at deliberately wrong URLs.
2. After ≤ 30 seconds, observe the management cameras list:
   - The two healthy cameras show the **Healthy** badge.
   - The three unreachable cameras show **Degraded** (within the
     first 10 s) and then **Offline** (after the 5-minute retry cap).

**Acceptance Scenarios:**

1. **Given** five registered cameras with mixed RTSP reachability,
   **When** the admin requests `GET /streams?cameraIdentifiers=...`,
   **Then** the response is `200 OK` with one entry per requested
   identifier, carrying state + `lastSuccessAt` + `transcodeMode`.
2. **Given** the management UI renders the cameras list,
   **When** the page loads,
   **Then** the UI issues `GET /cameras` (CameraCatalog) and
   `GET /streams?cameraIdentifiers=...` (StreamDistribution) in
   parallel and merges the results client-side. No proxy /
   backend-for-frontend is introduced.
3. **Given** a stream's state changes server-side,
   **When** the UI has been open for ≥ 5 seconds since the change,
   **Then** the badge reflects the new state. (v1 polls every 5 s; v2
   may upgrade to push via the replaceable real-time transport
   per ADR-0076.)

---

### User Story 3 — PTZ control from the operator (Priority: P3)

Operators steer the camera (pan / tilt / zoom) from the viewer panel
via ONVIF PTZ commands tunnelled through StreamDistribution.

**Why this priority:** Out of scope for v1 of this spec. Listed so the
v1 design doesn't paint us into a corner — the viewer component must
admit a future overlay control surface without rework. *(Deferred to a
separate spec.)*

---

### User Story 4 — Recording / replay (Priority: P3)

Streams can be recorded and replayed for incident review.

**Why this priority:** Out of scope for v1 of this spec. MediaMTX
supports recording natively; turning it on is a configuration toggle.
The data-retention policy and the replay UI are large enough to deserve
their own spec. *(Deferred.)*

---

### Edge Cases

- **Camera registered while MediaMTX is down:** The
  `CameraRegisteredV1` consumer in StreamDistribution sits in the
  Postgres outbox (ADR-0088 at-least-once delivery) and is replayed
  once MediaMTX is reachable again. The Stream aggregate goes from
  *not created* → `Provisioning` → `Healthy/Degraded` once MediaMTX
  accepts the path.
- **Camera registered while StreamDistribution.Api is down:** Same
  as above — the integration event waits in the queue. RabbitMQ
  buffers up to its configured queue limit; beyond that, messages
  fall to the dead-letter queue (operator alert via Audit &
  Observability).
- **MediaMTX restarts while a viewer is connected:** Browser's WebRTC
  peer connection enters `disconnected` state; viewer renders the
  "reconnecting…" banner. StreamDistribution re-applies the path
  config to MediaMTX on its next startup (StreamDistribution acts as
  the source of truth for paths). Browser reconnects automatically
  within ≤ 15 seconds of MediaMTX coming back.
- **Two operators watch the same camera simultaneously:** Both browsers
  subscribe to the same MediaMTX path. The SFU fans out the same H.264
  stream to multiple viewers; the RTSP source is pulled exactly once
  per camera (always-on per Resolved Clarification #3). CPU/network
  on MediaMTX scales with viewer count, not with camera count.
- **Operator browser blocks WebRTC / behind restrictive firewall:** Out
  of scope for on-prem v1; ICE candidate gathering is local-network
  only (no STUN/TURN). On-prem fab assumption: browser and MediaMTX
  share the same L2 network.
- **RTSP source serves a codec MediaMTX doesn't support even with
  software transcode (e.g. MPEG-2):** Stream stays in `Provisioning`
  for the first ~30 seconds, then transitions to `Offline` with
  diagnostic `error` field populated (e.g.,
  `unsupported_input_codec`). Admin sees this in the badge tooltip
  and the camera-row error column.
- **Slow software transcode on a low-CPU host:** MediaMTX's path
  process exits if it can't keep up; StreamDistribution treats this
  as the standard offline path, retries with backoff, eventually
  surfaces `Offline`. Transcode performance is a deployment concern
  — not gated in spec.
- **Camera URL changed after registration:** Out of scope here —
  requires spec 003's "edit camera" user story. For v1, the path
  remains pinned to the original URL even if CameraCatalog publishes a
  future `CameraUrlChangedV1`. (Followup ticket noted.)
- **CameraCatalog publishes `CameraDecommissionedV1` (US3 of spec 001,
  future):** StreamDistribution consumes it and removes the
  MediaMTX path, publishing `StreamRemovedV1`. Browsers viewing get
  `disconnected`. Defer the consumer wiring; this spec defines the
  contract only.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001:** The system MUST automatically provision a stream for
  every camera registered via spec 001. Provisioning is triggered by
  consuming `CameraRegisteredV1` in StreamDistribution (per ADR-0040;
  no cross-context project references).
- **FR-002:** Streams MUST be always-on per Resolved Clarification #3:
  StreamDistribution registers the path with MediaMTX immediately and
  MediaMTX pulls the RTSP source 24/7, regardless of viewer presence.
- **FR-003:** When the RTSP source is H.264, the SFU MUST passthrough
  the stream (re-packetize only) with **no transcoding**. When the
  source is a supported non-H.264 codec (H.265, MJPEG), the SFU MUST
  transcode to H.264 via software FFmpeg per Resolved
  Clarification #4. Unsupported codecs surface as `Offline` with a
  diagnostic error code.
- **FR-004:** A Stream aggregate in StreamDistribution MUST persist the
  state machine: `Provisioning → Healthy → Degraded → Offline`, with
  recovery back to `Healthy` from `Degraded`. State changes MUST
  publish `StreamHealthChangedV1` on the integration bus.
- **FR-005:** The system MUST expose `GET /streams/{cameraIdentifier}`
  returning the per-stream state, `whepUrl`, `transcodeMode`,
  `lastSuccessAt`, and an optional `error` field when non-Healthy.
- **FR-006:** The system MUST expose a batch read
  `GET /streams?cameraIdentifiers=...` so the management UI can fetch
  health for N cameras in one call.
- **FR-007:** The system MUST expose a WHEP-compatible WebRTC playback
  endpoint per stream (MediaMTX provides this natively at
  `/{path}/whep`). The endpoint MUST require a valid bearer token
  with `sse.management` scope; tokens MUST be passed via the
  `Authorization` header on the WHEP POST. (Token validation is
  enforced by MediaMTX's external auth hook calling back into
  StreamDistribution.Api per ADR-0007.)
- **FR-008:** On sustained RTSP read failure (≥ 10 seconds), the
  Stream MUST transition `Healthy → Degraded` and the SFU MUST retry
  the RTSP connect with exponential backoff (1 s, 2 s, 5 s, 10 s,
  30 s, capped at 30 s thereafter). If 5 minutes pass without
  recovery, the Stream MUST transition `Degraded → Offline`.
- **FR-009:** On RTSP recovery from Degraded/Offline (three
  consecutive frames decoded), the Stream MUST transition to
  `Healthy` and publish `StreamHealthChangedV1`.
- **FR-010:** Only authenticated users carrying the `sse.management`
  scope (per ADR-0023) MUST be allowed to call `/streams/*` endpoints
  or the WHEP endpoints. Unauthenticated → `401`; missing scope →
  `403`.
- **FR-011:** Stream provisioning MUST be idempotent. If
  StreamDistribution receives `CameraRegisteredV1` for a camera whose
  stream already exists (e.g., redelivery from RabbitMQ), the
  second receipt MUST be a no-op (Stream aggregate detects by
  `cameraIdentifier`).
- **FR-012:** The control-path latency budget for
  `GET /streams/{cameraIdentifier}` MUST be ≤ 100 ms p95 (read-only,
  in-memory state lookup against Postgres-backed Stream aggregate).
- **FR-013:** The viewer-path latency budget (click in UI → first
  decoded frame in `<video>`) MUST be ≤ **3 seconds p95** for the
  v1 happy path. This is NOT the constitution's 800 ms
  event-to-overlay budget — that begins ticking after the first
  frame is rendered.
- **FR-014:** The integration event `StreamHealthChangedV1` MUST
  carry `{ cameraIdentifier, fromState, toState, changedAt,
  error? }`. Versioned `V1` per ADR-0073. Lives in
  `Shared.Contracts/StreamDistribution/StreamHealthChangedV1.cs`.
- **FR-015:** StreamDistribution MUST NOT write to CameraCatalog. The
  management UI fetches both contexts' read endpoints and merges
  client-side (Resolved Clarification #5).
- **FR-016:** The management UI MUST render a generic **CameraViewer**
  component (the "cell wrapper" per Resolved Clarification #1) that
  accepts a `cameraIdentifier` prop and renders the WebRTC playback.
  This component MUST live in `apps/shared/src/ui/composites/` so
  spec 003 (Layout Composition) can embed it without modification.
- **FR-017:** The cameras list MUST display each camera's stream
  health badge alongside its existing columns. Badge tooltip MUST
  show `lastSuccessAt` and (when non-Healthy) the `error` field.
- **FR-018:** Stream state changes MUST be observable through the
  audit log (Audit & Observability context subscribes to
  `StreamHealthChangedV1`).

### Key Entities

- **Stream (aggregate root, StreamDistribution.Domain):** One per
  registered camera. Owns:
  `streamIdentifier` (Guid v7, per ADR-0090),
  `cameraIdentifier` (foreign reference, NOT cross-context project
  reference — copied value),
  `mediaMtxPath` (the path name MediaMTX uses internally),
  `state` (StreamState VO),
  `transcodeMode` (TranscodeMode VO: `Passthrough` | `Software` |
  `Unknown`),
  `lastSuccessAt` (timestamp of the most recent successful frame),
  `lastError` (Option<string>),
  `provisionedAt`, `provisionedBy`,
  `version` (concurrency token per ADR-0043).
- **StreamState (value object):** Enum-style record. Values:
  `Provisioning`, `Healthy`, `Degraded`, `Offline`. Transitions
  enforced by the aggregate (no jumping `Provisioning → Offline`
  without passing through `Degraded`, etc.).
- **TranscodeMode (value object):** `Passthrough`, `Software`,
  `Unknown`. Determined at the moment MediaMTX successfully
  negotiates the input stream.
- **MediaMtxPath (value object):** Derived from the
  `cameraIdentifier`: `cam-{cameraId}` — deterministic, URL-safe,
  unique. Allows the WHEP URL to be reconstructed by both backend
  and frontend without a separate lookup.
- **CameraRegisteredV1 consumer (StreamDistribution.Application):**
  Wolverine handler that translates the integration event into a
  domain `ProvisionStreamCommand`. Idempotent on `cameraIdentifier`.
- **StreamHealthChangedV1 (integration event in `Shared.Contracts`):**
  `{ CameraIdentifier, FromState, ToState, ChangedAt, Error? }`.
  Versioned per ADR-0073.

### External Dependencies

- **MediaMTX container resource** (anchored by Resolved Clarification
  #6). Wired via Aspire as `builder.AddContainer("mediamtx",
  "bluenviron/mediamtx", "latest-ffmpeg")`. Exposes RTSP-in
  (port 8554), WHEP (HTTP 8889), and a management API (HTTP 9997).
  StreamDistribution.Api talks to MediaMTX over HTTP for path
  config and health.
- **FFmpeg** ships inside the `latest-ffmpeg` MediaMTX image; no
  separate transcode container needed for v1.

### Cross-context contracts

- **Inbound (subscribed):** `CameraRegisteredV1` (from
  `Shared.Contracts/CameraCatalog/`). Triggers stream provisioning.
- **Outbound (published):** `StreamHealthChangedV1` (new file
  `Shared.Contracts/StreamDistribution/StreamHealthChangedV1.cs`).
  Consumed by Audit & Observability (audit log) and potentially
  Automation (future "alert on N consecutive degradations" rules).
- **No project references** between StreamDistribution and
  CameraCatalog (NetArchTest enforces).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001:** From "register a camera" (spec 001 happy path) to
  "stream is Healthy in the StreamDistribution catalog" — **≤ 30
  seconds** at p95 against a reachable H.264 RTSP source.
- **SC-002:** Click-to-first-frame in the browser viewer — **≤ 3
  seconds** at p95 (FR-013).
- **SC-003:** RTSP outage detection latency — **≤ 10 seconds** from
  last good frame to `Degraded` badge appearing in the UI.
- **SC-004:** RTSP recovery detection latency — **≤ 10 seconds**
  from RTSP coming back to `Healthy` badge appearing in the UI.
- **SC-005:** `GET /streams/{cameraIdentifier}` returns within
  **≤ 100 ms** at p95.
- **SC-006:** Integration test (Aspire fixture + MediaMTX container)
  verifies: register a camera → stream becomes Healthy → browser-side
  WebRTC peer reaches `connected` → simulated source disconnect →
  Degraded → reconnect → Healthy. Whole sequence completes in **≤ 90
  seconds** of wall-clock test time.
- **SC-007:** Architecture tests still pass — no new cross-context
  references introduced (ADR-0027) and the Domain layer of
  StreamDistribution has no infrastructure dependencies (ADR-0044).
- **SC-008:** Coverage thresholds from ADR-0065 hold for the new
  context: StreamDistribution.Domain ≥ 90 %, .Application ≥ 80 %,
  Shared.Contracts ≥ 90 %.
- **SC-009:** No new latency-budget regressions in the existing
  command path: `POST /cameras` p95 stays ≤ 200 ms (spec 001 FR-012).

## Assumptions

- **Network topology is on-prem flat L2.** Browser, MediaMTX, and
  cameras share the same network segment. No STUN/TURN needed. ICE
  candidate gathering uses host candidates only.
- **Cameras serve H.264 (with H.265/MJPEG as the realistic edge
  cases).** Resolved Clarification #4 covers transcode for those;
  anything more exotic (MPEG-2, MJPEG-over-HTTP-not-RTSP) lands
  in `Offline` until a follow-up spec.
- **One MediaMTX instance per fab is sufficient for the 250-camera
  target.** Horizontal scaling of the SFU is a v2 concern; if a fab
  exceeds single-host capacity (~250 H.264 1080p30 streams on
  reasonable hardware), spec 002 doesn't block the scale-out
  architecture (MediaMTX clustering is a configuration change).
- **Browser-side WebRTC is supported (Chrome / Edge / Firefox
  current).** Kiosk endpoints will be a known Chromium-based image
  (spec TBD), so codec / WHEP compatibility is fully under our
  control.
- **PTP frame-sync (ADR-0014) is NOT in scope for spec 002.** Single
  viewer, single-cell rendering — frame-accurate sync matters for
  the multi-cell video-wall spec only. The WebRTC stack carries
  RTP timestamps; PTP-anchored sync is a future-add on top.
- **MediaMTX's external auth hook is sufficient for v1.** When a
  browser POSTs the WHEP offer, MediaMTX calls back to
  StreamDistribution.Api (`POST /streams/{path}/authorize`) which
  validates the bearer token against Keycloak and replies 200/401.
  No custom signaling layer.

## Resolved Clarifications (Phase 2)

Six clarifications resolved during the Phase 1 Q&A round:

| # | Marker | Resolution |
|---|---|---|
| 1 | v1 scope | **Streaming + minimal cell wrapper** — a single CameraViewer composite that LayoutComposition will embed in spec 003 without changes. |
| 2 | Health-badge ownership | **StreamDistribution owns it.** No cross-context writes; management UI joins CameraCatalog + StreamDistribution read endpoints client-side. |
| 3 | Stream lifecycle | **Always-on per camera.** RTSP pull starts on provisioning, regardless of viewer presence. |
| 4 | Codec strategy | **H.264 passthrough + software (FFmpeg) fallback transcode.** GPU transcode deferred. |
| 5 | RTSP outage UX | **Auto-retry with exponential backoff (1 s → 30 s capped, 5-min total) + browser banner + Stream `Degraded` → `Offline` state transitions + integration events.** |
| 6 | SFU choice | **MediaMTX** anchored as the SFU. Single binary, RTSP-native ingest + WebRTC playback, MIT-licensed. Wired as an Aspire container resource. LiveKit and raw Pion considered and rejected (LiveKit's participant model mismatches our camera-centric one-way streaming; raw Pion is a multi-week engineering investment for no clear win). |

## Open clarifications

None — Phase 1 Q&A closed all six markers. Ready for Phase 2 (Plan).
