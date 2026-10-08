# Plan 317 — The tab left alone

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2751

Frontend only. Bounded contexts, domain, messaging, `Shared.Contracts`: none touched. Layers:
`apps/management-web` (bootstrap, store, six call sites, page tests) and `apps/shared` (one
additive test-harness helper and one composition test — **no production change in `apps/shared`**).

## 1. Mechanism

### 1.1 RTK Query's built-in focus refetch, not a hand-rolled listener

| Candidate | Verdict |
|---|---|
| `window.addEventListener('focus', () => refetch())` per page | Re-implements `setupListeners` six times; misses `visibilitychange`; no in-flight dedup except RTK's own; each page owns a lifecycle to get wrong. Rejected. |
| Global `refetchOnFocus: true` on the `createApi` slices | Over-applies (spec D1): refetches editor queries mid-edit and every query in the app; the slices live in `apps/shared` and kiosk-web imports them. Rejected. |
| **`setupListeners(store.dispatch)` once + `refetchOnFocus` on the six hooks** | RTK's documented mechanism; per-subscription option overrides the (false) api default; zero new code paths in the counting hook. **Chosen.** |

How it works in RTK 2.12.0 (read from source, `src/query/core/`):

- `setupListeners.ts` adds `window` listeners for `focus`, `visibilitychange`, `online`, `offline`.
  `focus` → `dispatch(onFocus())`; `visibilitychange` → `onFocus()` if
  `document.visibilityState === 'visible'`, else `onFocusLost()`. It returns an unsubscribe. A
  **module-global** `initialized` flag makes a second call a no-op until the first is unsubscribed —
  this is both why double installation is harmless (spec scenario) and why tests MUST unsubscribe
  in `afterEach` (§4).
- `buildMiddleware/windowEventHandling.ts`: on `onFocus`, for every cache key with
  subscriptions, refetch if any subscription has `refetchOnFocus === true` (or all are `undefined`
  and `config.refetchOnFocus` — false here). A cache key with zero subscriptions is only removed when
  `shouldRefetch` holds, which with per-hook opt-in it never does for an unsubscribed entry. An
  `uninitialized` entry is skipped.
- `buildMiddleware/index.ts` `refetchQuery` = `endpoint.initiate(originalArgs, { subscribe: false,
  forceRefetch: true })` — the **same thunk shape** spec 314's harness drives directly.

### 1.2 Bootstrap — `apps/management-web/src/app/store.ts` + `src/main.tsx` (FR-001, D3)

```ts
// store.ts
import { setupListeners } from '@reduxjs/toolkit/query';
/** Spec 317 (#2751): ... why-comment: refetchOnFocus is inert without this. Returns the unsubscribe. */
export function listenForWindowFocus(): () => void {
  return setupListeners(store.dispatch);
}
```

`main.tsx` calls `listenForWindowFocus()` once, before `createRoot(...).render(...)`. The return
value is deliberately not kept: the listeners live as long as the page. Name is a suggestion; the
engineer keeps ADR-0091 (no abbreviations).

`apiSlices`/`resetApiCaches`/`store.test.ts`'s drift guard are untouched.

### 1.3 Call sites (FR-002, FR-003)

| File | Change |
|---|---|
| `features/cameras/CameraDetailPage.tsx:53` | `useGetCameraQuery({ cameraIdentifier }, { refetchOnFocus: !(editing \|\| renaming \|\| retiring) })` — the three `useState`s already exist at lines 42-44, above the query |
| `features/cameras/CamerasPage.tsx:66` | `useListCamerasQuery(listArgs, { refetchOnFocus: true })` — **not** the `useListStreamsQuery` at :102 |
| `features/audit/AuditPage.tsx:82` | `useSearchAuditQuery(applied, { refetchOnFocus: true })` |
| `features/layouts/LayoutsPage.tsx:58` | `useListLayoutsQuery(undefined, { refetchOnFocus: true })` |
| `features/overlays/OverlaysPage.tsx:56` | `useListOverlaysQuery(undefined, { refetchOnFocus: true })` |
| `features/systemVariables/SystemVariablesPage.tsx:53` | `useListVariablesQuery(variablesArgs, { refetchOnFocus: true })` |

Each gets a one-line why-comment beside the spec-310 comment already there (spec 317: strikes need a
refresh, and a passive tab otherwise never refreshes). `CameraDetailPage`'s comment states D2's
reason (the open dialog's `If-Match` version is a live prop). RTK's `useQuery` re-registers
subscription options when they change (`updateSubscriptionOptions`), so toggling the CameraDetail
option on dialog open/close takes effect on the existing subscription — the dialog test proves it
rather than assuming it.

**Existing page tests mock these hooks** with `(...args) => mock(...args)`. A new second argument
does not break them; no existing assertion inspects the hook's arguments by arity. If one does,
that is a block, not an edit (phase 4a characterisation rule does not apply — these tests are
not this spec's evidence — but an assertion that has to change means behaviour moved; report it).

### 1.4 Stale comment

`CameraDetailRevocation.test.tsx:96-98` says "`CameraDetailPage` polls nothing and never calls
`setupListeners`". After this spec the app calls it (the test's own store still does not, which is
why its first strike still comes from `invalidateTags`). Reword to say the test's store installs no
focus listeners. Comment-only.

## 2. Composition with #2762 / spec 314 — reasoned, then tested

The claim: a focus-triggered refetch is counted exactly once by the existing listener path, with no
change to `useRevocationFallback.ts`. Why it holds, step by step against the post-#2762 hook:

### 2.1 The settlement is recognised

`refetchQuery` dispatches `endpoint.initiate(originalArgs, { forceRefetch: true, subscribe: false })`
on the **endpoint the page passes as `source.endpoint`**, with the cache entry's own
`originalArgs` — which serialise to the same cache key as the page's `source.args` (the page
subscribed with those args; that is how the entry exists). Its `fulfilled`/`rejected` actions are
ordinary `executeQuery` settlements, so `endpoint.matchFulfilled`/`matchRejected` match them. The
pending reducer writes the new `requestId` into the entry (`buildSlice.ts:205-210`), so at
settlement `select(args)(state).requestId === action.meta.requestId` — the hook's FR-002 ownership
predicate passes. The effect reads `entry.error` (403 → strike, else reset) and applies
`applySettlement`, deduplicated by `requestId`.

### 2.2 It does not depend on a render

The listener observes the action inside `dispatch`, after the reducer and before autobatch
notifies anyone (spec 314 §1.1). A focus refetch is no different from a Retry-click refetch in
this respect: the hook never learns *why* a request ran. So a focus refresh whose settlement is
coalesced with the next focus refresh's `pending` is still counted — the exact race #2762 fixed
applies to focus refreshes unchanged, and the fix covers them unchanged.

### 2.3 Overlapping signals cannot double-count

Tab switch fires `focus` **and** `visibilitychange` → two `onFocus` dispatches, synchronously. The
first starts a request (entry → `pending`). The second's thunk hits the condition
`requestState?.status === 'pending' → false` (`buildThunks.ts:947`) and is aborted: it never runs
`queryFn`. RTK Query **does** dispatch that condition-rejection (`dispatchConditionRejection:
true`, `buildThunks.ts:976`), and `endpoint.matchRejected` matches it — but its `requestId` is
**not** the entry's (the reducer leaves a `condition` rejection untouched, `buildSlice.ts:394-395`),
so the hook's ownership predicate rejects it. One request, at most one strike. Same for focus while a Retry is in flight.

### 2.4 Render path and StrictMode

The render path still runs, and still dedups by `requestId`; for a post-mount focus refresh the
listener has already applied the settlement before React renders it, so the render path sees a
counted `requestId` and does nothing. StrictMode's double effect invocation is spec 314's existing
fence and is unaffected (no new effect).

### 2.5 Subject change

`useRevocationFallback` resubscribes its listener per `subject`, and its updater ignores a
settlement whose subject is stale. A focus refresh of the previous subject's entry (only possible
if it still has a subscription, i.e. still mounted) cannot strike the new one. Unchanged.

**What is tested rather than argued:** §4 row 2 runs 2.2 and 2.3 against the spec-314 harness
with notifications held; §4 rows 3-8 run 2.1 end to end per page.

## 3. Entities / invariants

No domain model. Invariants this spec adds:

- **I1** Only the six named subscriptions refetch on focus (FR-002, FR-004).
- **I2** `CameraDetailPage` never refetches `getCamera` on focus while a dialog that quotes
  `record.version` (or shows `record.name`) is open (FR-003).
- **I3** One focus/visibility pair → at most one request per opted-in cache entry (§2.3, RTK's
  condition — not new code).

## 4. Testing (phase 4a colour: **RED**)

Driving focus: real events, real listeners.

```ts
const stopListening = setupListeners(store.dispatch);   // or listenForWindowFocus() for the app store
afterEach(() => stopListening());                        // MANDATORY — module-global `initialized`
window.dispatchEvent(new Event('focus'));
// visibility: Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
//             window.dispatchEvent(new Event('visibilitychange'));   // RTK listens on window, not document
await act(async () => { await Promise.all(store.dispatch(api.util.getRunningQueriesThunk())); });
```

Notes the test-writer must keep:

- **`setupListeners` attaches to `window`, including for `visibilitychange`.** Dispatch that event
  on `window`, not `document`; set `document.visibilityState` explicitly rather than relying on
  jsdom's default (do not assume `pretendToBeVisual`).
- **Unsubscribe in `afterEach`, every file.** Without it the next test's `setupListeners` silently
  no-ops and focus goes to the previous test's store — a red that looks like a missing feature.
- **Own store per page test**, as `CameraDetailRevocation.test.tsx` does (prepended listener
  middleware — spec 314 FR-006 — plus every api slice the page's queries/mutations touch). Do **not**
  import `app/store` in page tests: it is shared module state across the file.
- `vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test')` before the dynamic import of the api
  modules; `fetch` stubbed and routed by URL so unrelated queries (`CamerasPage`'s streams poll,
  any page's secondary query) get a benign 200 and are excluded from the counts. Count only
  requests to the guarded endpoint's path.
- Settlement awaited on a condition (FR-007, ADR-0150) — `getRunningQueriesThunk` or the selector's
  `isLoading`, as `isCameraQueryPending` does. No fixed `await tick()` loops.
- `CamerasPage` mounts a `useListStreamsQuery` poll (`STREAM_POLL_MS`); use `vi.useFakeTimers` only
  if the poll interferes, and then never to wait for settlement.

| # | File | Kind | Expected before fix |
|---|---|---|---|
| 1 | `apps/management-web/src/test/focusRefetch.ts` (new) | helper: `installFocusListeners(store)` (wraps `setupListeners`, registers `afterEach` cleanup), `fireWindowFocus()`, `fireVisible()`, `settleRunningQueries(store, api)`, `countRequests(fetchMock, pathFragment)` | n/a |
| 2 | `apps/shared/src/hooks/useRevocationFallbackFocus.test.tsx` (new), using `revocationHarness.tsx` + a new additive `subscribe(args, { refetchOnFocus })` helper on the harness | composition (§2): three focus refreshes answered 403 with notifications held throughout → refused after flush, with the render-log precondition that no intermediate settlement was rendered; focus+visibility pair → one `queryFn` call; 403·403·200·403·403 via focus → not refused; a subscription without the option is not refetched | **green fences** — RTK and the post-#2762 hook already compose; report them as fences, not as red |
| 3 | `apps/management-web/src/app/focusRefetch.test.ts` (new) | app-store wiring: real `store`, real `camerasApi` subscription with `refetchOnFocus: true` vs one without, `listenForWindowFocus()`, focus → only the opted-in entry refetches; installing twice → one request | **red** — `listenForWindowFocus` is not exported (runtime `TypeError` + `tsc` error; quote both, as spec 313/314 did) |
| 4 | `features/cameras/CameraDetailFocusRefetch.test.tsx` (new) | focus → one request; 3× focus 403 → "No such camera"; 403/404/503/200 outline; dialog-open suspends (×3 dialogs), close → resumes | focus/refusal/outline-true rows **red**; outline-false rows and "no request while dialog open" **green** (vacuously — no focus refetch exists yet); "after close → one request" **red** |
| 5 | `features/cameras/CamerasPageFocusRefetch.test.tsx` (new) | focus → one camera-list request (streams excluded); visibility → one; 3× 403 → refusal surface; 403·403·200·403·403 → not refused | red, except the reset row (green — vacuous) |
| 6 | `features/audit/AuditPageFocusRefetch.test.tsx` (new) | focus → one; 3× 403 → refusal | red |
| 7 | `features/layouts/LayoutsPageFocusRefetch.test.tsx` (new) | focus → one; 3× 403 → refusal | red |
| 8 | `features/overlays/OverlaysPageFocusRefetch.test.tsx` (new) | focus → one; 3× 403 → refusal | red |
| 9 | `features/systemVariables/SystemVariablesPageFocusRefetch.test.tsx` (new) | focus → one; 3× 403 → refusal | red |

**Anti-tautology.** Rows 4-9 count requests at the `fetch` boundary and assert the rendered refusal
surface — neither is the input the test controls. Row 3 asserts the opted-out subscription is
**not** refetched in the same run, so a green cannot come from "everything refetches". A
"vacuously green before" row is acceptable only beside a red row in the same file that proves the
mechanism engaged; the dialog file's "after close → one request" is that row.

**Existing tests:** no assertion or case is edited. The only existing-file change is the
comment in §1.4.

## 5. Constitution / ADR check

- §II value objects — frontend, N/A. §IV latency — N/A (spec §6). §VII dashboards — N/A.
- ADR-0075 — RTK's built-in mechanism. ADR-0113 — D2 keeps `If-Match` meaning "the version the
  operator was shown". ADR-0139 — red first; fences labelled. ADR-0150 — condition waits.
  ADR-0109 — one new file per page test, disjoint. ADR-0036 — no hook change, no global default, no
  new abstraction beyond a test helper.

## 6. Contention

- `feat/316-operator-mfe-shell-and-first-remote` (unmerged, spec 316 / ADR-0168) restructures the
  operator console into a Module Federation shell + remotes. If it merges first, `main.tsx` and the
  cameras pages may move; re-locate the bootstrap call and the two camera call sites, behaviour
  unchanged. Check before phase 4 (T001).
- No open PR touches `useRevocationFallback.ts` (#2776 merged).
