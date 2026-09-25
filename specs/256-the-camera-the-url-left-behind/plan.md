# Plan — Spec 256, the camera the URL left behind

**Spec:** `specs/256-the-camera-the-url-left-behind/spec.md`
**Issue:** #2522
**Phase-4a colour:** **RED.** Behaviour-changing — the page renders something different (loading instead of a stale record) for a state it can reach. New-behaviour tests must be observed **failing**, verbatim output returned and quoted in the PR (ADR-0139, ADR-0144). A new-behaviour test arriving green is a phase-4 failure.

---

## Context and layers

Frontend-only; no bounded context, aggregate, event, migration or `Shared.Contracts` change.

| Layer | File | Change |
|---|---|---|
| `management-web` feature (ADR-0074) | `apps/management-web/src/features/cameras/CameraDetailPage.tsx` | **The only production edit.** |
| Router | `apps/management-web/src/app/router.tsx` | **None** (see Decision). |
| `apps/shared` API client (ADR-0075) | `apps/shared/src/api/cameras.api.ts` | **None.** |
| Tests | `CameraDetailPage.test.tsx` (mocked hook) + **new** `CameraDetailPageNavigation.test.tsx` (real hook) | Add only. |

Boundary: the page reads only fields already on the `useGetCameraQuery` result (`isFetching` is one of them). No `camerasApi.endpoints.*.select`, no `api.util.*` in production code.

---

## Decision — gate on `isFetching` in the page, not a route `key`

**Chosen: an `isFetching` gate inside `CameraDetailPage`.**

| | `isFetching` gate (chosen) | `key={cameraIdentifier}` remount |
|---|---|---|
| Where correctness lives | In the page, under any mounting | In `router.tsx`; the page stays wrong wherever else it is mounted, and the existing unit tests mount it without a router key |
| Mechanics | One condition on a field the hook already returns | `createBrowserRouter` builds `element` once, so a param-derived key needs a **new wrapper component** reading `useParams` — new surface for a bug fix |
| Files | 1 production file | `router.tsx` + a wrapper, and the page is untouched so its own tests cannot go red for the fix |
| Red-first | The mocked-hook file and a real-hook file both go red on the page | The mocked file cannot express it at all; only a router-level test could |
| Consistency | Matches spec 211's design: `record` is explicitly written for same-instance reuse (see its comment, "including across a same-instance navigation away and back"); and spec 153's `LayoutEditorDialog` fix, same shape (`currentData` + fetching, on a reused instance) | Contradicts both: fixes by avoiding the reused instance instead |

The remount's commonly-cited costs are **not** the reason — be precise about that: a detail→detail move changes `cameraIdentifier`, so `CameraViewer`'s WHEP session restarts either way, and scroll/dialog state is not meaningfully preserved today. The reasons are locality, the one-file scope, and that the page itself becomes testably correct.

**Rejected alternative, also recorded:** gating on `currentData === undefined && error === undefined` without `isFetching`. In real RTK that shape without a fetch is unreachable, but it would break the 13 pre-existing tests whose default mock omits `currentData` — an edited mock would be required, which ADR-0144 forbids. `isFetching` is also the more honest statement: *a request for this identifier is in flight and nothing for it has arrived*.

---

## The change, precisely

### Today (`CameraDetailPage.tsx:41-52`)

```tsx
const { data: camera, currentData, isLoading, error, refetch } = useGetCameraQuery({ cameraIdentifier });
const record = error !== undefined ? currentData : camera;

if (isLoading) {
  return <Surface>Loading…</Surface>;
}
```

### After

```tsx
const { data: camera, currentData, isLoading, isFetching, error, refetch } = useGetCameraQuery({ cameraIdentifier });
const record = error !== undefined ? currentData : camera;

if (isLoading || (isFetching && currentData === undefined && error === undefined)) {
  return <Surface>Loading…</Surface>;
}
```

`record`, the "No such camera" branch, the alert, the controls, and every render site are **unchanged**.

### Comment obligation

The comment above `record` (`:44-50`) already explains the `data`/`currentData` split. Extend the `isLoading` gate with a short *why*: `isLoading` stays false when `data` carried over from a previous identifier, so "a request for this identifier is in flight and nothing for it has arrived" must be tested directly; `error === undefined` keeps spec 211's refusal rows reading as "No such camera". No issue/spec numbers in the comment (house rule).

### The rendering contract — spec 211's table, extended

`isFetching` column added. Rows 1–7 are spec 211's, restated with the column filled; only row 8 changes.

| # | `isLoading` | `isFetching` | `camera` (`data`) | `currentData` | `error` | Render | Change |
|---|---|---|---|---|---|---|---|
| 1 | `true` | any | any | any | any | `Loading…` | unchanged |
| 2 | `false` | `false` | `undefined` | `undefined` | `undefined` | No such camera | unchanged |
| 3 | `false` | any | `undefined` | `undefined` | set | No such camera | unchanged (FR-008) |
| 4 | `false` | any | defined | `undefined` | set | No such camera | unchanged (spec 211 US1-C) |
| 5 | `false` | any | defined | defined | set | record + alert + Retry | unchanged (spec 211 fix) |
| 6 | `false` | any | defined | defined | `undefined` | record, no alert | unchanged — **includes a background refetch of the camera on screen** (`isFetching` true, `currentData` kept by `writePendingCacheEntry`) |
| 7 | `false` | `false` | defined | `undefined` | `undefined` | record | unchanged — unreachable in real RTK; it is the shape of every pre-existing mock, which is why it must not move |
| **8** | `false` | **`true`** | defined | `undefined` | `undefined` | **`Loading…`** | **new — the fix (issue #2522)** |

Row 2 with `isFetching` true is unreachable (real RTK would report `isLoading` true, row 1); either way it renders as before — the new clause renders `Loading…` there, which is also what row 1 would. Note for reviewers: that is the *only* other cell the new clause reaches, and it is not reachable.

---

## Testing strategy

### Two files, disjoint, both RED before the production edit

**A. `CameraDetailPage.test.tsx` (mocked hook) — add, do not edit.**

- Type change only: add `isFetching?: boolean` to `GetCameraResult` (`:12-18`), and extend the comment at `:9-11` to name it — the same optional-field pattern spec 211 set. No existing `mockReturnValue` gains the field; omitted means falsy, which is row 7 and renders as today.
- New tests: row 8 (RED), row 6-while-fetching (fence, green), row 4-while-fetching (fence, green). See tasks T001.

The row 8 / row 6 pair is load-bearing: an `isFetching`-only gate passes row 8 and fails row 6. The row 8 / row 4 pair catches a gate that drops `error === undefined`. Write each pair adjacent with a comment saying so.

**B. `CameraDetailPageNavigation.test.tsx` (new, real hook) — the observation the issue asks for.**

A mocked hook returns whatever the test hands it, so file A can only prove the page's mapping *given* the shape this plan derived from RTK's source; it cannot prove RTK produces that shape. (Memory: an assertion must not check its own input.) File B uses the **real** `useGetCameraQuery` against a stubbed `fetch`, modelled directly on `apps/management-web/src/features/layouts/LayoutEditorDialogChainRetention.test.tsx` (spec 153): `vi.stubEnv('VITE_API_GATEWAY_URL', …)` before any dynamic import, a fresh `configureStore` per test with `camerasApi`, `react-oidc-context` and `CameraViewer` mocked exactly as in file A (jsdom has no `RTCPeerConnection`).

Same-instance navigation is essential: use `createMemoryRouter` + `RouterProvider` with the single `cameras/:cameraIdentifier` route and `act(() => router.navigate('/cameras/B'))`. Remounting between URLs would give the hook no `lastResult` and the test would pass for the wrong reason, so navigate on one rendered tree and never call `render` a second time.

### Existing tests: none edited

All 21 existing tests in file A use shapes in rows 4–7 with `isFetching` omitted. None reaches row 8. **No assertion moves and no existing mock return is edited.** If an engineer finds one must, the behaviour moved further than this spec allows: block, do not adjust (ADR-0144).

### Counterfactuals the reviewer should run (prove the guard, memory "prove a guard by counterfactual")

| Mutation to the gate | Caught by |
|---|---|
| No fix | T001 row-8 test and both T002 in-flight tests (RED) |
| `isFetching && error === undefined` (drop `currentData`) | T001 row-6 fence; T002 background-refetch case |
| `isFetching && currentData === undefined` (drop `error`) | T001 row-4-fetching fence |
| `currentData === undefined && error === undefined` (drop `isFetching`) | 13 pre-existing tests (default mock, row 7) |

### Gates

`pnpm lint`, `pnpm typecheck`, `pnpm typecheck:e2e`, `pnpm test`. (`typecheck:e2e` can fail on a clean `develop` for an unrelated `@types/node` — stash and re-run before attributing it here.) No C#, no coverage/Sonar gate reached. No dependency change.

### E2E — not added, and why

A Playwright test would need A's cache entry to expire (60 s) before a `history.go(-2)` jump. A 60-second wait per run, or `page.clock` fast-forwarding, which also moves OIDC silent-renew and WHEP timers, costs more than it proves beyond T002, which exercises the real hook through a real router. Phase 5 observes it in a browser by the procedure in `spec.md`.

### Security review trigger

**Yes, light:** the page sits on FR-008 (a refused camera indistinguishable from a missing one). The reviewer's one question: *does the loading surface ever render anything that varies with a refused identifier?* It renders a constant string, and refusals (`error` set) never reach it — row 4-fetching fence.
