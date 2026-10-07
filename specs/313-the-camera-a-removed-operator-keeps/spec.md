# Spec 313 — The camera a removed operator keeps

**Issue:** [#2750](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2750)
— *Revocation fallback misses a fab removal: CameraDetailPage answers 404, not 403*.
**Branch:** `fix/2750-revocation-fallback-catches-404` (cut from `origin/develop` @ `4ee7060d`)
**Created:** 2026-10-08
**ADRs:** 0075 (RTK Query), 0089 (error envelope), 0109 (disjoint files), 0139 (red first),
0144 (lane — the decision below is a human's, not the lane's).
**Amends:** spec 310 §0 (reset rule), §2 Scenario Outline row `404`, §8 "Counting 404s".
**Keeps:** spec 029 FR-006 (404 for another fab's camera), spec 030 FR-008 (one refusal sentence),
spec 310 FR-001/003/004/005/006.

**Spec number.** 2026-10-08: `origin/develop` tops out at **312**; no remote branch and no local
worktree (`sse-2337`, `sse-2762c`) carries `specs/313-*`. Re-check before opening the PR (memory:
*spec number: origin/develop isn't enough*).

**Why a new spec, not a resumption of 310.** Spec 310 is merged and has no resumption section; its
§0 records a human decision this issue changes. A different decision on a different issue gets
its own record, and 310 stays readable as what was decided on 2026-10-07.

## 0. The decision (a human's, recorded this session)

> Extend the fallback trigger to also count a **404 that follows a prior 200 for the same camera
> identifier** as a strike, same as a 403. **CameraDetailPage only** — the list pages are
> unaffected.

## 1. The premise, re-checked on this tree

- `src/CameraCatalog/Api/CameraEndpoints.cs:107-112` — `GET /cameras/{id}` answers **404, never
  403**, for a camera in a fab the caller no longer belongs to (spec 029 FR-006). A 403 there means
  only a lost `sse.cameras.read` scope.
- `useRevocationFallback.ts:60` strikes on `isForbidden` only; every other settled response,
  including 404, **resets** the count. An operator removed from the camera's fab therefore keeps
  the record, viewer and controls on screen beside the "could not refresh" banner indefinitely.
- **Cameras are never deleted.** CameraCatalog has no `MapDelete` and no repository removal;
  retire keeps the row (spec 032). So a 404 for an identifier that has already answered 200 can
  only mean lost access, never "does not exist".
- **The "prior 200" signal already exists.** RTK Query's `currentData` is the cache entry for the
  *current* argument only, and RTK 2.12 keeps it on a rejected refetch (spec 310 §1.1;
  `CameraDetailRevocation.test.tsx` pins this against the real store). So
  `currentData !== undefined` ⇔ a request for **this** identifier has succeeded and its result is
  still cached. `data` is not usable for this: it carries a *previous* identifier's record across
  a navigation (`lastResult`, spec 211).
- List pages: unaffected (spec 310 §1.2.1) — a fab-spanning list answers 200 with fewer rows.

## 2. User stories

### US1 (P1) — An operator removed from a camera's fab loses the record after three refusals

As a shift supervisor, when an operator is removed from a fab mid-session, I need the camera
detail page to stop showing that fab's camera after three refused refreshes — exactly as it
already does when the read scope is revoked — so that fab removal is not the one revocation the
fallback misses.

**Why P1 / the whole slice:** it is the issue; one predicate, one hook condition, one page line.

### Acceptance scenarios (Gherkin)

```gherkin
Feature: Fab removal trips the revocation fallback on the camera detail page

  Background:
    Given an operator viewing camera C, whose record loaded successfully (200)

  # Happy path
  Scenario: three consecutive 404 refreshes after a 200 show the refusal surface
    When three consecutive refreshes of camera C are answered 404
    Then the page renders "No such camera" exactly as a first-load refusal renders it
    And the camera's name, RTSP URL and viewer are not shown
    And no Rename, Correct-the-address or Retire control is shown

  Scenario: 403 and 404 are the same kind of strike
    When refreshes of camera C are answered 403, 404, 403
    Then the page renders "No such camera"

  # Below threshold
  Scenario: two 404s keep the stale record
    When two consecutive refreshes of camera C are answered 404
    Then the record stays visible beside the "could not refresh" banner

  # Conflict / reset
  Scenario: a non-refusal between 404s resets the count
    When refreshes of camera C are answered 404, 404, 503, 404
    Then the record stays visible beside the "could not refresh" banner

  Scenario: access restored
    Given three consecutive 404 refreshes of camera C showed the refusal surface
    When the next refresh of camera C succeeds
    Then camera C's fresh record is shown and no banner is shown

  # Bad request — a 404 with no prior success is not a strike
  Scenario: a 404 on the first load is spec 029's ordinary refusal, not a strike
    Given an operator opening camera D, which has never answered 200 in this cache
    When the load of camera D is answered 404
    Then the page renders "No such camera" as it does today
    And the hook records no strike for D

  Scenario: a previous camera's record does not make a new identifier's 404 a strike
    Given camera C loaded successfully
    When the operator navigates to camera D and D's load is answered 404
    Then no strike is recorded for D

  # Scope — list pages unchanged
  Scenario: list pages still never count a 404
    Given an operator viewing the cameras list, which loaded successfully
    When three consecutive refreshes of the list are answered 404
    Then the rows stay visible beside the existing failure banner

  # Auth
  Scenario: an expired session is still the session layer's
    When a refresh of camera C is answered 401 and the silent renewal fails
    Then the session-expired handling runs as today and the 401 resets the count
```

### Edge cases

- **Remount within `keepUnusedDataFor` (60 s).** The cached entry still holds C's record, so
  `currentData` is defined and a cached rejected 404 counts as one strike on the first render back
  — the same, already-documented behaviour the hook has for a cached 403 (hook doc, "Returning to a
  previously-visited subject"). A signal kept in hook state ("have *I* seen a 200?") would miss
  this: the new mount never sees the 200, so the operator would keep the record for the rest of
  that mount. That is why the signal comes from the cache, not the hook.
- **After the refusal surface shows,** `currentData` is still defined (only `record` is masked), so
  later 404s keep counting and a 200 restores as in spec 310.
- **Page-visible effect of the gate is nil today, and that is stated, not hidden.** A 404 with no
  record already renders "No such camera" (`record === undefined`), and any 200 resets the count,
  so counting a first-load 404 would change nothing on screen. The gate is still required: it is
  what makes a strike *mean* "lost access" as the issue defines it, and it is pinned at hook level
  (T002) where it is observable.

## 3. Functional requirements

- **FR-001** On `CameraDetailPage`, a settled **404** for the identifier on screen MUST count as a
  strike **iff** the query's `currentData` is defined (a prior 200 for that identifier is cached).
- **FR-002** A 404 with `currentData` undefined MUST NOT count as a strike; it resets the count,
  as every non-strike settled response does.
- **FR-003** 403 and qualifying 404 strikes accumulate in one count against the existing
  `REVOCATION_STRIKE_THRESHOLD` (3). No second threshold.
- **FR-004** The 404 rule is **opt-in per call site**. A caller that does not opt in (all five list
  pages) MUST behave exactly as today — a 404 resets.
- **FR-005** (keeps spec 310 FR-005) No page reads `error.status`. The 404 status test lives in
  `apps/shared/src/api/problemDetail.ts` as `isNotFound`; the "only after a record" rule lives in
  the hook; the page passes one boolean derived from `currentData`.
- **FR-006** `isForbidden` is NOT widened to include 404 — that would make every list page count
  404s (FR-004) and break `problemDetail.test.ts`'s existing `isForbidden(404) === false`.

**Why FR-008 (spec 030) still holds.** Every path still converges on the same "No such camera"
surface, and a strike can only accrue on an identifier this operator has already been shown. Whether
the fallback trips reveals nothing about any camera the operator could not already see. No ADR.

## 4. Locked tech

React 19 + TypeScript, RTK Query 2.12 (ADR-0075), Vitest + Testing Library. No new dependency, no
store change, no backend change.

## 5. Latency budget

**N/A.** `apps/management-web`'s REST read path is off the event-to-overlay path (constitution §IV).

## 6. Independent end-to-end test procedure (phase 5)

Against the running Aspire stack, signed in to `management-web`:

1. Open a camera's detail page; the record and viewer render.
2. Override `GET …/camera-catalog/cameras/{id}` to answer **404** for every subsequent request
   (Playwright `page.route` or devtools override).
3. Retry once, then twice: the record stays, with the "could not refresh" banner.
4. Retry a third time: "No such camera"; no viewer, no controls.
5. Remove the override, navigate back: the record returns.
6. Open a never-registered GUID directly: "No such camera" on the first load, unchanged.
7. Repeat 2–4 on `/cameras` with 404s: rows stay, banner stays (list page unchanged).

Optional, real revocation: remove the operator from the camera's fab group under `/fabs` in
Keycloak (ADR-0159), force a silent renewal, repeat 3–4 with no override.

## 7. Phase 4a colour

**Behaviour-changing → RED.** Three 404 refreshes after a 200 today keep the record; after this
change they show the refusal surface. No existing assertion is edited.

## 8. Out of scope

The list pages; other detail pages (`WallDetailPage`, `OverlayEditPage`, …); refreshing a passive
tab (spec 310 §1.2.2); consolidating `OverlayEditPage.tsx`'s private `isNotFound` copy (`:201`)
with the new shared one — a refactor, separate issue if wanted; any backend change.
