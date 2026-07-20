# Tasks: Frontend 24/7 Resilience

**Input**: Design documents from `/specs/011-frontend-247-resilience/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/resilience-interfaces.md, quickstart.md

**Tests**: Included — this feature exists because failure paths were untested; every story ships its failure-mode tests (component tests with fake timers write-first where practical).

**Organization**: Grouped by user story (US1 streams P1, US2 live updates P2, US3 sessions P2, US4 crash containment P3) so each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4 per spec.md

## Phase 1: Setup

**Purpose**: The one helper every story logs through (FR-017).

- [x] T001 Create `logResilienceEvent(subsystem, transition, detail?)` per contracts §8 in apps/shared/src/observability/resilienceLog.ts, with unit test apps/shared/src/observability/resilienceLog.spec.ts (stable `[resilience]` prefix, structured payload)

---

## Phase 2: Foundational (env-config contract)

**Purpose**: FR-010 fail-loudly configuration — US2 (hub URL) and US3 (auth guards) both build on it.

- [x] T002 [P] Create hub-URL resolution `VITE_LAYOUT_HUB_URL ?? '/hubs/layouts'` with PROD module-load throw per contracts §6 in apps/shared/src/realtime/hubUrl.ts + apps/shared/src/realtime/hubUrl.spec.ts
- [x] T003 [P] Add PROD guard (throw when `VITE_API_GATEWAY_URL` missing in `import.meta.env.PROD`) in apps/shared/src/api/gateway.ts, keeping dev/test same-origin fallback
- [x] T004 [P] Add PROD guard for `VITE_KEYCLOAK_URL` in apps/kiosk-web/src/app/auth.ts and apps/management-web/src/app/auth.ts (dev fallback unchanged)

**Checkpoint**: `pnpm typecheck && pnpm test` green; dev behaviour unchanged.

---

## Phase 3: User Story 1 — Video streams recover on their own and never lie (P1) 🎯 MVP

**Goal**: Dead peer connections are detected, truthfully surfaced, retried indefinitely with jittered backoff, and WHEP sessions are released on teardown (FR-001…005).

**Independent Test**: quickstart §1 — stop/start `mediamtx`; tiles must leave "Live" ≤ 10 s, keep retrying, resume unaided; layout switch DELETEs sessions.

- [x] T005 [US1] Write failing WhepClient tests (connection-state callback, Location capture, DELETE-on-close exactly once, close-without-Location, abort-mid-connect leaves no live PC, ≤250 ms gathering wait) in apps/shared/src/streaming/WhepClient.test.ts
- [x] T006 [US1] Extend WhepClient per contracts §1: `onConnectionStateChange` option, ICE-gathering wait (250 ms cap), capture WHEP `Location`, fire-and-forget bearer DELETE (`keepalive: true`) in `close()` before unconditional local teardown, in apps/shared/src/streaming/WhepClient.ts
- [x] T007 [US1] Write failing CameraViewer state-machine tests (data-model §1: live requires connected PC; `failed` → immediate retry; `disconnected` → 5 s grace; connect-reject → backoff 1 s base ×2 cap 15 s ±20% jitter, unbounded; `Offline` suspends; Degraded→Healthy bumps nonce not label; teardown aborts + closes) with fake timers in apps/shared/src/ui/composites/CameraViewer.test.tsx
- [x] T008 [US1] Implement truthful status + retry ladder in CameraViewer (retryNonce effect dep, per-attempt WhepClient instance, `logResilienceEvent('stream', …)` on every transition) in apps/shared/src/ui/composites/CameraViewer.tsx
- [x] T009 [US1] Update the existing management-web viewer suites for the new semantics (no behavioural assertions weakened — "Reconnects automatically" now true) in apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx and CameraViewer.test.tsx

**Checkpoint**: US1 fully verifiable via quickstart §1 on its own — MVP.

---

## Phase 4: User Story 2 — Live updates survive outages and catch up (P2)

**Goal**: Unbounded hub reconnect + initial-start retry, visible degraded badge, effective reconciliation (snapshot `'ALL'` fix, per-overlay refetch, pre-mount archived overlays), prod hub URL (FR-006…010).

**Independent Test**: quickstart §2 — stop/start the hub host; badge appears/clears; value + archive changes made while down appear ≤ 10 s after reconnect; pre-mount archive shows "Overlay unavailable".

- [x] T010 [US2] Write failing layoutHub tests (custom retry policy never returns null, ladder 0/2/5/10/30 s ±20% jitter; `onclose` schedules restart; `stop()` cancels pending retries; `onStateChange` sequence connecting→connected→degraded→connected) in apps/shared/src/realtime/layoutHub.spec.ts
- [x] T011 [US2] Implement contracts §2 in apps/shared/src/realtime/layoutHub.ts: unbounded `IRetryPolicy`, `onclose` restart loop, `onStateChange` callback, `start()` owns initial-retry, consume hubUrl.ts (T002)
- [x] T012 [P] [US2] Fix `getOverlaySnapshot.providesTags` to add the `{OverlaySnapshot, id:'ALL'}` sentinel (contracts §5) in apps/shared/src/api/systemVariables.api.ts, with a test proving a mounted snapshot query refetches on an `'ALL'` invalidation in apps/shared/src/api/systemVariables.api.spec.ts
- [x] T013 [US2] Rework useLayoutLifecycle (contracts §3): remove the false "recovers on next automatic reconnect" comment/catch, delegate retry to the hub handle, return `{ degraded }`, extend reconnect reconciliation with bare-`Overlay` invalidation, `logResilienceEvent('hub', …)`, in apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts + its test apps/kiosk-web/src/features/revocation/useLayoutLifecycle.test.tsx (reconnect → all four invalidations dispatched)
- [x] T014 [US2] Add the degraded badge to kiosk pages and derive Tile "Overlay unavailable" from fetched overlay state (no Published revision) in addition to pushed `OverlayArchived` frames (FR-009), in apps/kiosk-web/src/features/cell/CellPage.tsx and apps/kiosk-web/src/features/picker/PickerPage.tsx, with component tests for badge visibility + pre-mount-archived rendering
- [x] T015 [US2] Add kiosk Playwright project (baseURL 5174) and degraded-indicator e2e (route-abort `/hubs/**` → badge shown; unroute → badge clears) in playwright.config.ts and e2e/kiosk-live-updates.spec.ts, reusing e2e/support/sign-in.ts

**Checkpoint**: US1 + US2 independently verifiable.

---

## Phase 5: User Story 3 — Sessions renew invisibly; expiry is never silent (P2)

**Goal**: Silent renewal, one-retry-on-401 with shared renewal mutex, kiosk auto re-sign-in with deep-link restoration and loop guard, explicit expired states (FR-011…015).

**Independent Test**: quickstart §3 — shortened-lifetime realm; token expiry invisible; SSO lapse → same layout after auto redirect, or the dedicated expired screen; management gets an explicit prompt; no silent 401 failures.

- [x] T016 [US3] Write failing gateway reauth tests (401 → one renew → retry original; renew-false/second-401 → onSessionExpired + 401 passthrough; non-401 untouched; concurrent 401s share ONE renewal) in apps/shared/src/api/gateway.spec.ts
- [x] T017 [US3] Implement contracts §4 in apps/shared/src/api/gateway.ts: `setSessionRenewer`, `setOnSessionExpired`, reauth-wrapping `gatewayBaseQuery` with renewal mutex, `logResilienceEvent('session', …)`
- [x] T018 [P] [US3] Kiosk session flow (data-model §3): register renewer/expiry handlers, oidc `addSilentRenewError`/`addAccessTokenExpired` wiring, auto `signinRedirect({state})` with `sessionStorage` loop guard (`sse.auth.redirectGuard`), full-screen session-expired state, `onSigninCallback` restores stashed path, in apps/kiosk-web/src/App.tsx and apps/kiosk-web/src/app/auth.ts, with AuthGate state tests in apps/kiosk-web/src/App.test.tsx (expired→redirect once, guard→expired-final screen, callback restores path)
- [x] T019 [P] [US3] Management session flow: register renewer, session-expired re-sign-in prompt replacing generic failure, return-path restoration through `onSigninCallback`, in apps/management-web/src/App.tsx and apps/management-web/src/app/auth.ts, with tests in apps/management-web/src/App.test.tsx
- [x] T020 [P] [US3] FR-015 regression tests: WHEP `getToken` and hub `accessTokenFactory` resolve the CURRENT token on a post-renewal (re)connect attempt, in apps/shared/src/streaming/WhepClient.test.ts and apps/shared/src/realtime/layoutHub.spec.ts

**Checkpoint**: US1–US3 independently verifiable.

---

## Phase 6: User Story 4 — One error never takes down the whole display (P3)

**Goal**: Shared error boundary; management bounded panel + retry; kiosk reload watchdog with 5/15/60 s backoff; monotonic timed UI (FR-016; clock-step edge case).

**Independent Test**: quickstart §4 — `?crash=render` dev trigger; management panel retries; kiosk auto-reloads to the same layout with stepped delays.

- [x] T021 [US4] Create ErrorBoundary per contracts §7 (class component, render-prop fallback, `onError` hook) with tests in apps/shared/src/ui/composites/ErrorBoundary.tsx + ErrorBoundary.test.tsx
- [x] T022 [US4] Kiosk crash recovery: fallback scheduling `location.reload()` with `sse.crash.count`/`sse.crash.lastAt` backoff (5/15/60 s, clear after 5 min stable), URL preserved, dev-only `?crash=render` trigger stripped before reload, wire into apps/kiosk-web/src/App.tsx, tests with fake timers in apps/kiosk-web/src/features/recovery/KioskCrashRecovery.test.tsx (new module apps/kiosk-web/src/features/recovery/KioskCrashRecovery.tsx)
- [x] T023 [P] [US4] Wrap the management shell in ErrorBoundary with bounded panel + reset fallback in apps/management-web/src/App.tsx, test in apps/management-web/src/App.test.tsx
- [x] T024 [P] [US4] Make highlight timers leak-free and monotonic (tracked handles cleared on unmount; `performance.now()` deltas instead of `Date.now()` epochs) in apps/kiosk-web/src/features/cell/CellPage.tsx, with a fake-timer test covering unmount-clears-timers and clock-step immunity

**Checkpoint**: all four stories independently verifiable.

---

## Phase 7: Polish & Cross-Cutting

- [x] T025 [P] Document the deploy env contract (contracts §6 table: `VITE_API_GATEWAY_URL`, `VITE_KEYCLOAK_URL`, `VITE_LAYOUT_HUB_URL`, PROD fail-loudly) in docs/deployment-frontend-env.md and link it from the throw messages
- [x] T026 Run the full gates — `pnpm typecheck`, `pnpm lint`, `pnpm test`, `pnpm test:e2e` — and fix fallout; verify a `pnpm build` (PROD) of both apps fails loudly with the env vars unset and succeeds with them set
- [ ] T027 Execute quickstart.md §§1–4 end-to-end against the Aspire stack and record the observations (per-SC pass/fail, console `[resilience]` excerpts) as the Phase-5 verification note for the PR

---

## Dependencies & Execution Order

- **Phase 1 → 2 → stories**: T001 blocks all stories (logging calls); T002 blocks T011; T003/T004 block T017/T018/T019 only in prod-guard edge tests — practically, finish Phase 2 first (it is < 1 h).
- **US1 (T005–T009)**: independent of US2–US4. T005→T006→T007→T008→T009 (T005/T007 may be written together; tests-first).
- **US2 (T010–T015)**: T010→T011→T013→T014→T015; T012 [P] anytime. Independent of US1/US3/US4.
- **US3 (T016–T020)**: T016→T017, then T018/T019/T020 in parallel. T020 touches files owned by US1/US2 test suites — sequence after T006/T011 land if run in the same worktree, or rebase.
- **US4 (T021–T024)**: T021→T022; T023/T024 [P]. **File-overlap warning (ADR-0109)**: kiosk `App.tsx` is touched by T018 (US3) and T022 (US4); management `App.tsx` by T019 and T023; `CellPage.tsx` by T014 (US2) and T024 (US4). If parallelizing stories across worktree agents, assign US3+US4 to one agent (or land US3 before US4) and keep T014/T024 ordered.
- **Polish**: T025 [P] anytime after Phase 2; T026/T027 last.

### Parallel opportunities

- Phase 2: T002/T003/T004 all [P].
- After Phase 2, US1 and US2 are fully disjoint file sets → two parallel worktree agents per ADR-0109.
- Within US3: T018/T019/T020 in parallel after T017.
- T012, T023, T024, T025 are drop-in parallel tasks.

## Implementation Strategy

**MVP = Phase 1 + 2 + US1** (stream truthfulness/recovery is the P1 safety property; independently shippable and demonstrable via quickstart §1). Then US2 → US3 in either order (both P2; US2 is lower-risk, US3 has the Keycloak-policy dependency — spec Assumption 1), US4 last. Each story ends at a checkpoint that quickstart can verify without the later stories. Commits follow ADR-0030; every commit references its task ID (e.g. `feat(011): T008 …`).
