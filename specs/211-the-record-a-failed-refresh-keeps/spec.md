# Spec 211 — The record a failed refresh keeps

**Issue:** #2432 — *A transient refetch failure after a successful mutation makes CameraDetailPage claim the camera does not exist*
**Branch:** `2432-transient-refetch-camera-exists`
**Base:** `origin/develop` @ `1cd84443`
**Phase-4a colour:** **RED** (behaviour-changing). The page's rendered output changes for a real, reachable state — a camera that is on screen after a failed refresh stays on screen instead of being replaced by "No such camera". A test that arrives green is a phase-4 failure, not a shortcut (ADR-0139, constitution §Testing).
**ADRs:** **ADR-0075** (Redux Toolkit + RTK Query — the hook state this whole defect lives in), **ADR-0074** (two apps; `management-web` only), ADR-0077 (Radix + Tailwind primitives), ADR-0078 (Tailwind design tokens — the banner's classes are copied, not invented), ADR-0106 (the YARP gateway whose 5xx is the transient failure), ADR-0108 (Playwright e2e), ADR-0109 (`[P]` markers), ADR-0036 (smallest possible change), ADR-0037 (phases), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane).
**Prior specs this amends:** spec 030 (`specs/030-camera-detail-view`) FR-008 — the requirement the current `||` was written to satisfy. This spec keeps FR-008 intact and says exactly how.
**Constitution:** §Testing (red for new behaviour), §IV (latency budget — **N/A**, see below).

**No new ADR is required.** Nothing here decides architecture. The page already renders an RTK Query result; the fix reads a second field of the same result that RTK Query has always exposed. No new dependency, no new component, no new pattern.

---

## Problem

Every line number, type and predicate below was **re-read in the working tree at HEAD `1cd84443`**. The line numbers the issue quotes are still exact.

### The page replaces itself on any error, including one it has data for

`apps/management-web/src/features/cameras/CameraDetailPage.tsx`:

```tsx
:42    const { data: camera, isLoading, error } = useGetCameraQuery({ cameraIdentifier });
:44    if (isLoading) { return <Surface>Loading…</Surface>; }
...
:48-56 // FR-008. Every refusal renders the same sentence, and that is the whole
       // requirement rather than an omission. … there is nothing to branch on
       // here and no error code is inspected.
:57    if (error !== undefined || camera === undefined) {
:58-65   return (<Surface><h1>No such camera</h1><p>Nothing here matches that identifier. …</p></Surface>);
:66    }
```

The `||` is the defect. It collapses two states that differ in the one thing that matters — **whether the page has the requested camera's record in hand** — into a single sentence that denies the record exists.

### Every successful edit on this page triggers the refetch that can hit it

`apps/shared/src/api/cameras.api.ts` (all confirmed; the issue's line numbers are exact):

- `:191` — `getCamera` … `providesTags: (_result, _error, { cameraIdentifier }) => [{ type: 'Camera', id: cameraIdentifier }]`
- `:207-210` — `changeCameraAddress` invalidates `{ type: 'Camera', id: cameraIdentifier }` **and** `id: 'LIST'`
- `:226-229` — `renameCamera`, the same two
- `:246-249` — `retireCamera`, the same two

`retireCamera`'s own comment states the mechanism this defect rides on: *"Invalidation **refetches** for a mounted subscriber rather than evicting."* The detail page is that mounted subscriber. So each of the three controls it offers — Rename (`:85`), Correct the address (`:92`), Retire camera (`:97`) — guarantees a `GET /cameras/{identifier}` immediately after a write the operator just watched succeed.

### A 5xx from the gateway reaches the hook as `error`

`apps/shared/src/api/gateway.ts:119-135` retries **only** `401`, and only by renewing the session once. Every other status — the `503` in the issue's scenario included — is returned unchanged. There is no resilience layer between the gateway and the hook that would absorb a single transient failure.

### RTK Query keeps the data, and this is provable rather than assumed

`@reduxjs/toolkit` is pinned at **2.12.0** in all three `package.json` files, and `camerasApi` (`cameras.api.ts:168-172`) declares **no** `keepUnusedDataFor`, **no** `refetchOnMountOrArgChange`, and no per-endpoint overrides — so stock 2.12.0 behaviour applies unmodified. Read from the pinned dist rather than from documentation or memory:

`dist/query/rtk-query.legacy-esm.js:1504-1520` — `queryThunk.rejected`:

```js
substate.status = STATUS_REJECTED;
substate.error = payload != null ? payload : error;
```

`substate.data` is **not touched**. The cached record survives a failed refetch. That is the issue's central claim, confirmed at the source.

`dist/query/rtk-query.legacy-esm.js:1397` — `writeFulfilledCacheEntry`:

```js
delete substate.error;
```

A **successful** refetch removes the error by itself. This is load-bearing below: it is why the banner needs no dismiss control.

### The failure, restated

1. Operator renames a camera on `/cameras/{A}`. The `PATCH` succeeds.
2. The invalidation refetches `GET /cameras/{A}`. It meets one `503`.
3. `camera` is still A's record — correct, merely a few hundred milliseconds stale.
4. `error` is set, so the `||` fires.
5. The page renders **"No such camera — nothing here matches that identifier."**

The page states the opposite of both facts it holds: the rename worked, and the camera exists. There is no Retry, and nothing recovers it but navigating away and back.

---

## Verifying the fix direction — and the one thing that breaks it

The issue proposes splitting the `||` into `camera === undefined` (keep today's surface) and `camera !== undefined && error !== undefined` (keep the record, add a banner). **The first half is right. The second half, taken literally, is unsafe**, and this is the finding that narrows the fix.

### `data` is not "the data for this identifier"

`dist/query/react/rtk-query-react.legacy-esm.js:164-192`, `queryStatePreSelector`:

```js
if (lastResult?.endpointName && currentState.isUninitialized) {
  … if (serializeQueryArgs(lastResult.originalArgs) === serializeQueryArgs(queryArgs)) lastResult = void 0;
}
let data = currentState.isSuccess ? currentState.data : lastResult?.data;
if (data === void 0) data = currentState.data;
…
return { …currentState, data, currentData: currentState.data, isFetching, isLoading, isSuccess };
```

Two things follow, and both matter here:

- **`data` can belong to a *previous* hook argument.** `lastResult` is discarded only when the serialized args are *equal*. Across an identifier change it is kept, so when the new identifier's entry is not `isSuccess`, `data` falls through to the **previous camera's record**.
- **`currentData` is `currentState.data`** — the cache entry for the argument being asked for right now, and nothing else. `queryThunk.rejected` leaves it in place (above), so it is exactly "the requested camera's record, in hand" — the predicate the split needs.

`apps/management-web/src/app/router.tsx:46-50` declares the route with **no `key`**:

```tsx
{ path: 'cameras/:cameraIdentifier', element: <CameraDetailPage />, errorElement: <SurfaceCrash /> }
```

so a navigation from `/cameras/A` to `/cameras/B` reconciles the same component instance and `lastResult` survives it.

### What that does to the issue's literal condition

Operator is on camera A, then opens camera B in another fab. The API refuses B exactly as it refuses a camera that never existed (spec 029 FR-006). At that moment `error` is set, `currentData` is `undefined`, and `data` is **camera A's record**. The issue's `camera !== undefined && error !== undefined` is satisfied, and the page would render A's heading, A's fab, A's RTSP URL and a `CameraViewer` keyed to A — under the URL `/cameras/B`, over a banner saying the refresh failed.

That is worse than the bug being fixed. It shows the wrong camera, and it turns a refusal into something that reads like a success.

**So the gate is `currentData`, not `data`.** It is a one-word difference and it is the whole correctness of the change.

### The pre-existing flash, checked and left alone

The same `queryStatePreSelector` arithmetic says something about **today's** code too. During the in-flight window of a navigation A→B:

```js
isLoading = (!lastResult || lastResult.isLoading || lastResult.isUninitialized) && !hasData && isFetching;
```

`lastResult` is A's successful result and `hasData` is true (A's data), so `isLoading` is **false** — and with `error` not yet set, the current `if` falls through and the page renders **A at B's URL** until B resolves. This is a distinct, pre-existing defect. It is **not** fixed here (a bug fix changes the bug, nothing else) and it is **not** made worse: gating on `currentData` touches only the error branch. It is recorded in §Out of scope with a follow-up issue.

Note that nothing in this spec's tests depends on that derivation being right. The scenarios below pin the states directly.

---

## The error code is never inspected, and that is deliberate

`CameraDetailPage.tsx:48-56` states FR-008's guarantee as *"there is nothing to branch on here and no error code is inspected."* This spec keeps that literally true.

The new gate discriminates on **whether the requested identifier's record is in hand** (`currentData`), never on the error's `status`. A `503`, a `404` and a `403` over data-in-hand all render the same thing, and all three over no-data render the same thing. Nothing the page shows varies with the refusal's code, so the enumeration oracle FR-008 closes stays closed.

**The accepted consequence, stated rather than discovered:** a `403` on a *same-identifier* refetch — permission revoked mid-session — leaves the record on screen with a staleness banner. That is not a fab-boundary leak. The API already delivered this record to this browser once; an enumeration attack needs an answer that *distinguishes*, and this operator is reading something they were legitimately given, not probing an identifier they were not. The alternative — inspecting the status to decide whether to evict — is precisely the branch FR-008 forbids.

---

## Not dismissible — mirroring the sibling rather than the issue text

The issue asks for a *dismissible* banner. `CamerasPage.tsx:123-133` — the pattern the issue names as the thing to mirror — is **not** dismissible:

```tsx
{error !== undefined && (
  <div role="alert" className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault">
    Could not load cameras.{' '}
    <button type="button" className="underline" onClick={() => void refetch()}>Retry</button>
  </div>
)}
```

The identical shape appears at `AuditPage.tsx:165`, `LayoutsPage.tsx:112`, `OverlaysPage.tsx:110` and `SystemVariablesPage.tsx:98`. None dismisses.

A dismiss control would be new state that must be reset when the *next* refetch fails, and it would let an operator hide a staleness warning while still reading stale data. It is also unnecessary: `delete substate.error` on a fulfilled refetch (above) means a successful Retry clears the banner on its own. **Mirror the sibling: no dismiss.**

---

## Blessed — checked, and deliberately left alone

`grep -rn "error !== undefined" apps/management-web/src apps/kiosk-web/src apps/shared/src --include=*.tsx` returns 16 sites. Every one was read:

- `CamerasPage.tsx:123`, `AuditPage.tsx:165`, `LayoutsPage.tsx:112`, `OverlaysPage.tsx:110`, `SystemVariablesPage.tsx:98`, `RetireCameraDialog.tsx:72`, `RuleDialog.tsx:179`, `DryRunPanel.tsx:65` — all **banner-shaped** (`{error !== undefined && …}`), rendered *beside* content rather than instead of it. Already correct. Untouched.
- `FormField.tsx:22`, `OverlayGeometryFields.tsx:223,227` — field-level validation messages. Unrelated.
- `App.tsx:55` (management), `App.tsx:70` (kiosk) — OIDC `auth.error`, not RTK Query. Unrelated.
- `PickerPage.tsx:36` — passes `hasError` down as a prop; no data is hidden. Unrelated.
- `CellPage.tsx:320` (kiosk) — `error !== undefined || data === undefined || published === undefined || tiles.length === 0`. Same `||` *shape*, different meaning: a wall cell with no tiles genuinely has nothing to render, there is no operator at a kiosk to press Retry, and the kiosk has its own reconciliation path. **Out of scope, no follow-up.**

**`CameraDetailPage.tsx:57` is the only early-return-that-replaces-the-page error guard in `management-web`.** The fix has exactly one site.

No shared error-banner component exists to reuse — `apps/shared/src/ui/primitives/` holds `Button`, `ConfirmDialog`, `Dialog`, `Input`, `Tooltip` and nothing else, and the `role="alert"` hits under `apps/shared/src/ui` are all form-field or boundary contexts. Extracting one now would touch five already-correct files; a sixth inline copy is the smaller change (ADR-0036). The duplication is recorded in §Out of scope.

---

## Locked technical choices

- **React + TypeScript + Vite, `management-web` only** (ADR-0074). `kiosk-web` and `apps/shared` are untouched.
- **RTK Query 2.12.0 hook state** (ADR-0075). `currentData` is read from the existing `useGetCameraQuery` result; no endpoint config, no `selectFromResult`, no new query.
- **Tailwind tokens** (ADR-0078). The banner's classes are copied verbatim from `CamerasPage.tsx:126-128`; no new token, no new class.
- **No new component, no new dependency, no change under `apps/shared/`.**
- **Unit:** vitest + React Testing Library, in the existing `CameraDetailPage.test.tsx` (`useGetCameraQuery` is already mocked there).
- **E2E:** Playwright (ADR-0108), in the existing `e2e/camera-detail.spec.ts`, with one injected `503` via `page.route` — the fault-injection pattern `system-variables.spec.ts:121` and `overlays.spec.ts:379` already use.

## Latency-budget impact

**N/A.** This is the management console. Nothing here is on the `event arrival → overlay rendered ≤ 800 ms` path (constitution §IV); no leg is touched, and §VII's dashboard obligation does not attach.

## Assumptions

- **A1 (marked guess).** The operator-visible wording of the banner is not specified by any existing requirement. The scenarios below fix it as *"Could not refresh this camera — what you see may be out of date."* plus a *Retry* control, chosen to (a) name the refresh rather than the record, (b) warn about staleness, and (c) use none of the access vocabulary the existing test "Says nothing about access, ever" forbids. Changing the sentence is a review comment, not a redesign.
- **A2.** `page.route` interception of the detail `GET` is sufficient to reproduce the failure end to end without a real gateway fault. Held by the two existing specs that use the same technique.

---

## User stories

### US1 (P1) — A failed refresh does not deny the camera

**As** an operator who has just renamed, re-addressed or retired a camera,
**I want** the page to keep showing the camera when the refresh after my edit fails,
**so that** I can see my edit stood and try again, instead of being told the camera does not exist.

This is the whole slice. It is independently shippable, independently observable (one `503` on one `GET`), and there is no P2 behind it — the FR-008 surface is unchanged by design, not deferred.

---

## Acceptance scenarios

All scenarios are stated after the `isLoading` guard, which is unchanged.

### US1-A — the filed defect (happy path of the fix)

```gherkin
Given an operator is viewing camera A on /cameras/A
  And the page holds camera A's record for the identifier A
When a refetch of camera A fails
Then the page still shows camera A's name, fab, RTSP URL and status
  And an alert is shown above the record reading
      "Could not refresh this camera — what you see may be out of date."
  And that alert offers a "Retry" control
  And the page does not show "No such camera"
```

### US1-B — the retry clears the alert

```gherkin
Given the alert from US1-A is shown
When the operator presses "Retry"
Then the query is refetched
  And on success the alert is gone and the record is the refreshed one
```

*(The clearing is RTK Query's own `delete substate.error` on fulfilment — the unit test asserts the refetch is requested; the clearing is observed in the e2e and in phase 5.)*

### US1-C — conflict case: no data for this identifier, stale data for another

```gherkin
Given an operator was viewing camera A
When they navigate to /cameras/B and the request for B is refused
  And the page holds no record for identifier B
Then the page shows "No such camera — nothing here matches that identifier."
  And it does not show camera A's name, fab or RTSP URL
  And it offers no Retry
```

**This is the scenario that the issue's literal fix direction would have broken.** It is the reason the gate reads `currentData` and not `data`.

### US1-D — bad request / never loaded: unchanged FR-008 surface

```gherkin
Given no record has ever been loaded for the identifier in the URL
When the request for it fails, for any reason and with any status
Then the page shows "No such camera — nothing here matches that identifier."
  And it offers no Retry
  And a camera in another fab renders byte-identically to one that does not exist
```

### US1-E — auth: a refusal says nothing about access

```gherkin
Given the identifier names a camera in a fab this operator may not see
When the page renders any of its states — record, alert, or "No such camera"
Then nothing rendered matches /access|permission|not yours|another fab/i
  And nothing rendered varies with the refusal's HTTP status
```

### US1-F — the ordinary path is untouched

```gherkin
Given a refetch of the camera on screen succeeds
Then no alert is shown
  And the record is the refreshed one
```

### US1-G — a retired camera keeps its retired behaviour under a failed refresh

```gherkin
Given the operator is viewing a retired camera
When a refetch of it fails
Then the alert is shown
  And the retired notice is still shown
  And no viewer, Rename, Correct-the-address or Retire control appears
```

---

## Independent end-to-end test procedure

Against the live Aspire stack, without reading any test file:

1. `dotnet run --project src/AppHost` and sign in to `management-web` as an operator.
2. Register a camera, open it from the list; confirm the URL is `/cameras/{guid}` and the heading is the camera's name.
3. In devtools, add a request-blocking rule that fails only the **`GET`** of `**/camera-catalog/cameras/{guid}`, leaving the `PATCH` through. (Devtools blocking is URL-based and will catch both verbs on the same path; if so, use the Playwright spec from this feature, which discriminates by method, or block after the `PATCH` has been sent.)
4. Press **Rename**, give a new name, Save.
5. Observe: the **camera stays on screen**, and a red alert appears above the record reading *"Could not refresh this camera — what you see may be out of date."* with a **Retry** link. Before the fix, the page reads "No such camera".
6. Remove the block. Press **Retry**. The alert disappears and the record shows the new name.
7. Navigate to `/cameras/00000000-0000-0000-0000-000000000000` directly. The page reads **"No such camera — nothing here matches that identifier."** with no Retry and no trace of the camera from step 2.

Step 5 is the fix; step 7 is FR-008 still holding.

---

## Out of scope

- **The navigation flash** — the page renders the previous camera briefly at the new camera's URL while the new one loads, because `isLoading` is false whenever `data` is carried over from a previous argument. Pre-existing, distinct, and untouched here. **Filed as #2522.**
- **Extracting a shared error-banner component.** Six inline copies will exist after this change (`CamerasPage`, `AuditPage`, `LayoutsPage`, `OverlaysPage`, `SystemVariablesPage`, and this page). Extraction touches five already-correct files and is a refactor, not this fix. **Filed as #2523.**
- `CellPage.tsx:320` and every other site listed under §Blessed.
- Any change to `apps/shared/`, to `cameras.api.ts`, or to the gateway's retry policy.
- Any redesign of the page's loading state, or of the "No such camera" surface itself.

## File contention

| File | Owner | Phase |
|---|---|---|
| `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` | `test-writer` | 4a |
| `e2e/camera-detail.spec.ts` | `test-writer` | 4a |
| `apps/management-web/src/features/cameras/CameraDetailPage.tsx` | `frontend-engineer` | 4b |

No other file is edited. No backend file, no `apps/shared/` file, no `cameras.api.ts`.
