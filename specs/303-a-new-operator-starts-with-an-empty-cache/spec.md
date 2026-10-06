# Spec 303 — A new operator starts with an empty cache

**Issue:** #2524 (observation 2 only; found in #2432's phase-6 review, spec 211)
**Branch:** `fix/2524-reset-api-cache-on-subject-change`
**App:** `apps/management-web` · **ADRs:** 0075 (one Redux store, RTK Query), 0080 (browser auth, `react-oidc-context`), 0144 (lane — the decision below was made outside it), 0150 (wait on a condition, not a count)
**Engineer:** frontend · **Phase 4a colour:** behaviour-changing → **red first**
**Latency budget:** N/A — management-web only; nothing on the event→overlay path (constitution §IV).

## 1. The decision being implemented (human, 2026-10-06, on #2524)

> Decision: reset the stale-camera cache on subject change via an OIDC event
> handler, not a per-page effect. A subject change is an auth-layer concern
> shared by all 6 affected pages — a per-page fix would need repeating 6 times
> and could drift out of sync.

This spec implements that decision and does not revisit it. No new ADR: the
placement was the open question, and the decision settled it.

**Out of scope — observation 1 of #2524** (a mid-session `403` for the *same*
subject keeps the last-known-good record on screen indefinitely). No subject
change happens in that scenario, so this mechanism never fires for it. Its
suggested bound ("after N consecutive failed refreshes") picks no N, no reset
rule and no scope, and it has to hold alongside spec 030 FR-008. That is an
undecided design question. It goes to a separate issue and is not built here.

## 2. Problem

The six pages `CameraDetailPage`, `CamerasPage`, `AuditPage`, `LayoutsPage`,
`OverlaysPage` and `SystemVariablesPage` render cached data next to a
refresh-failure banner. Their data comes from **five separate RTK Query slices**
(`camerasApi`, `streamsApi`, `auditApi`, `layoutsApi`, `overlaysApi`,
`systemVariablesApi`). Those slices sit in one store
(`apps/management-web/src/app/store.ts`), which holds **eight** slices once
`rulesApi` and `wallsApi` are counted. There is no single `api` object, so the
issue's `api.util.resetApiState()` has to become one dispatch per slice.

If a silent renewal (`gateway.ts`'s 401 path through `signinSilent()`, or
oidc-client-ts's automatic renew) returns a user with a different `profile.sub`,
nothing clears the previous operator's cache. The new operator then sees the
old operator's record, with a "could not refresh" banner, where they should see
a clean refusal. `resetApiState` has no call site in the app today; it appears
only in tests.

## 3. User story

**US1 (P1)**: As an operator who takes over a management console whose
authenticated subject has changed underneath it, I see only data fetched under
my own identity, never what the previous subject had cached.

### Acceptance scenarios

```gherkin
Scenario: A renewal that returns a different subject clears every cache
  Given operator A is signed in and camera C is cached from A's request
  When a silent renewal completes with a user whose sub is B
  Then every RTK Query slice in the store is back to its initial state
    And camera C is fetched again with B's bearer

Scenario: The new subject is refused, and sees a refusal rather than A's record
  Given operator A has camera C on screen
    And B may not see camera C (the API answers 404 to B)
  When a request 401s, the renewal returns B, and the retry runs
  Then camera C's name, address and controls are no longer rendered
    And the page shows the refusal surface, not "could not refresh"

Scenario: An ordinary renewal for the same subject keeps the cache (conflict)
  Given operator A has camera C cached
  When a silent renewal completes with a user whose sub is still A
  Then nothing is reset, camera C stays rendered
    And no extra request is issued because of the renewal

Scenario: Signing out does not count as a subject change (bad input)
  Given operator A was signed in
  When the user is unloaded (no subject)
  Then nothing is reset on that event
    And a later load with sub B still counts as a change from A

Scenario: The first sign-in of a page load resets nothing (auth bootstrap)
  Given the app has just loaded and no subject has been seen yet
  When the first user resolves with sub A
  Then nothing is reset
```

## 4. Functional requirements

- **FR-001**: When the OIDC user-loaded event carries a `profile.sub` that differs from the last subject this page load has seen, every RTK Query slice registered in the management-web store MUST be reset (`<slice>.util.resetApiState()`).
- **FR-002**: The reset MUST happen inside the OIDC event handler (`userManager.events.addUserLoaded`, reached through `useAuth().events`), not in a page or a per-page effect. oidc-client-ts raises that event before `signinSilent()` resolves, so the cache is cleared before the gateway retries the 401.
- **FR-003**: A user that loads with the same subject MUST NOT reset anything. Routine token renewal must not blank every page.
- **FR-004**: An absent subject (unload, sign-out, still loading) MUST NOT reset anything and MUST NOT erase the last seen subject.
- **FR-005**: The list of slices to reset MUST come from the same source the store is built from, and a test MUST fail if a slice is added to the store without also being reset.
- **FR-006**: The reset MUST NOT branch on the HTTP status of any failure. This keeps spec 030 FR-008 intact.
- **FR-007**: No change to `apps/shared/src/api/gateway.ts`, the six pages, or `kiosk-web`.

## 5. Independent end-to-end test

1. Unit: render the real `AuthProvider` around a real `UserManager` (`automaticSilentRenew: false`) and the real store, as `staleBearerRetry.test.tsx` does. Mount a probe that uses `useGetCameraQuery`. Serve camera C to A's bearer. Then make a request 401, and stub `manager.signinSilent` to `storeUser` and `events.load` a user whose sub is B. The retry and refetch get a 404. Observe that C's name is gone and the query reports the error with no data.
2. Manual (phase 5): sign in as `operator` and open `/cameras/<id>`. In a second tab, end the Keycloak SSO session and sign in as another seeded user with no access to that camera's fab. Back in the first tab, trigger a silent renew. Observe the refusal, not the cached record.

## 6. Residual exposure (recorded, not built)

- Observation 1 of #2524 (same-subject `403`) is unchanged. See §1.
- `kiosk-web` has its own store and auth flow. A wall display signs in once as a fixed account, so the exposure there is unexamined, not proven absent. This slice does not touch it.
