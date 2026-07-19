# Research: Frontend 24/7 Resilience

All Technical Context entries were known up front (locked stack, no new
dependencies); research below resolves the *mechanism* choices. No
`NEEDS CLARIFICATION` items remain.

## R1 — Detecting dead WebRTC sessions

**Decision**: Subscribe to `RTCPeerConnection.onconnectionstatechange`
and treat `failed` as terminal (immediate retry) and `disconnected` as
suspect (retry if not back to `connected` within a 5 s grace window).
"Live" is derived from `connectionState === 'connected'`, not from the
WHEP POST succeeding.

**Rationale**: `connectionstatechange` aggregates ICE + DTLS transport
state and is the standards-track signal Chromium fires within seconds of
media transport loss; it requires no extra traffic. `disconnected` can
self-heal (ICE consent retries), hence the grace window — retrying
instantly would churn sessions on micro-blips.

**Alternatives considered**: (a) polling `getStats()` for
`framesDecoded` deltas — strongest "frozen frame" oracle but adds a
per-tile poll loop and GC pressure on 16+ tiles; deferred unless field
data shows silent stalls with `connected` state. (b) `video` element
events (`stalled`/`waiting`) — fire unreliably for WebRTC sinks in
Chromium; rejected.

## R2 — WHEP session release

**Decision**: Capture the `Location` header from the WHEP POST response
and issue `DELETE` with the current bearer and `keepalive: true` in
`close()`; ignore the result, always tear down locally.

**Rationale**: Per the WHEP spec (draft-ietf-wish-whep), the session
resource URL returned in `Location` is the sanctioned teardown handle;
MediaMTX implements it. `keepalive` lets the DELETE survive page
navigation/unload. Local teardown must not depend on it (server may be
the thing that died — spec edge case).

**Alternatives considered**: relying on ICE timeout server-side (status
quo) — leaks sessions for tens of seconds per layout switch and
accumulates under cycling; rejected by FR-004.

## R3 — Offer timing (ICE gathering)

**Decision**: Wait for `icegatheringstatechange === 'complete'` with a
250 ms cap before POSTing the offer.

**Rationale**: On the single-L2 fab network host candidates gather in
single-digit ms, so the cap is effectively never hit in production while
materially improving offers when the browser is slower (VM kiosks). A
hard cap keeps connect latency bounded (§IV: SFU → kiosk decode leg is
about steady state, but slow connects erode SC-001's 60 s wall target).

**Alternatives considered**: trickle ICE via PATCH — MediaMTX supports
it but it adds a second request path and error mode for zero benefit on
L2; rejected as speculative generality.

## R4 — SignalR unbounded reconnect

**Decision**: Custom `IRetryPolicy` `{ nextRetryDelayInMilliseconds: ctx =>
delays[min(ctx.previousRetryCount, 4)] + jitter }` with delays
0/2/5/10/30 s and full jitter of ±20%, never returning `null`; plus
`connection.onclose` scheduling `start()` again (covers server-initiated
closes that bypass the reconnect machinery); plus an initial-`start()`
retry loop with the same ladder in the hook.

**Rationale**: `withAutomaticReconnect()`'s default 4-attempt policy is
the direct cause of the ~40 s give-up. A policy that never returns
`null` keeps the built-in machinery (and `onreconnected` reconciliation)
while making it unbounded. `onclose` + start-retry cover the two paths
automatic reconnect does not: never-connected and explicitly-closed.
Jitter prevents 20 kiosks synchronizing reconnects after a backend
restart (SC-005).

**Alternatives considered**: hand-rolled connect loop without
`withAutomaticReconnect` — loses the library's negotiated-transport
resume and `onreconnected` semantics; rejected.

## R5 — Reconnect cache reconciliation (RTK Query tags)

**Decision**: Make `getOverlaySnapshot` provide
`[{OverlaySnapshot, id}, {OverlaySnapshot, id:'ALL'}]` so the existing
`id:'ALL'` invalidations (mutations + reconnect) start working; on
reconnect additionally invalidate the bare `Overlay` type (refetches all
mounted per-overlay queries). Full refetch over missed-event replay.

**Rationale**: RTK Query matches invalidated tags with an `id` only
against identical provided tags — providing the sentinel alongside the
specific id is the established pattern already used by the `*List` tags
in this codebase; the fix is additive and keeps per-id invalidation
working. Full refetch honours the spec assumption (correctness over
efficiency; state per kiosk is small; 10 s target is comfortable).

**Alternatives considered**: bare-type invalidation
(`['OverlaySnapshot']`) — equivalent effect but diverges from the
codebase's sentinel-id convention; sequence-numbered event replay —
server work, out of scope, speculative.

## R6 — Hub endpoint configuration

**Decision**: `VITE_LAYOUT_HUB_URL ?? '/hubs/layouts'` resolved in a new
shared `hubUrl.ts`; `import.meta.env.PROD && missing → throw` at module
load. Same PROD guard retrofitted to `VITE_API_GATEWAY_URL` and
`VITE_KEYCLOAK_URL`.

**Rationale**: Mirrors the ADR-0106 gateway-origin mechanism exactly
(dev: Aspire/Vite proxy; prod: deploy layer injects). Fail-loudly at
boot beats the current silent `localhost:8080`/same-origin fallbacks
(spec FR-010). Module-load throw surfaces in the first paint and in e2e
smoke immediately.

**Alternatives considered**: runtime `config.json` fetch — adds a boot
round-trip and a new mechanism the repo doesn't have; rejected
(constitution VI prefers the existing env-injection path).

## R7 — Silent renewal and 401 posture (react-oidc-context)

**Decision**: Keep `automaticSilentRenew` (library default, refresh-token
based). Add `baseQueryWithReauth`: one `signinSilent()` retry on 401,
then escalate to a registered `onSessionExpired`. Wire
`events.addSilentRenewError` and `events.addAccessTokenExpired` to the
same escalation. Kiosk escalation = stash path → `signinRedirect({state})`
with a `sessionStorage` loop-guard; if the round-trip comes back
unauthenticated, render the full-screen session-expired state.
Management escalation = explicit re-sign-in prompt.

**Rationale**: oidc-client-ts already renews on a timer while the
Keycloak SSO session lives; the gap is *failure* handling. One silent
retry inside the base query heals the token-expired-between-renewals
race without user-visible noise (FR-012); the loop guard prevents the
classic redirect storm when Keycloak requires interaction (FR-014).
Deep-link restoration uses the OIDC `state` round-trip — the mechanism
designed for it — rather than guessing in the callback.

**Alternatives considered**: kiosk `client_credentials` device tokens —
the real long-term answer (constitution: Availability) but explicitly a
separate deferred identity feature (spec Assumption 1); interceptor-less
"just let queries fail and watch `isAuthenticated`" — leaves in-flight
requests failing silently, violating FR-012.

## R8 — Crash containment / kiosk watchdog

**Decision**: One shared class-based `ErrorBoundary` with a render-prop
fallback. Management fallback: bounded panel + reset. Kiosk fallback:
`location.reload()` after 5/15/60 s (crash-count in `sessionStorage`,
cleared after 5 min of stability), preserving the URL so the router
restores the layout.

**Rationale**: React error boundaries are the only mechanism to catch
render-phase throws; class component is required by React. Reload (vs
boundary reset) on the kiosk clears whatever corrupted client state
caused the crash — the standard unattended-display watchdog pattern.
Backoff satisfies the hot-loop edge case; `sessionStorage` survives the
reload but not a power cycle, which is the right scope.

**Alternatives considered**: service-worker or external watchdog — new
moving parts, out of scope for a frontend feature; boundary-reset-only
on kiosk — risks resurrecting the same broken state indefinitely.

## R9 — Monotonic timing

**Decision**: All retry/backoff scheduling uses relative `setTimeout`
durations; highlight expiry comparisons move from `Date.now()` epochs to
`performance.now()` deltas; timer handles are tracked and cleared on
unmount.

**Rationale**: Fab clocks are PTP-disciplined and can step; epoch
arithmetic can pin a highlight on forever or expire it instantly.
`performance.now()` is monotonic by contract. (Spec edge case; also
fixes the existing highlight-timer leak.)

**Alternatives considered**: none credible.

## R10 — Resilience observability (FR-017)

**Decision**: A shared `logResilienceEvent(subsystem, transition,
detail?)` helper emitting structured `console.info` with a stable
`[resilience]` prefix, called at every state transition (stream
lost/recovered per tile, hub degraded/restored, session
renewed/expired, crash reload).

**Rationale**: The frontends have no OTel pipeline today; a stable,
greppable console contract is verifiable in Playwright and kiosk
remote-debug sessions now, and gives a single seam to lift into browser
OTel later. Satisfies FR-017 without inventing infrastructure
(constitution VII applies to services; noted as future work).

**Alternatives considered**: browser OTel SDK — real dependency +
collector CORS surface; rejected here as speculative for this feature.
