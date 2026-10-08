# Spec 314 — The strike a frame swallowed

**Issue:** [#2762](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2762)
— filed as *CameraDetailRevocation.test.tsx's "three consecutive 403s" test has flaked on 3
unrelated PRs*; root-caused in the issue's comments to a production defect in
`useRevocationFallback`, not a test flake.
**Branch:** `fix/2762-revocation-strike-counting-race` (cut from `origin/develop` @ `4ee7060d`)
**Created:** 2026-10-08
**ADRs:** 0075 (Redux Toolkit + RTK Query), 0139 (new behaviour starts red), 0109 (disjoint files),
0037 (phased workflow).
**Amends:** spec 310 §1.2 (the hook's counting mechanism — *how* a settlement is observed). The
strike *condition*, threshold, reset rule and subject semantics of spec 310 are unchanged.
**Composes with:** spec 313 / #2750 (PR #2775, unmerged at time of writing) — that spec widens the
strike *condition*; this one fixes strike *counting*. See plan §6.

**Spec number.** 2026-10-08: `origin/develop` and every remote branch top out at **313**
(`313-the-camera-a-removed-operator-keeps`, #2750's branch); worktrees `sse-2337` and `sse-2750`
carry nothing above 313. Re-check before opening the PR (memory: *spec number: origin/develop
isn't enough*).

**No ADR.** Application-level hook code inside the existing RTK Query decision (ADR-0075). The
listener middleware it registers is part of Redux Toolkit, already a locked dependency; no new
package, runtime resource or pattern outside RTK's own documented API.

## 0. The defect (proven in #2762, re-checked against RTK 2.12.0 source)

- `configureStore`'s default enhancer is `autoBatchEnhancer({ type: 'raf' })`
  (`@reduxjs/toolkit/src/autoBatchEnhancer.ts`). For any action carrying `meta.RTK_autoBatch`,
  the store's **state updates synchronously**, but `store.subscribe` listeners are only notified
  on the next animation frame (or a 100 ms fallback timer). The enhancer **replaces**
  `store.subscribe` itself, so *every* subscriber — react-redux, and anything a hook could
  subscribe — sees only the state at flush time, never the states in between.
- RTK Query's `pending`, `fulfilled` **and** `rejected` query actions all carry that flag
  (`query/core/buildThunks.ts:180-186`).
- `useRevocationFallback` counts a strike only for a `requestId` it **renders** as settled
  (`useRevocationFallback.ts:55`). If request *r2*'s rejection and request *r3*'s `pending` land
  in one unflushed window, React renders *r3* fetching and *r2* is never seen settled —
  **its strike is dropped** (count stops at 2; the record stays on screen).
- The same mechanism **drops a reset**: a 200 swallowed between two 403s never zeroes the count,
  so 403 · [200 swallowed] · 403 · 403 refuses on what is really a run of two.
- #2762's counterfactual: an artificial 90 ms rAF delay gave **3/8 failures with the default
  enhancer, 0/8 with autobatch disabled**; the race opens only when the frame arrives later than
  the settle-to-next-request gap. Two earlier test-only fixes (#2763 settlement wait, #2766 5 s
  timeout) could not help — no wait recovers a strike that was never counted.
- **Why rendered-snapshot inference cannot fix it.** RTK keeps `error` on a refetching entry, so
  "requestId changed from an uncounted *r2* to *r3*, error still 403" could be read as *r2*'s
  strike. But when *r2*'s pending, *r2*'s settlement and *r3*'s pending all share one window, the
  hook goes from a counted *r1* straight to *r3* and cannot tell whether a request ran in between.
  Only the action stream carries that information; state does not keep a settlement history.

## 1. User stories

### US1 (P1) — Every settled refresh is counted exactly once, whatever the frame timing

As a shift supervisor, when an operator's access is revoked mid-session, I need the third refused
refresh to take the record off screen **every time** — not only when the browser happened to paint
between refreshes — and a successful refresh in between to reset the count every time, so that the
spec-310 three-strike rule means what it says.

**Why P1 / the whole slice:** it is the issue. One hook mechanism, one store registration, one
argument at six call sites. Observable end to end through the existing spec-310 page behaviour.

### Acceptance scenarios (Gherkin)

```gherkin
Feature: Revocation strikes are counted from settlements, not from renders

  Background:
    Given a store whose autobatch notifications are held until the test releases them
    And a page-shaped component using a real RTK Query endpoint and useRevocationFallback
    And the endpoint's responses are scripted per request

  # Happy path — the dropped strike
  Scenario: A 403 whose notification is coalesced with the next request still counts
    Given the first load answered 200 and was rendered
    And refresh r1 answered 403 and was rendered settled
    When refresh r2 answers 403 and refresh r3 starts before notifications are released
    And notifications are released
    Then r2's settled state was never rendered
    When r3 answers 403 and notifications are released
    Then the hook reports refused

  # Conflict — the dropped reset
  Scenario: A 200 coalesced between refusals still resets the count
    Given the first load answered 200 and was rendered
    And refresh r1 answered 403 and was rendered settled
    When refresh r2 answers 200 and refresh r3 starts before notifications are released
    And r3 answers 403, then refresh r4 answers 403, each released and rendered
    Then the hook does not report refused

  # The case snapshot inference cannot see
  Scenario: A request that started and settled entirely inside one window still counts
    Given the first load answered 200 and r1 answered 403, both rendered settled
    When r2 starts, answers 403, and r3 starts, all before notifications are released
    And r3 answers 403 and notifications are released
    Then r2 was never rendered at all
    And the hook reports refused

  # Bad request — misconfigured store
  Scenario: A store without the listener middleware fails loudly
    Given a store with a react-redux Provider but no listener middleware
    When a component calls useRevocationFallback
    Then rendering throws an error naming the missing listener middleware

  # Auth surface — unchanged semantics (spec 310 / 313)
  Scenario Outline: Settlement outcome classification is unchanged
    Given three consecutive settled responses with status <status>
    Then refused is <refused>
    Examples:
      | status | refused |
      | 403    | true    |
      | 503    | false   |
      | 200    | false   |

  Scenario: A settlement for the previous subject never strikes the new one
    Given subject A has two counted 403s
    When the subject changes to B while A's request is still in flight
    And A's request answers 403
    Then B's count is unaffected

  Scenario: StrictMode does not double-count a settlement seen by both paths
    Given the component is rendered under React.StrictMode
    When three refreshes each answer 403 with notifications released after each
    Then refused becomes true on the third and not before
```

## 2. Functional requirements

- **FR-001** The hook MUST observe each settlement of its query's cache entry **at dispatch time**
  (RTK's listener middleware, `addListener` dispatched from the hook), independent of when
  subscribers are notified.
- **FR-002** Ownership of a settlement MUST be exact: an action counts only if it is the endpoint's
  `fulfilled`/`rejected` action **and** the post-reducer cache entry for the hook's args carries
  that action's `requestId` (so condition-aborted and superseded requests never count, exactly as
  RTK's reducer ignores them).
- **FR-003** Each settlement MUST be applied at most once across both observation paths (listener,
  and the existing render path), keyed by `requestId`, in settlement order.
- **FR-004** The render path stays: it still counts a settled, not-yet-counted `requestId` seen in
  render. It covers a cached settled entry present **before** the listener existed (spec 310's
  documented "returning to a subject" behaviour) and mocked query results in page tests.
- **FR-005** The strike condition, threshold (3), reset-on-any-other-settlement and
  reset-on-subject-change rules of spec 310 (and, after rebase, spec 313's `notFoundRevokes`) are
  unchanged, and are evaluated by **one** function used by both paths.
- **FR-006** A store reachable through react-redux but lacking the listener middleware MUST make
  the hook throw with a message naming the fix. No silent degradation to render-only counting.
- **FR-007** Every production store that renders a page using the hook (management-web's
  `app/store.ts`) registers the listener middleware, **prepended**.
- **FR-008** No test may depend on real `requestAnimationFrame` timing to expose the defect.

## 3. Out of scope

- Disabling or reconfiguring autobatch in the app store. It would mask the defect in one store
  while leaving the hook wrong under any store with the RTK default, and the hook (a library piece
  in `apps/shared`) does not own `configureStore`.
- Changing what counts as a strike (spec 313 owns the 404 case).
- kiosk-web — it does not use the hook.

## 4. Locked tech choices

React 19 + Redux Toolkit 2.12.0 (`createListenerMiddleware`, `addListener`, endpoint
`matchFulfilled`/`matchRejected`/`select`) + react-redux 9.3.0 — ADR-0075. Vitest + Testing Library.
No new dependency.

## 5. Latency budget impact

**N/A.** management-web's revocation fallback is not on the event→overlay path (constitution §IV).
The listener predicate is O(1) per RTK Query settle action.

## 6. Independent end-to-end test procedure

1. **Deterministic (the evidence):** run
   `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/hooks/useRevocationFallbackCoalesced.test.tsx`.
   Before the fix: the three "dropped" scenarios fail on their outcome assertions **and** their
   precondition assertions (the intermediate state was never rendered) pass — proving the test
   reached the race. After: all green.
2. **Counterfactual on the original flake:** with a temporary (uncommitted) rAF delay of 90 ms in
   `CameraDetailRevocation.test.tsx`'s environment, run the file 8 times on the old hook and 8 on
   the new. #2762 recorded 3/8 failing on the old hook; the new must be 0/8. Quote both counts.
3. **Full suites:** `apps/shared` and `apps/management-web` Vitest, twice (memory: measurement runs
   need repeating), plus `tsc --noEmit`, eslint and `prettier --check` for both.
4. **Live (supervised, optional):** on the Aspire stack, open a camera detail page, revoke the
   operator's `sse.cameras.read` in Keycloak, click Retry three times rapidly; the record leaves
   after the third refusal.
