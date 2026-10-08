# Plan 314 — The strike a frame swallowed

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2762

Frontend only. Bounded contexts, domain, messaging: none touched. Layering: `apps/shared` (the
hook, a reusable library piece) and `apps/management-web` (the six call sites, the app store, two
own-store tests).

## 1. Mechanism

### 1.1 Why the listener middleware, and why nothing lighter works

| Candidate | Sees every settlement? | Verdict |
|---|---|---|
| `store.subscribe` from the hook / `useSyncExternalStore` | No — `autoBatchEnhancer` **replaces** `subscribe`; every subscriber is notified at flush with flush-time state | Same defect |
| `endpoint.select(args)(store.getState())` inside a subscribe callback | No — runs at the same flush | Same defect |
| Infer the missed settlement from the next render (retained `error`) | No — fails when pending + settle + next pending share one window (spec §0) | Lossy |
| Disable autobatch (`enhancers: getDefault({ autoBatch: false })`) | In that store only | Masks; hook stays wrong under the RTK default; hook does not own the store |
| **Listener middleware**, `dispatch(addListener(...))` from the hook | **Yes** — the predicate runs inside `dispatch`, after the reducer, before any notification (`listenerMiddleware/index.ts:528-545`; the effect is invoked synchronously before its first `await`) | **Chosen** |

`addListener` from a component's `useEffect`, returning the unsubscribe as cleanup, is RTK's own
documented pattern for component-scoped listeners. It needs a listener middleware instance in the
store — any instance; `addListener` is a global action creator.

### 1.2 Hook changes — `apps/shared/src/hooks/useRevocationFallback.ts`

**Signature.** A required third parameter naming the query, so the listener can recognise its own
settlements:

```ts
export interface RevocationSource<Args> {
  /** The RTK Query endpoint the page's query hook reads. */
  endpoint: {
    select: (args: Args) => (state: never) => { requestId?: string; error?: unknown };
    matchFulfilled: (action: unknown) => action is { meta: { requestId: string } };
    matchRejected: (action: unknown) => action is { meta: { requestId: string } };
  };
  args: Args;
}
export function useRevocationFallback<Args>(
  subject: string,
  query: RevocationQueryState,
  source: RevocationSource<Args>,
): boolean;
```

The structural type above is a sketch — the engineer narrows it to whatever RTK's
`ApiEndpointQuery` actually satisfies (e.g. `Pick<ApiEndpointQuery<...>, 'select' | 'matchFulfilled' |
'matchRejected'>`) without `any` leaking to call sites. **Required, not optional**: an optional
source would let a page omit it and silently keep the race; TypeScript is the enforcement.

`RevocationQueryState` is **unchanged** — this is what keeps #2750's additive `notFoundRevokes`
field composable (§6).

**One apply function.** Extract the transition both paths use:

```ts
function applySettlement(state: RevocationState, requestId: string, error: unknown): RevocationState {
  if (requestId === state.counted) return state;           // FR-003
  return { subject: state.subject, counted: requestId, strikes: isForbidden(error) ? state.strikes + 1 : 0 };
}
```

The render path (existing lines 55-62) becomes a call to `applySettlement`. After #2750, the
predicate inside it is the single place spec 313's widening lands (§6).

**Listener path.**

```ts
const dispatch = useDispatch();
const [state, setState] = useState(...);           // as today
useEffect(() => {
  const select = source.endpoint.select(source.args);   // captured per subject — see below
  const unsubscribe: unknown = dispatch(addListener({
    predicate: (action, currentState) =>
      (source.endpoint.matchFulfilled(action) || source.endpoint.matchRejected(action)) &&
      select(currentState).requestId === action.meta.requestId,                       // FR-002
    effect: (action, listenerApi) => {
      const entry = select(listenerApi.getState());
      setState((s) => (s.subject === subject ? applySettlement(s, action.meta.requestId, entry.error) : s));
    },
  }));
  if (typeof unsubscribe !== 'function') {
    throw new Error('useRevocationFallback needs the Redux Toolkit listener middleware in the store ...'); // FR-006
  }
  return unsubscribe;
}, [dispatch, source.endpoint, subject]);
```

Points the engineer must keep:

- **Error read from the post-reducer entry**, not the action — it is exactly the `error` the
  page's `useQuery` result would expose (RTK writes `payload ?? error`), so `isForbidden` sees the
  same shape on both paths. Fulfilled → entry has no `error` → reset.
- **`args` captured at subscription, keyed by `subject`.** At all six call sites `subject` changes
  iff the args change (`JSON.stringify(args)` ×3, `cameraIdentifier` ⇔ `{ cameraIdentifier }`,
  constant subject ⇔ `undefined` args ×2). State this contract in the docblock; omitting `args`
  from the deps needs an `eslint-disable-next-line react-hooks/exhaustive-deps` with that reason.
  Do **not** route args through a ref updated in a layout effect: between commit and passive
  cleanup the old listener would read the new args.
- **The `s.subject === subject` guard** discards a late settlement from the previous subject's
  listener (cleanup runs after the render that reset the state).
- **Functional `setState` from inside `dispatch`** is what makes coalescing harmless: React queues
  each updater in order; none is lost. The updater is pure, so StrictMode's double invocation is
  safe. When React re-renders, `useQuery`'s `useSyncExternalStore` reads `store.getState()`
  afresh, so `refused` and the page's `error` still land in the same commit (spec 310's claim
  holds).
- **The render path still runs** and is deduplicated by `requestId`: the listener has always
  applied a post-subscription settlement before React can render it, so the render path only
  ever counts (a) a cached entry settled before the listener existed (spec 310 docblock's
  "returning to a subject" case) and (b) mocked query results in page tests, which dispatch
  nothing. Both paths go through `applySettlement`.
- **`useDispatch` requires a Provider.** Every caller already has one (the page's `useQuery`
  needs it). No Provider-optional branch.
- Docblock: replace "Counts one strike per settled … in render" with the two-path description
  and why (autobatch; cite spec 314). Keep the StrictMode and "returning to a subject" paragraphs.

### 1.3 Store — `apps/management-web/src/app/store.ts`

```ts
const listenerMiddleware = createListenerMiddleware();
middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(apiSlices.map(...)),
```

**Prepend**, per RTK docs: the listener middleware consumes `addListener` actions without calling
`next`, so prepending keeps their function payloads away from the serializability check. Add a
one-line why-comment (spec 314). `store.test.ts`'s slice drift guard concerns `apiSlices` only and
is unaffected — confirm by running it.

### 1.4 Call sites (six, one argument each)

| File | Third argument |
|---|---|
| `features/cameras/CameraDetailPage.tsx:60` | `{ endpoint: camerasApi.endpoints.getCamera, args: { cameraIdentifier } }` |
| `features/cameras/CamerasPage.tsx:70` | `{ endpoint: camerasApi.endpoints.listCameras, args: listArgs }` |
| `features/audit/AuditPage.tsx:81` | `{ endpoint: auditApi.endpoints.searchAudit, args: applied }` |
| `features/layouts/LayoutsPage.tsx:61` | `{ endpoint: layoutsApi.endpoints.listLayouts, args: undefined }` |
| `features/overlays/OverlaysPage.tsx:59` | `{ endpoint: overlaysApi.endpoints.listOverlays, args: undefined }` |
| `features/systemVariables/SystemVariablesPage.tsx:56` | `{ endpoint: systemVariablesApi.endpoints.listVariables, args: variablesArgs }` |

Endpoint names are inferred from the hook names; confirm each against its `*.api.ts`. The args
must be **the same value** passed to the page's `useXQuery` — otherwise `select` reads a different
cache entry and the listener silently matches nothing. (Page tests mock the `useXQuery` hook but
keep the real `endpoints` object via `importOriginal`, so the endpoint reference resolves.)

## 2. Entities / invariants

No domain model. Hook-local invariants:

- **I1** A `requestId` contributes at most one transition (`counted` check in `applySettlement`).
- **I2** Transitions apply in settlement order (listener updaters queue in dispatch order; the
  render path can only see a settlement after the listener has applied it, or one that predates
  the listener).
- **I3** A transition for subject A never mutates subject B's state.

## 3. Messaging / boundaries

None. No `Shared.Contracts`, no backend. `apps/shared` → `apps/management-web` direction unchanged;
`apps/shared` gains no app import. **Dependency placement:** `@reduxjs/toolkit` is already in
`apps/shared`'s `dependencies`, but `react-redux` is only a **devDependency** and no non-test file
in `apps/shared/src` imports it today — the hook becomes the first. Move `react-redux` (same pinned
`9.3.0`) to `dependencies` beside `@reduxjs/toolkit` and refresh `pnpm-lock.yaml`; both consuming
apps already depend on that exact version, so nothing resolves differently. Part of T006.

## 4. Testing (phase 4a colour: **RED**)

No real rAF anywhere (FR-008). The controllable scheduler is RTK's own:

```ts
configureStore({
  reducer, middleware: (gd) => gd().prepend(createListenerMiddleware().middleware).concat(api.middleware),
  enhancers: (gde) => gde({ autoBatch: { type: 'callback', queueNotification: (notify) => { held = notify; } } }),
});
```

`flush()` = `act(() => { const n = held; held = undefined; n?.(); })`. Requests are driven with
`store.dispatch(api.endpoints.getThing.initiate(args, { forceRefetch: true, subscribe: false }))`
and awaited (the returned promise resolves after the settle action) — so "settled in the store but
not yet rendered" is a deterministic, held state rather than a timing accident. The first load's
promise comes from `api.util.getRunningQueriesThunk()`.

| # | File | Kind | Expected before fix |
|---|---|---|---|
| 1 | `apps/shared/src/test/revocationHarness.tsx` (new) | helper: held-notification store, scripted `createApi` (`fakeBaseQuery` + `queryFn` shifting a per-request response list: `200` or `{ status: 403 \| 503 }`), Provider wrapper, a `Probe` hook recording every rendered `(requestId, isFetching)` | n/a |
| 2 | `apps/shared/src/hooks/useRevocationFallbackCoalesced.test.tsx` (new) | the spec §1 scenarios | dropped strike: **red** (false ≠ true); dropped reset: **red** (true ≠ false); fully-coalesced request: **red**; missing middleware: **red** (no throw); outline rows, subject change, StrictMode: **green fences** — report as such |
| 3 | `apps/shared/src/hooks/useRevocationFallback.test.ts` (existing) | harness only: `renderFallback` and `renderFallbackStrict` gain a Provider wrapper over a harness store and pass an inert source. **No assertion, no case edited.** | green |
| 4 | `apps/management-web/src/features/cameras/CameraDetailRevocation.test.tsx` and `CameraDetailPageNavigation.test.tsx` (existing) | `createStore()` gains the prepended listener middleware. Nothing else. | green |

**Anti-tautology requirement for row 2.** Each "dropped" scenario MUST also assert its
precondition from the `Probe`'s render log — e.g. *no rendered entry has `requestId === r2 &&
!isFetching`* (and for the fully-coalesced case, *no rendered entry has `requestId === r2` at
all*). Without that, a green after the fix could mean the harness never reproduced the race. The
precondition assertions must be **green before the fix** while the outcome assertion is red; quote
both.

The current hook ignores a third argument at runtime, so rows 2-3 execute against it and fail on
assertions, not on import; `tsc --noEmit` will additionally report the extra argument — quote it as
part of the red, as spec 313's phase 4a did for its missing export.

## 5. Constitution / ADR check

- §II value objects — frontend, N/A. §IV latency — N/A (spec §5). §VII dashboards — N/A.
- ADR-0075 — RTK's listener middleware is part of the locked library. ADR-0139 — red first, with
  the precondition proof above. ADR-0109 — test files are new or disjoint from #2750's.
- ADR-0036 — smallest change: one parameter, one effect, one extracted function, one store line;
  no autobatch reconfiguration, no new abstraction beyond `RevocationSource`.

## 6. Composition with #2750 (PR #2775) — what the rebase resolves

#2750 changes the strike **condition**; this changes the **counting**. Expected after rebasing onto
a `develop` that contains #2775:

1. **`useRevocationFallback.ts`** — textual conflicts in (a) the import line (`isNotFound`), (b) the
   `RevocationQueryState` interface (#2750 adds `notFoundRevokes?`, this spec leaves the
   interface alone → take theirs), (c) the strike line: #2750 edits it in place, this spec moves it
   into `applySettlement`. Resolution: `applySettlement` takes the refusal predicate's inputs —
   `isForbidden(error) || (notFoundRevokes === true && isNotFound(error))` — and the **listener path
   reads `notFoundRevokes` from the latest render** (a ref assigned in an effect; it is a page
   policy flag derived from `currentData`, which RTK retains across a rejection, so it is stable
   between renders). Docblock: merge both paragraphs.
2. **`CameraDetailPage.tsx`** — #2750 reformats the call to a multi-line object; add the third
   argument to it. Trivial.
3. **`useRevocationFallbackNotFound.test.ts`** (#2750, new) — calls the hook with two arguments;
   `tsc` will flag it. Apply the same harness-only change as row 3 (Provider wrapper + inert
   source); assertions untouched.
4. **`CameraDetailFabRemoval.test.tsx`** (#2750, new) — builds its own store; without the listener
   middleware the hook now throws (FR-006). Add the prepended middleware, nothing else.
5. Add one spec-314 scenario variant for 404 under `notFoundRevokes` (coalesced 404 still counts)
   so the listener path is proven to honour spec 313's condition, not just the render path.

If #2775 merges **before** phase 4 starts, do phase 4 on the rebased branch and fold items 1-5
into T004/T005/T009 directly instead of resolving them as conflicts.

## 7. Contention

- #2750 — §6.
- #2337 (`sse-2337`) — no overlap found (`specs/` top is 312 there; not a frontend hook change).
- Spec number 314 — re-check before the PR.
