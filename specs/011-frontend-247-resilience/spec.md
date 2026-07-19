# Feature Specification: Frontend 24/7 Resilience

**Feature Branch**: `011-frontend-247-resilience`

**Created**: 2026-07-19

**Status**: Draft

**Input**: User description: "Frontend 24/7 resilience: the kiosk and management web apps must survive and self-heal from real-world failure conditions during unattended 24/7 operation. Scope: (1) WebRTC/WHEP stream recovery; (2) realtime hub resilience; (3) auth session survival; (4) cache reconciliation after outages; (5) crash containment. Driven by findings from a frontend health investigation on develop; the load-bearing NFR is 24/7 unattended operation of industrial CCTV walls."

## Context

A frontend health investigation (2026-07-19, develop) found that both web
apps work only while every dependency is healthy. Today, all of the
following failures are **silent and permanent** until a human reloads or
re-authenticates the display:

- A dropped video session freezes the last frame while the tile still
  reads "Live".
- The live-update channel stops retrying after roughly 40 seconds of
  outage, and never connects at all if the display boots before the
  backend is ready.
- When the sign-in session lapses (by default after ~10 hours), the
  kiosk replaces the entire video wall with a manual sign-in button.
- State changes that occur during an outage (variable values, overlay
  publishes/archives) are never reconciled after reconnecting.
- A single unexpected rendering error blanks the whole application with
  no recovery path.

For an unattended industrial CCTV wall, silent-and-stale is the worst
failure class: operators trust what the wall shows. This feature makes
every failure either self-heal or become visibly degraded — never
silently wrong.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Video streams recover on their own and never lie (Priority: P1)

A kiosk wall is showing a grid of live camera streams. The streaming
backend restarts, or the network blips for a minute. Each affected tile
visibly switches from "Live" to a reconnecting state, then returns to
live video automatically once the stream is reachable again — with no
human involvement. At no point does a frozen frame masquerade as live
video.

**Why this priority**: A stale frame presented as live is the single
most dangerous failure for a safety-adjacent CCTV wall — an operator
acting on a frozen image believes they are seeing the present. This is
also the failure with the widest blast radius (every tile, both apps).

**Independent Test**: With a wall displaying streams, restart the
streaming backend (or cut the network for 60 seconds). Verify each tile
leaves the "Live" state within seconds, shows a reconnecting indicator,
and returns to moving video without any user action. Verify a tile whose
very first connection attempt fails also keeps retrying and eventually
shows video.

**Acceptance Scenarios**:

1. **Given** a tile showing live video, **When** the underlying stream
   session dies (backend restart, network drop), **Then** the tile stops
   claiming "Live" within 10 seconds and shows a visible reconnecting
   state.
2. **Given** a tile in the reconnecting state, **When** the stream
   becomes reachable again, **Then** live video resumes automatically
   within 30 seconds of availability, without a page reload.
3. **Given** a kiosk that boots while the streaming backend is down,
   **When** the backend comes up later, **Then** every tile connects on
   its own (initial connection failures are retried indefinitely with
   backoff).
4. **Given** a tile whose stream is reported unhealthy by monitoring,
   **When** the health recovers, **Then** the tile re-establishes a real
   session rather than merely relabeling the old one.
5. **Given** an operator navigates away from a layout (or the layout is
   switched), **When** tiles are torn down, **Then** their streaming
   sessions are explicitly released on the server side rather than left
   to time out.

---

### User Story 2 - Live updates survive outages and catch up afterwards (Priority: P2)

A kiosk relies on pushed updates for layout revocation, tile
highlighting, and live variable text on overlays. The backend is
restarted for maintenance, or the network is unavailable for an hour.
The wall shows a discreet "live updates degraded" indicator while
disconnected, keeps retrying forever, and — once reconnected — brings
itself fully up to date: values that changed during the outage are
shown, layouts revoked during the outage stop playing, and overlays
archived during the outage are flagged.

**Why this priority**: Highlights and revocations are alarm-adjacent
signals. Today they silently stop arriving after ~40 seconds of outage
and missed changes are never reconciled, so a wall can display a revoked
layout or a stale machine state indefinitely.

**Independent Test**: Disconnect a kiosk from the network for 2 minutes
while changing a variable value and archiving an overlay on the
management side. Verify the kiosk shows the degraded indicator while
offline, reconnects on its own, and within seconds of reconnection shows
the new variable value and the "overlay unavailable" flag.

**Acceptance Scenarios**:

1. **Given** a connected kiosk, **When** the live-update connection
   drops, **Then** a non-intrusive degraded indicator appears and
   reconnection is attempted indefinitely with backoff (never gives up).
2. **Given** a kiosk that boots while the backend is unavailable,
   **When** the backend becomes available, **Then** the live-update
   connection establishes without a reload (initial connection failures
   retry indefinitely).
3. **Given** a variable value changed while a kiosk was disconnected,
   **When** the kiosk reconnects, **Then** the overlay shows the current
   value within 10 seconds of reconnection.
4. **Given** an overlay was published or archived while a kiosk was
   disconnected, **When** the kiosk reconnects, **Then** the wall
   reflects the current overlay state (new geometry/text, or an
   "overlay unavailable" notice) without a reload.
5. **Given** an overlay was archived before a kiosk ever loaded the
   layout, **When** the kiosk displays a tile bound to it, **Then** the
   tile shows the "overlay unavailable" notice rather than silently
   rendering nothing.
6. **Given** a production deployment (no development proxy), **When** a
   kiosk starts, **Then** the live-update connection reaches its
   endpoint via deploy-time configuration, exactly like the API gateway
   address does today.

---

### User Story 3 - Sign-in sessions renew invisibly; expiry is never silent (Priority: P2)

A kiosk runs unattended for days. Token renewal happens silently in the
background for as long as the identity provider allows. When the session
finally cannot be renewed, the kiosk automatically attempts a fresh
sign-in; if that succeeds without interaction (per identity-provider
policy for kiosk devices), the wall returns to the exact layout it was
showing. Only when interactive credentials are genuinely required does
the display show a clear full-screen "session expired" state — never a
half-dead app whose requests silently fail. Management users get the
same treatment: an expired session leads to an explicit re-sign-in
prompt, not endless generic errors.

**Why this priority**: Today session lapse takes the wall dark (or
silently 401s every request) after roughly 10 hours — incompatible with
the core 24/7 requirement — but the fix depends on identity-provider
session policy, which is partially outside frontend control.

**Independent Test**: Shorten the identity provider's session lifetimes
in a test realm. Verify the kiosk survives token expiry without visual
interruption while renewal succeeds; verify that when renewal is made to
fail, the kiosk lands either back on its layout (after automatic
re-sign-in) or on an explicit session-expired screen — and that API
calls never silently fail with stale credentials in either app.

**Acceptance Scenarios**:

1. **Given** an authenticated kiosk with a valid provider session,
   **When** the access token expires, **Then** it is renewed silently
   and video, live updates, and API calls continue uninterrupted.
2. **Given** silent renewal fails, **When** the kiosk attempts an
   automatic re-sign-in that the provider completes without user
   interaction, **Then** the wall returns to the same layout it was
   displaying (deep-link restoration).
3. **Given** re-sign-in requires user interaction, **When** the session
   lapses, **Then** the kiosk shows an explicit full-screen
   session-expired state (not a sign-in button under a torn-down wall,
   not a frozen app).
4. **Given** a management user whose session expired, **When** any
   request is rejected as unauthenticated, **Then** the app attempts
   silent renewal once and otherwise presents an explicit re-sign-in
   prompt instead of generic request-failure banners.
5. **Given** any authenticated background connection (video, live
   updates), **When** the token is renewed, **Then** subsequent
   connection attempts use the fresh token (no connection keeps retrying
   with a dead one).

---

### User Story 4 - One error never takes down the whole display (Priority: P3)

An unexpected rendering error occurs in one part of the application
(for example, a malformed push message triggers a bug). Instead of a
permanent blank screen, the management app shows a contained error state
with a retry action, and the kiosk automatically recovers by reloading
itself after a short delay — returning to the layout it was showing.

**Why this priority**: Lower likelihood than the failures above, but
today's outcome (permanent white screen on an unattended display until
a human visits the fab floor) is disproportionate to the trigger.

**Independent Test**: Inject a deliberate rendering error in a test
build. Verify the management app shows a bounded error panel with a
working retry, and the kiosk shows a brief recovery notice and reloads
itself back to the same layout without human action.

**Acceptance Scenarios**:

1. **Given** the management app, **When** an uncaught rendering error
   occurs, **Then** the user sees a contained error state with a retry
   action instead of a blank page.
2. **Given** a kiosk wall, **When** an uncaught rendering error occurs,
   **Then** the kiosk automatically reloads within 30 seconds and
   returns to the layout it was displaying.
3. **Given** a kiosk that keeps crashing immediately after reload,
   **When** recovery loops, **Then** reload attempts are spaced with
   backoff so the device does not hot-loop.

---

### Edge Cases

- Network flaps rapidly (connect/disconnect every few seconds): retry
  backoff must prevent request storms against gateway, streaming, and
  identity services; indicators must not strobe.
- Backend is up but the streaming service is down (or vice versa): each
  subsystem degrades and recovers independently; one subsystem's outage
  must not mask another's recovery.
- Token renewal succeeds while the live-update or stream connection is
  mid-retry: the next attempt must pick up the fresh token.
- A layout is revoked during an outage: on reconnect the kiosk must
  leave the revoked layout (reconciliation covers revocation, not just
  values and overlays).
- Outage longer than the identity session: reconciliation and re-auth
  interact — the kiosk must end up authenticated *and* current, in
  either order of recovery.
- Multiple tiles fail simultaneously (backend restart): per-tile retries
  must be independently jittered so 16+ tiles do not synchronize their
  reconnect attempts.
- The kiosk device's clock is stepped by time synchronization: timed UI
  behaviours (highlight expiry, retry timers) must not fire early, late,
  or forever.
- Session release on tile teardown fails (server already gone): teardown
  must complete locally regardless.

## Requirements *(mandatory)*

### Functional Requirements

**Stream recovery**

- **FR-001**: The system MUST detect that a video stream session is no
  longer delivering media (connection loss, transport failure) and
  reflect this in the tile's visible status within 10 seconds.
- **FR-002**: A tile MUST NOT display a "Live" status unless its
  underlying stream session is currently established and delivering
  media.
- **FR-003**: The system MUST automatically re-establish failed stream
  sessions — including first-attempt failures — retrying indefinitely
  with backoff and jitter.
- **FR-004**: The system MUST explicitly release a stream session with
  the streaming service when a tile is torn down (navigation, layout
  switch, page close), and local teardown MUST succeed even if the
  release request fails.
- **FR-005**: A stream-health recovery signal MUST trigger verification
  or re-establishment of the actual session, not only a status-label
  change.

**Live-update resilience**

- **FR-006**: The live-update connection MUST retry indefinitely with
  backoff — both for reconnection after loss and for initial connection
  at startup. It MUST never enter a permanent give-up state.
- **FR-007**: While the live-update connection is down, each app MUST
  display a non-intrusive "live updates degraded" indicator; the
  indicator MUST clear on reconnection.
- **FR-008**: On reconnection, the system MUST reconcile all state that
  can change via push while disconnected — layout lifecycle (including
  revocation), overlay definitions and availability, and variable
  values — so the wall reflects current state without a reload.
- **FR-009**: Overlay unavailability MUST be derived from the overlay's
  actual current state, not only from push notifications observed while
  connected (an overlay archived before the kiosk loaded must be flagged
  identically).
- **FR-010**: The live-update endpoint address MUST be supplied through
  deploy-time configuration in production, with the development setup
  continuing to work unchanged; a production build with the
  configuration missing MUST fail loudly at startup rather than
  malfunction at runtime.

**Session survival**

- **FR-011**: Access-token renewal MUST happen silently in the
  background for as long as the identity provider session permits, with
  no visible interruption to video, live updates, or API calls.
- **FR-012**: When a request is rejected as unauthenticated, the system
  MUST attempt one silent renewal and retry before treating the session
  as expired; repeated failures MUST escalate to the explicit
  session-expired flow, never to silent request failures.
- **FR-013**: On session expiry, the kiosk MUST automatically attempt a
  fresh sign-in; if the provider completes it without interaction, the
  kiosk MUST return to the layout it was displaying (deep-link
  restoration through the sign-in round-trip, in both apps).
- **FR-014**: When interactive sign-in is unavoidable, the kiosk MUST
  present a dedicated full-screen session-expired state and the
  management app an explicit re-sign-in prompt.
- **FR-015**: All authenticated long-lived connections (video streams,
  live updates) MUST use the current token on every (re)connection
  attempt.

**Crash containment**

- **FR-016**: An uncaught rendering error MUST be contained: the
  management app presents a bounded error state with a retry action; the
  kiosk automatically reloads to its current layout, with backoff if
  crashes repeat.
- **FR-017**: Every resilience state transition (stream lost/recovered,
  live-updates degraded/restored, session renewed/expired, crash
  recovery) MUST be observable in application logs/telemetry so
  operations can distinguish self-healed incidents from ongoing ones.

### Key Entities

- **Stream session**: The relationship between a tile and its camera
  feed; has an observable delivery state (connecting, live,
  reconnecting, failed) that the UI must truthfully mirror; explicitly
  released on teardown.
- **Live-update connection**: The single per-app push channel; has
  connected/degraded states surfaced to the user and an associated
  reconciliation action on every reconnect.
- **Sign-in session**: The user/device authentication context; renewable
  silently, expiring visibly; carries the display's current location so
  it can be restored after any sign-in round-trip.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a 60-second full network outage, a 16-tile kiosk
  wall returns to 100% live tiles and a cleared degraded indicator
  within 60 seconds of network restoration, with zero human
  interventions.
- **SC-002**: A frozen or dead stream is never labeled "Live" for more
  than 10 seconds, measured across induced backend restarts and network
  drops.
- **SC-003**: A kiosk survives 72 hours of unattended operation —
  including at least one backend restart, one streaming-service restart,
  and one identity-session expiry — without a page going permanently
  dark, stale, or requiring on-site interaction (given a provider
  session policy that permits non-interactive renewal).
- **SC-004**: A state change made while a kiosk is disconnected (value
  change, overlay archive, layout revocation) is reflected on the wall
  within 10 seconds of reconnection in 100% of test runs.
- **SC-005**: During a 2-minute outage with 16 tiles, reconnection
  attempts per subsystem stay under 30 per minute per kiosk (backoff and
  jitter are effective — no request storms).
- **SC-006**: An injected rendering crash on a kiosk recovers to the
  same layout without human action within 30 seconds.

## Assumptions

- The identity provider's session policy for kiosk devices can be
  configured to permit long-lived sessions and/or non-interactive
  re-sign-in; fully unattended credential provisioning (device
  accounts, offline tokens) remains a separate, deferred feature. Where
  provider policy forces interaction, the explicit session-expired
  screen is the accepted outcome.
- The existing deploy-time configuration mechanism used for the API
  gateway and identity addresses is the appropriate vehicle for the
  live-update endpoint address; no new configuration system is
  introduced.
- Reconciliation on reconnect may be implemented as a full refresh of
  the affected read models (correctness over efficiency); the volume of
  state per kiosk is small enough that this is acceptable within the
  10-second reconciliation target.
- The "live updates degraded" indicator is a small persistent badge and
  intentionally understated; alarm-grade signaling of degraded walls to
  a central operations view is out of scope for this feature.
- The event-to-overlay latency budget is unaffected: this feature
  changes failure and recovery behaviour, not the steady-state push and
  render path.
- Findings from the same investigation that are not resilience-related
  (swallowed mutation errors on management list pages, unreachable
  camera viewer, overlay draft editing dead end, camera picker cap,
  accessibility issues) are explicitly out of scope and will be
  addressed in separate features.
