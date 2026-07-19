# Implementation Plan: Frontend 24/7 Resilience

**Branch**: `011-frontend-247-resilience` | **Date**: 2026-07-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-frontend-247-resilience/spec.md`

## Summary

Make both SPAs survive real-world failure conditions unattended: (1) the
WHEP viewer detects dead peer connections via `RTCPeerConnection`
connection-state events and re-establishes sessions indefinitely with
jittered backoff, releasing sessions via the WHEP `Location` DELETE on
teardown; (2) the SignalR layout hub gets an unbounded retry policy, an
initial-connect retry loop, an `onclose` restart, a surfaced
connected/degraded state, and a deploy-time endpoint configuration;
(3) reconnect reconciliation is fixed (the `OverlaySnapshot id:'ALL'`
no-op) and extended to per-overlay caches, layout lifecycle, and
pre-mount archived overlays; (4) a `baseQueryWithReauth` wrapper plus
oidc event handling give silent renewal, one-retry-on-401, automatic
kiosk re-sign-in with deep-link restoration, and explicit
session-expired states; (5) shared React error boundaries contain
crashes — management with retry, kiosk with a backoff-guarded
auto-reload watchdog. No new dependencies; all changes live in
`apps/shared`, `apps/kiosk-web`, `apps/management-web`, plus dev/prod
wiring in `AppHost`/deploy config.

## Technical Context

**Language/Version**: TypeScript 5.7 (strict, NRT-equivalent flags per `tsconfig.base.json`), React 18, Node ≥ 22 toolchain

**Primary Dependencies**: Redux Toolkit 2.5 + RTK Query, `@microsoft/signalr` (WebSocket transport, ADR-0076 v1), `react-oidc-context` / `oidc-client-ts` (ADR-0080), native `RTCPeerConnection` (WHEP against MediaMTX), react-router (kiosk), Vite 5. **No new packages.**

**Storage**: N/A (browser state only; RTK Query cache + `sessionStorage` for crash-loop counters and return-path stash)

**Testing**: Vitest + React Testing Library with fake timers and the existing hand-written `RTCPeerConnection`/fetch mocks; Playwright e2e (`/e2e`, ADR-0108) gains a kiosk project with route-interception-induced outages

**Target Platform**: Evergreen Chromium kiosks and operator browsers (constitution: Frontend)

**Project Type**: Web frontend (two SPAs + shared package in the pnpm workspace)

**Performance Goals**: Steady-state event→overlay push and render path untouched (§IV legs unaffected); recovery targets are the spec's SCs — dead stream flagged ≤ 10 s, wall fully live ≤ 60 s after outage ends, reconciliation ≤ 10 s after reconnect

**Constraints**: Reconnect storms bounded (≤ 30 attempts/min/subsystem/kiosk via backoff + jitter); retry timers monotonic (no wall-clock arithmetic — fab clocks are PTP-stepped); no long-lived secrets in the browser (constitution: Security)

**Scale/Scope**: 16+ tiles per kiosk wall, 20-kiosk walls rebooting unattended, 250-camera fab target

## Constitution Check

*GATE: evaluated pre-Phase 0 and re-checked post-design — PASS (no violations, no Complexity Tracking entries).*

| Principle | Assessment |
|---|---|
| I. On-prem first | All recovery logic is client-local; no new external dependency. Deploy-time hub URL config mirrors the existing `VITE_API_GATEWAY_URL` mechanism. PASS |
| II. DDD / value objects | Frontend-only feature; no domain-boundary changes. Backend untouched. PASS |
| III. Context isolation | No cross-context references introduced; SPAs keep talking to the gateway + hub only. PASS |
| IV. Latency budget | Steady-state push/render path is not modified — this changes *failure and recovery* behaviour. Legs touched: none in steady state. The `Tile` render path is not restructured here (perf memoization is out of scope). PASS — PR will cite "no leg affected in steady state" with reasoning |
| V. Spec-driven | This plan implements spec 011. PASS |
| VI. Aspire composition root | Dev: existing Vite `/hubs` proxy stays; the new `VITE_LAYOUT_HUB_URL` is optional in dev and, where supplied, is set through AppHost env wiring like `VITE_API_GATEWAY_URL`. PASS |
| VII. Observability | FR-017 satisfied with structured `console` logging of every resilience transition (browser OTel export is not part of the current stack; noted as future work, not a gate). PASS |
| VIII. Safe at trust boundaries | Tokens stay short-lived; re-auth uses existing OIDC flows; no secrets persisted (only a non-secret return-path and crash counter in `sessionStorage`). Kiosk device-bound `client_credentials` (constitution: Availability) remains the deferred identity feature — spec Assumption 1. PASS |
| IX. Forward-compat interfaces | `LayoutHubHandle` remains the ADR-0076 transport seam; its surface is extended (state callback), not bypassed. The dead `RealtimeClient` interface is *not* touched here (separate cleanup). PASS |

## Project Structure

### Documentation (this feature)

```text
specs/011-frontend-247-resilience/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (client state machines)
├── quickstart.md        # Phase 1 output (how to induce + verify failures)
├── contracts/
│   └── resilience-interfaces.md   # Extended client interfaces + env contract
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
apps/shared/src/
├── streaming/
│   ├── WhepClient.ts            # + connection-state callback, Location capture, DELETE on close
│   └── WhepClient.test.ts       # + state-change, abort-race, close/DELETE tests
├── ui/composites/
│   ├── CameraViewer.tsx         # + truthful status from PC state, jittered retry loop
│   ├── CameraViewer.test.tsx    # NEW — suite moves/extends beside the component
│   └── ErrorBoundary.tsx        # NEW — shared boundary (render-prop fallback)
├── realtime/
│   ├── layoutHub.ts             # + unbounded retry policy, onclose restart, state callback
│   └── hubUrl.ts                # NEW — VITE_LAYOUT_HUB_URL resolution + PROD guard
└── api/
    ├── gateway.ts               # + baseQueryWithReauth, session callbacks, PROD env guard
    └── systemVariables.api.ts   # fix OverlaySnapshot providesTags ('ALL')

apps/kiosk-web/src/
├── app/auth.ts                  # + return-path state, PROD guard, renew-error posture
├── App.tsx                      # + auto re-signin flow, session-expired screen, boundary
├── features/revocation/
│   └── useLayoutLifecycle.ts    # + start-retry loop, degraded state out, richer reconcile
└── features/cell/CellPage.tsx   # + degraded badge, unavailable-from-state, monotonic timers

apps/management-web/src/
├── app/auth.ts                  # + return-path state, PROD guard
└── App.tsx                      # + session-expired prompt, boundary

apps/kiosk-web/vite.config.ts    # unchanged dev proxy (documented)
e2e/                             # + kiosk Playwright project + degraded-indicator spec
playwright.config.ts             # + second project (baseURL :5174)
```

**Structure Decision**: All resilience mechanics live in `apps/shared`
(they serve both apps); the apps contribute only wiring (auth posture,
screens, badges). This respects the existing shared/composite split and
keeps the ADR-0109 disjoint-file property: shared streaming/realtime
files, kiosk files, and management files partition cleanly.

## Design Outline (per user story)

### US1 — Stream recovery (P1)

- `WhepClient` gains `onConnectionStateChange?: (s: RTCPeerConnectionState) => void`
  (subscribed to `pc.onconnectionstatechange`), captures the WHEP
  `Location` response header, and `close()` fire-and-forgets a `DELETE`
  to it (bearer-authenticated, `keepalive: true`) before tearing the PC
  down locally — teardown never awaits or fails on the DELETE (FR-004).
- Offer POST waits for ICE gathering complete with a 250 ms cap
  (single-L2 fabs gather host candidates in ms; the cap keeps the
  latency budget honest) — improves first-attempt success (FR-003).
- `CameraViewer` derives status truthfully: `live` only while the PC is
  `connected` (FR-002). A `failed`/`disconnected` state or a rejected
  `connect()` schedules a retry via a `retryNonce` in the effect deps
  with exponential backoff + full jitter (base 1 s, cap 15 s, reset on
  success) — indefinite (FR-001/003). `Degraded → Healthy` health
  transitions bump the nonce instead of relabeling (FR-005).
- Backoff timers use `setTimeout` durations only (no wall-clock
  arithmetic) — clock-step safe (edge case).

### US2 — Live-update resilience (P2)

- `layoutHub.ts`: custom `IRetryPolicy` (0/2/5/10/30 s then every 30 s
  ± jitter, unbounded) replaces bare `withAutomaticReconnect()`;
  `connection.onclose` triggers a scheduled restart; a new
  `onStateChange(connected | degraded)` callback feeds the UI (FR-006/007).
  `LayoutHubHandle` stays the ADR-0076 seam.
- `useLayoutLifecycle`: owns the initial-start retry loop (backoff, same
  policy) replacing the swallowed-catch comment; returns
  `{ degraded: boolean }` so `CellPage`/`PickerPage` render a discreet
  badge (FR-007).
- Reconciliation on reconnect (FR-008): keep `LayoutList`/`OverlayList`
  invalidations, fix the snapshot no-op by making `getOverlaySnapshot`
  also provide `{ type: 'OverlaySnapshot', id: 'ALL' }` (mirrors the
  list-tag pattern), and additionally invalidate the bare `Overlay` type
  so per-overlay geometry/text caches refetch. Layout revocation
  reconciles through the existing `LayoutList` refetch path CellPage
  already consumes.
- FR-009: `Tile` derives "Overlay unavailable" from the fetched overlay
  state (no Published revision) in addition to live `OverlayArchived`
  frames.
- FR-010: new `hubUrl.ts` resolves `VITE_LAYOUT_HUB_URL ?? '/hubs/layouts'`;
  in `import.meta.env.PROD` a missing value **throws at module load**.
  Same PROD guard added for `VITE_API_GATEWAY_URL` and
  `VITE_KEYCLOAK_URL` (fail-loudly, spec FR-010; dev/test fallbacks
  unchanged).

### US3 — Session survival (P2)

- `gateway.ts`: `baseQueryWithReauth` wraps `fetchBaseQuery`; on 401 it
  invokes an app-registered `renewSession(): Promise<boolean>` once and
  retries; renewal failure invokes an app-registered `onSessionExpired`
  (FR-011/012). Registration mirrors `setAccessTokenProvider` (module
  singleton, set during render — same ADR-0106 race rationale).
- Apps register `renewSession = () => auth.signinSilent()…`; oidc events
  (`addSilentRenewError`, `addAccessTokenExpired`) route to the same
  expiry path. `automaticSilentRenew` (library default) keeps working
  for the happy path.
- Kiosk expiry flow (FR-013/014): on session-expired, stash
  `location.pathname` and call `signinRedirect({ state })`; a redirect
  round-trip that returns unauthenticated (loop guard flag in
  `sessionStorage`) renders the dedicated full-screen session-expired
  state instead of redirect-looping. `onSigninCallback(user)` restores
  the stashed path in both apps (also fixes the deep-link loss).
- FR-015 holds structurally: WHEP `getToken` and the hub
  `accessTokenFactory` already dereference the latest token per attempt;
  a regression test pins this.

### US4 — Crash containment (P3)

- Shared `ErrorBoundary` (class component, render-prop fallback).
  Management wraps the page shell → bounded error panel + retry (reset).
  Kiosk fallback schedules `location.reload()` with a crash-loop
  counter in `sessionStorage` (delays 5/15/60 s, capped; counter clears
  after 5 min stable) — returns to the same URL/layout (FR-016).
- Kiosk highlight timers move to tracked, cleaned-up handles with
  monotonic expiry (`performance.now()`), closing the timer-leak /
  clock-step edge case.
- FR-017: a tiny `logResilienceEvent(subsystem, transition, detail)`
  helper (structured `console.info`) called at every transition.

## Testing Strategy

- **Unit/component (Vitest, fake timers)**: WhepClient state-change +
  DELETE-on-close + abort race; CameraViewer retry ladder (never "Live"
  with a dead PC; jitter bounds); hub retry policy + onclose restart +
  start-retry; reauth wrapper (401 → renew → retry; failure → expired);
  snapshot tag fix (reconnect invalidation refetches a mounted snapshot
  query); boundary reload backoff. CameraViewer suite gains a home in
  `apps/shared` (component and tests co-located; management keeps its
  integration-level tests).
- **e2e (Playwright)**: new kiosk project (baseURL 5174); degraded
  indicator via `page.route` abort of `/hubs/**` negotiate; recovery of
  the indicator after unrouting. Stream-loss e2e is not automatable
  against real MediaMTX in CI — covered by quickstart manual protocol
  instead (SC-001/002/003 verification).
- **Verify phase (Phase 5)**: quickstart.md documents the induced-failure
  protocol against the Aspire stack (stop/start mediamtx and the hub
  service, shortened-lifetime Keycloak test realm) and which SCs each
  step demonstrates.

## Complexity Tracking

No constitution violations — table intentionally empty.
