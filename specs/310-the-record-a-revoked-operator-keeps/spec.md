# Spec 310 — The record a revoked operator keeps

**Issue:** [#2725](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2725)
— *A mid-session 403 (access revoked) leaves a cached record on screen indefinitely*. Labels
`bug`, `agent:ready`; Project #13, status **In Progress** (verified 2026-10-07).
**Branch:** `fix/2725-session-revocation-fallback` (cut from `origin/develop` @ `3f209556`)
**Created:** 2026-10-07 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0075 (RTK Query), 0089 (error envelope), 0109 (disjoint files), 0139 (red first),
0144 (lane), 0150 (wait on a condition). **Amends:** spec 211 §"accepted consequence".
**Keeps:** spec 030 FR-008 / spec 029 FR-006.

**Spec number.** On 2026-10-07 `origin/develop` tops out at **309**; no remote branch carries
`specs/310-*`; no open PR exists. Worktree `sse-2728` (branch
`fix/2728-device-reregistration-after-disable`) is active and has no spec directory yet — it is
the live collision risk. Re-check immediately before opening the PR (memory: *spec number:
origin/develop isn't enough*).

## 0. The decision (a human's, recorded on the issue)

> A fixed threshold of N (3) consecutive 403s, counted per-camera-identifier, triggers the refusal
> surface. The counter resets on any non-403 response.

This spec decides nothing beyond that. It names where the count lives, what "identifier" means on
the five list pages, and what "the refusal surface" is on a page that has none today.

## 1. The premise, re-checked on this tree

### 1.1 Confirmed

- `CameraDetailPage.tsx:53` — `record = error !== undefined ? currentData : camera`. RTK Query
  2.12 keeps `data`/`currentData` on a rejected refetch, so a same-identifier refetch that fails
  for *any* reason keeps the record, the `CameraViewer`, and Rename / Correct-address / Retire on
  screen beside a `RetryBanner`. Spec 211 introduced this and recorded the mid-session 403 as an
  *accepted consequence* (`specs/211-*/spec.md:144`). This spec withdraws that acceptance for the
  403 case only.
- The five list pages (`CamerasPage:58`, `AuditPage:77`, `LayoutsPage:72`, `OverlaysPage:70`,
  `SystemVariablesPage:52`) derive their rows from `data` and render `RetryBanner` beside them on
  `error`. Same exposure: stale rows (and, on Layouts / Overlays / SystemVariables, their row
  mutation controls) stay visible.
- **One pattern, six separate copies.** Each page calls its own RTK hook and applies the same
  `error !== undefined && <RetryBanner …/>` shape inline. There is no shared stale-record hook.
  All eight API slices share one base query (`apps/shared/src/api/gateway.ts:gatewayBaseQuery`),
  and `apps/shared/src/api/problemDetail.ts` is documented as *"the single place the frontend
  understands the server error envelope"* — it already inspects `status` for `isConflict` (409),
  below every page. That is the layer the 403 test belongs in.

### 1.2 Two limits of the decision, recorded, not resolved here

1. **Fab revocation on the camera detail endpoint answers 404, not 403.**
   `CameraEndpoints.cs:107-112`: *"404 for another fab's camera, never 403"* (spec 029 FR-006). A
   403 on `GET /cameras/{id}` arises when the operator loses the `sse.cameras.read` scope
   (`RequireAuthorization(Scope.Sse.Cameras.Read)`, `:105`), not when they are removed from the
   camera's fab. Under the decision a 404 is a non-403 and **resets** the count, so an operator
   removed from a fab keeps the stale camera exactly as today. Cameras are never deleted (retire
   keeps the row), so a 404 after a 200 for the same identifier always means lost access — but
   counting it is a change to the decision, which the lane may not make (ADR-0144). The 403 test is
   a single function (`isForbidden`) so widening it later is one line. **Recommend a follow-up
   issue for a human decision.** The list endpoints are not affected: a fab-spanning list answers
   200 with fewer rows, which replaces the stale data on the next refresh; an explicit `fabId` the
   caller no longer holds answers 403 (`FabAuthorizationExceptionHandler`) and is counted.
2. **A strike needs a refresh, and a passive tab never refreshes.** None of the six queries polls
   or refetches on focus (`setupListeners` is not called anywhere). Refreshes come from Retry,
   tag invalidation after a mutation, or a remount after the 60 s `keepUnusedDataFor`. A tab left
   untouched never accumulates strikes. Closing that is a polling / refetch-on-focus decision
   with server-load cost — out of scope. **Recommend a follow-up issue.**

Also assumed (not changed): revocation becomes visible only once the access token is renewed,
since scopes ride in the JWT. That is the session layer's existing behaviour.

## 2. User stories

### US1 (P1) — Three consecutive refusals take the record off screen

As a shift supervisor in a shared control room, when an operator's access to a record is revoked
mid-session, I need the record and its controls to leave the screen after three refused refreshes,
so that a revoked operator does not keep a camera feed and its controls for as long as the tab is
open.

**Why P1 / the whole slice:** it is the issue. Six pages, one hook; each page's wiring is one
line, and each is observable on its own (three 403s on one `GET`). No P2 is deferred.

### Acceptance scenarios (Gherkin)

```gherkin
Feature: Revocation fallback after consecutive refusals

  Background:
    Given an operator viewing camera C, whose record loaded successfully

  # Happy path
  Scenario: three consecutive 403 refreshes show the refusal surface
    When three consecutive refreshes of camera C are answered 403
    Then the page renders "No such camera" exactly as a first-load refusal renders it
    And the camera's name, RTSP URL and viewer are not shown
    And no Rename, Correct-the-address or Retire control is shown

  Scenario: the same holds on a list page
    Given an operator viewing the cameras list, which loaded successfully
    When three consecutive refreshes of the same list query are answered 403
    Then no stale row is shown
    And the page renders as a list whose first load failed: the "Could not load cameras." banner and no rows

  # Below threshold
  Scenario: two refusals keep the stale record
    When two consecutive refreshes of camera C are answered 403
    Then the record stays visible beside the "could not refresh" banner

  # Conflict / reset cases
  Scenario: a non-403 between refusals resets the count
    When refreshes of camera C are answered 403, 403, 503, 403
    Then the record stays visible beside the "could not refresh" banner

  Scenario: a success after the refusal surface restores the record
    Given three consecutive 403 refreshes of camera C showed the refusal surface
    When the next refresh of camera C succeeds
    Then camera C's fresh record is shown and no banner is shown

  Scenario: refusals are counted per identifier
    Given two 403 refreshes of camera C
    When the operator opens camera D and one refresh of D is answered 403
    Then camera D is not shown the refusal surface on account of C's refusals

  # Bad request / non-403 failures — unchanged
  Scenario Outline: other failures never trigger the fallback
    When three consecutive refreshes of camera C are answered <status>
    Then the record stays visible beside the "could not refresh" banner
    Examples:
      | status      |
      | 404         |
      | 503         |
      | 400         |
      | FETCH_ERROR |

  # Auth
  Scenario: an expired session is the session layer's, not this mechanism's
    When a refresh of camera C is answered 401 and the silent renewal fails
    Then the session-expired handling runs as today
    And the 401 counts as a non-403 response
```

### Edge cases

- **The same response seen twice.** A re-render that observes the same settled request must not
  count it again (strikes are counted per RTK `requestId`).
- **A request in flight** is not a response; only settled requests (`isFetching === false`) count.
- **A test double without `requestId`** never counts — existing page tests that mock the query
  hooks without it render exactly as before.
- **Remount within 60 s.** The count lives with the mounted page; a remount while RTK still caches
  the stale entry starts at zero, and that remount does not refetch (RTK's thunk condition skips an
  entry with a `fulfilledTimeStamp`). Exposure on that path is bounded by `keepUnusedDataFor`
  (60 s), after which a remount refetches and a 403 with no data in hand renders the refusal
  directly. Accepted; a store-level counter would need clearing on subject change (spec 303) and
  is not needed to meet the decision.

## 3. Functional requirements

- **FR-001** A page MUST render its refusal surface once **3 consecutive settled responses** to
  the query for the record on screen were **403**.
- **FR-002** Any settled non-403 response (success, any other status, a network failure) MUST
  reset the count to zero.
- **FR-003** The count MUST be kept per subject: per camera identifier on the detail page, per
  query argument set on a list page. A change of subject MUST start from zero.
- **FR-004** The refusal surface MUST be the page's **existing** no-record-with-error render,
  byte-for-byte: "No such camera" on the detail page; the existing failure banner with no rows on
  each list page. No new sentence is introduced.
- **FR-005** No page component may read an error's `status`. The 403 test lives in
  `apps/shared/src/api/problemDetail.ts`; the count lives in a shared hook; a page receives a
  boolean only. (Keeps spec 030 FR-008 and its *"no error code is inspected"* at the page.)
- **FR-006** The threshold MUST be one named constant (`3`), not repeated per page.

**Why FR-005 keeps FR-008 intact.** FR-008 forbids a render that distinguishes *may not see*
from *does not exist*. Here every path converges on the same existing surface, and the only status
distinguished (403) is a scope-level refusal on these endpoints — per-resource fab refusals are
404 by design (spec 029 FR-006) and are never counted. Nothing per-resource can be enumerated
from whether the fallback trips. No ADR is needed.

**Implementation note (FR-004), phase 6 finding S1.** The three list pages initially wired
`refused ? undefined : data` with no regard for `error`, so once an operator changed a filter —
resetting the fallback's strikes to zero for the new subject (FR-003) — a 403 on the new
argument set's own first request rendered the *previous* argument set's rows via RTK Query's
`lastResult` carryover, instead of nothing. Fixed by applying `CameraDetailPage`'s own
`error !== undefined ? currentData : data` guard (spec 211, §1.1 above) to all three. This is a
bug-fix application of a pattern already decided and already built elsewhere in this tree, not a
change to §0's N=3 / per-identifier / reset-on-non-403 decision.

## 4. Locked tech

React 19 + TypeScript, RTK Query 2.12 (ADR-0075), Vitest + Testing Library, existing
`RetryBanner` composite. No new dependency, no store change, no backend change.

## 5. Latency budget

**N/A.** `apps/management-web` and the REST read path are off the event-to-overlay path
(constitution §IV).

## 6. Independent end-to-end test procedure (phase 5)

Against the running Aspire stack, signed in to `management-web` as an operator:

1. Open a camera's detail page; the record and viewer render.
2. In the browser (Playwright `page.route` or devtools request override), answer
   `GET …/camera-catalog/cameras/{id}` with `403` for every subsequent request.
3. Press Retry once, then twice: the record stays, with the "could not refresh" banner.
4. Press Retry a third time: the page shows "No such camera"; no viewer, no controls.
5. Remove the override, navigate to the camera again: the record returns.
6. Repeat 2-4 on `/cameras` (list query) and confirm rows disappear and the banner stays.

Optional, real revocation: remove the role granting `sse.cameras.read` from the operator in
Keycloak, force a silent renewal, and repeat steps 3-4 without an override.

## 7. Phase 4a colour

**Behaviour-changing → RED.** After three 403s a page that today shows the stale record must show
the refusal surface. Existing page tests are not edited; new tests are added beside them.

## 8. Out of scope

Counting 404s (§1.2.1), automatic refresh of a passive tab (§1.2.2), the kiosk app, detail pages
not named by the issue (`WallDetailPage`, `OverlayEditPage`, `RulesPage`, `WallsPage`), and any
backend change.
