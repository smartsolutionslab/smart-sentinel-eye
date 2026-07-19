# Data Model: Frontend 24/7 Resilience

No backend entities change. The "data model" of this feature is three
client-side state machines and two small persisted records. Each machine
maps 1:1 to a Key Entity in spec.md.

## 1. Stream session (per tile)

Owned by `CameraViewer` (shared composite); source of truth for the
tile's visible status.

**States** (`CameraViewerStatus`, existing enum retained):
`idle → connecting → live | error | offline`, plus `reconnecting`.

**Transitions**

| From | Event | To | Notes |
|---|---|---|---|
| idle | stream health resolved, not offline | connecting | WHEP POST begins (after ≤250 ms ICE-gathering wait) |
| connecting | `pc.connectionState === 'connected'` | live | **"live" requires a connected PC** — POST success alone is no longer sufficient (FR-002) |
| connecting | connect rejected / PC `failed` | reconnecting | schedule retry: backoff base 1 s, factor 2, cap 15 s, full ±20% jitter; attempt counter resets on live |
| live | PC `failed` | reconnecting | immediate retry schedule |
| live | PC `disconnected` | reconnecting (after 5 s grace) | grace window lets ICE consent self-heal; if `connected` returns within grace, stay live |
| live/reconnecting | health `state === 'Offline'` | offline | retries suspended while source is offline |
| offline | health leaves `Offline` | connecting | fresh session |
| reconnecting | retry timer fires | connecting | new `WhepClient` instance per attempt (class is single-use by contract) |
| any | unmount / layout switch | (teardown) | abort in-flight connect; `close()` fires WHEP `DELETE` (Location) then closes PC locally regardless of DELETE outcome |

**Invariants**
- The UI label "Live" is rendered **iff** state is `live` (never from a
  stale relabel — FR-002/005; a `Degraded → Healthy` health transition
  bumps the retry nonce rather than relabeling).
- Retry loops never terminate on their own (FR-003); only `offline`
  (documented source-down) and unmount suspend them.
- Every transition calls `logResilienceEvent('stream', …)` (FR-017).

## 2. Live-update connection (per app)

Owned by `useLayoutLifecycle` (kiosk; management if/when it consumes the
hub); surfaced to pages as `{ degraded: boolean }`.

**States**: `connecting → connected ⇄ degraded`, terminal only on
unmount.

**Transitions**

| From | Event | To | Notes |
|---|---|---|---|
| connecting | `start()` resolves | connected | — |
| connecting | `start()` rejects | degraded | start-retry loop: 0/2/5/10/30 s ladder ±20% jitter, unbounded |
| connected | transport lost | degraded | SignalR auto-reconnect with the same unbounded custom `IRetryPolicy` |
| connected | `onclose` (server-initiated) | degraded | manual `start()` rescheduled — the path `withAutomaticReconnect` does not cover |
| degraded | reconnect/start succeeds | connected | fires **reconciliation** (below) |

**Reconciliation action** (on every `degraded → connected` via
`onreconnected` or successful restart) — FR-008/009:
1. invalidate `{LayoutList, 'ALL'}` (existing — covers publish/archive/revocation via the list refetch CellPage consumes)
2. invalidate `{OverlayList, 'ALL'}` (existing)
3. invalidate `{OverlaySnapshot, 'ALL'}` — **now effective**: `getOverlaySnapshot` additionally provides the `'ALL'` sentinel
4. invalidate bare `Overlay` type — refetches mounted per-overlay geometry/text queries

**Invariants**
- The connection never enters a permanent give-up state (FR-006).
- `degraded` is always visible as the non-intrusive badge; `connected`
  clears it (FR-007); badge state must not strobe (state changes are
  already ≥ 2 s apart by the retry ladder).
- Overlay unavailability is *derived state*: `unavailable(overlay) =
  pushedArchived(overlay) ∨ fetchedState has no Published revision`
  (FR-009 — covers pre-mount archives).

## 3. Sign-in session (per app)

Owned by the app shells (`AuthGate`) + `gateway.ts` singletons.

**States**: `authenticated → renewing → authenticated | expired`;
`expired → redirecting → authenticated | expired-final` (kiosk),
`expired → prompt` (management).

**Transitions**

| From | Event | To | Notes |
|---|---|---|---|
| authenticated | token nears expiry | renewing | library `automaticSilentRenew` (invisible — FR-011) |
| authenticated | any request returns 401 | renewing | `baseQueryWithReauth`: exactly one `signinSilent()` then retry of the original request (FR-012) |
| renewing | renew succeeds | authenticated | in-flight long-lived connections pick the fresh token on next attempt (FR-015 — factories already dereference latest) |
| renewing | renew fails / `silentRenewError` / `accessTokenExpired` unhandled | expired | escalation, never silent failure |
| expired (kiosk) | — | redirecting | stash `location.pathname` in OIDC `state`; set `sessionStorage` loop-guard; `signinRedirect()` |
| redirecting | callback authenticated | authenticated | `onSigninCallback(user)` restores `user.state.returnTo` (FR-013) — both apps |
| redirecting | callback NOT authenticated ∧ loop-guard set | expired-final | full-screen session-expired state with manual retry (FR-014) — no redirect loop |
| expired (management) | — | prompt | explicit re-sign-in prompt replaces generic error banners (FR-014) |

**Persisted records** (`sessionStorage`, non-secret):
- `sse.auth.returnTo` — pathname stash (also carried in OIDC `state`; storage is the fallback when the provider drops state)
- `sse.auth.redirectGuard` — timestamp of last automatic redirect (loop prevention)
- `sse.crash.count` / `sse.crash.lastAt` — crash-loop watchdog counters (US4): reload delays 5/15/60 s; counters cleared after 5 min stable

## Cross-machine interaction (edge cases)

- **Outage longer than the session**: machines are independent; the hub
  and stream machines keep retrying with 401s until the session machine
  reaches `authenticated`, whereupon the next attempt succeeds (order-
  independent recovery — spec edge case). No coordination code needed
  beyond FR-015's fresh-token-per-attempt.
- **Mass failure jitter**: stream retries jitter per tile (independent
  RNG draws), the hub retry jitters per app → 16 tiles + hub stay under
  the SC-005 attempt budget.
- **Clock steps**: all three machines schedule with relative timeouts /
  `performance.now()` only.
