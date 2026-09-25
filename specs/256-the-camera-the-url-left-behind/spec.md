# Spec 256 — the camera the URL left behind

**Issue:** #2522 (feature-level; autonomous lane, ADR-0144)
**Branch:** `fix/2522-cameradetailpage-stale-render` (worktree `D:/Github/sse-2522`)
**Base:** `origin/develop` @ `0d349ce8`
**Closes the row spec 211 left open on purpose:** `specs/211-the-record-a-failed-refresh-keeps/plan.md` §"The rendering contract", last row — *"the pre-existing navigation flash; out of scope"*.
**ADRs:** ADR-0074 (two frontend apps), ADR-0075 (RTK Query), ADR-0037 (phased workflow), ADR-0139 / ADR-0144 (red-first). **No new ADR** — a bug fix inside an existing page, no decision made.
**Latency budget:** **N/A.** `management-web` is not on the event→overlay path (constitution §IV).

---

## Problem

`CameraDetailPage` can render the **previously viewed** camera's record at a **different** camera's URL for the whole in-flight window of that camera's GET.

Mechanism, read from the pinned `@reduxjs/toolkit` **2.12.0** source (`src/query/react/buildHooks.ts`):

- `queryStatePreSelector` (`:1574`): `data = currentState.isSuccess ? currentState.data : lastResult?.data`. `lastResult` is per hook instance and is discarded only when the serialized args are *equal*, so across an identifier change `data` is camera A's record.
- `isLoading` (`:1583`) requires `!hasData`, so it is **false**; `isFetching` (`:1580`) is **true**; `currentData` (`:1598`) is `currentState.data` — **undefined** for B, which has no cache entry.
- On the very first render after the argument change the entry is still uninitialized; `noPendingQueryStateSelector` (`:1461`) rewrites that to `isFetching: true`, so there is **no render in the window where `isFetching` is false**.

The page computes `record = error !== undefined ? currentData : camera`; with `error` undefined that is `camera` — A. It renders A's heading, fab, RTSP URL, the three controls wired to A's identifier and version, and a `CameraViewer` opened on **A's** stream, all under `/cameras/B`.

### Reachability — narrower than the issue implies (verified, not assumed)

The flash needs **the same component instance** to move from one detail URL to another. The route (`router.tsx:46-50`) carries no `key`, so a detail→detail navigation reuses the instance. But **no in-app link goes detail→detail**: the only `Link` to `/cameras/:id` is in `CamerasPage.tsx:85`, a *different* route element, so list→detail always mounts a fresh instance with no `lastResult`. And A's own cache entry survives 60 s after unmount (default `keepUnusedDataFor`; no override in `cameras.api.ts`), during which `currentData` is A's own record and nothing is wrong.

What reaches it today: a **history jump** straight from one detail entry to another, skipping the list entries between them — the browser Back button's long-press history menu, or `history.go(-n)` — to a camera whose cache entry has expired (>60 s since last viewed). Rare, real, and exactly the kind of state that becomes common the moment anything adds a detail→detail link (an audit row, a "neighbouring camera" link). The fix makes the page correct under any composition rather than depending on how it is mounted.

The issue asked for the flash to be *observed* before being treated as real. Phase 4a's real-hook test (plan §Testing, T002) is that observation against the pinned library; phase 5 observes it in a browser.

## User story

### US1 (P1) — A camera's page never shows another camera

An operator moving between two camera detail pages sees either the camera the URL names, or the loading surface — never the previous camera.

**Acceptance scenarios**

```gherkin
Scenario: US1-A — in-flight navigation shows loading, not the previous camera (the fix)
  Given the detail page is showing camera A
  When the same page instance moves to /cameras/B
  And B's GET is still in flight
  Then "Loading…" is shown
  And none of A's name, fab, RTSP URL, viewer, Rename, Correct-the-address or Retire controls is rendered

Scenario: US1-B — B's answer arrives
  Given US1-A's state
  When B's GET succeeds
  Then B's record is shown and nothing of A remains

Scenario: US1-C — a background refresh of the camera already on screen does not blank it (conflict / fence)
  Given the page is showing camera B from its own cache entry
  When B is refetched (e.g. the tag invalidation after a rename)
  Then B's record stays on screen throughout, with no "Loading…"

Scenario: US1-D — a refusal still reads as "No such camera" (bad request / auth fence, spec 211 FR-008)
  Given the page moves to an identifier the API refuses (404, or a camera in another fab)
  Then "No such camera" is shown exactly as before, with no Retry and no alert
  And that stays true while a request for that identifier is in flight with its error still set

Scenario: US1-E — spec 211's refresh-failure rows are untouched
  Given the camera on screen has its own record and its refresh fails
  Then the record, the "could not refresh" alert and Retry are shown exactly as spec 211 specifies
```

Auth: no new surface. The loading surface names no camera, so it cannot distinguish a refused identifier from a permitted one — FR-008's guarantee is not weakened (US1-D).

## Done looks like

1. At `/cameras/B` with B's GET in flight after the instance showed A, the DOM contains **no** text or prop from A's record, and no `CameraViewer` — asserted against the **real** RTK Query hook, not only a mock.
2. "Loading…" is shown instead.
3. A refetch of the camera already on screen does not show "Loading…".
4. All 21 existing tests in `CameraDetailPage.test.tsx` pass **with no assertion and no existing `mockReturnValue` edited**, including spec 211's eight.
5. `pnpm lint`, `pnpm typecheck`, `pnpm typecheck:e2e`, `pnpm test` clean.

## Independent end-to-end test procedure (phase 5)

Against the live stack, management console, two registered cameras A and B:

1. Open A from the list. Back to cameras. Open B. (History: …, A, list, B.)
2. Wait **> 60 s** so A's cache entry expires.
3. DevTools → Network → throttle, or block-then-unblock `GET /cameras/{A}`, so the request stays pending.
4. Long-press Back (or run `history.go(-2)` in the console) to jump from B straight to A.
5. Observe: "Loading…" while A's GET is pending; never B's name or RTSP URL under A's URL. Then A's record.
6. Repeat step 4 **before** the 60 s expire: A renders immediately from cache, no "Loading…" (US1-C analogue).

Before trusting any manual observation, check the AppHost's start time against the commit.

## Out of scope (observed, not fixed)

- **Dialog open-state across a history jump.** `editing` / `renaming` / `retiring` are component state; on a same-instance jump with a dialog open, the dialog re-opens on the new camera once its record lands. Pre-existing, unchanged by this fix, and only reachable by jumping history *while a modal is open*. Not filed as a guess — flag to the orchestrator if a reviewer wants it tracked.
- A route `key` (see plan §Decision) — rejected, not deferred.

## Assumptions

- **A1 (guess, marked):** "Loading…" — the existing surface — is the right thing to show during the window, rather than a skeleton of the new camera. It is what a cold load of B already shows, so the page looks the same however it was reached.
