# Tasks — Spec 256, the camera the URL left behind

**Spec:** `specs/256-the-camera-the-url-left-behind/spec.md`
**Plan:** `specs/256-the-camera-the-url-left-behind/plan.md`
**Issue:** #2522 (feature-level; confirm it is on Project #13 — `/speckit-tasks` adds nothing to the board)
**Branch:** `fix/2522-cameradetailpage-stale-render` (worktree `D:/Github/sse-2522`)
**Phase-4a colour:** **RED** — behaviour-changing (ADR-0139, ADR-0144).

---

## Ordering

```
T001 [P] ─┐
T002 [P] ─┴─► T003 ─► T004 ─► T005 ─► T006
(4a red)     (4b)     (verify) (QA)   (PR)
```

Nothing foundational (no `Shared.Kernel`, `Shared.Contracts`, AppHost). `T001` and `T002` own disjoint files and may run in one or two `test-writer` passes; both must be red-observed before `T003`.

---

## US1 (P1) — A camera's page never shows another camera

### Phase 4a — RED (`test-writer`)

#### `[T001] [P] [US1]` — Mocked-hook tests for the in-flight gate

**File (sole owner):** `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`

1. Add `isFetching?: boolean` to `GetCameraResult` (`:12-18`); extend the comment at `:9-11` to cover it. **Edit no existing test and no existing `mockReturnValue`.**
2. Add, adjacent, with a comment naming each pair as load-bearing (plan §Testing):

| Name (sentence style, ADR-0053) | Mock shape | Render at | Assert | Expected today |
|---|---|---|---|---|
| `Shows loading, not the previous camera, while the new one is fetched` (row 8) | `{ data: camera, currentData: undefined, isLoading: false, isFetching: true, error: undefined, refetch }` | `otherCamera.cameraIdentifier` | `getByText('Loading…')`; `queryByText(camera.name)`, `queryByText(camera.fab)`, `queryByText(camera.rtspUrl)` all null; `queryByTestId('camera-viewer')` null; no Rename / Correct-the-address / Retire buttons; `viewerRenders` has length 0 | **RED** |
| `Keeps showing the camera while its own record is refreshed` (row 6 fence) | `{ data: camera, currentData: camera, isLoading: false, isFetching: true, error: undefined, refetch }` | `camera.cameraIdentifier` | heading `camera.name`; `queryByText('Loading…')` null; no alert | green, must stay green |
| `Still shows no such camera while a refused identifier is refetched` (row 4 fence) | `{ data: camera, currentData: undefined, isLoading: false, isFetching: true, error: { status: 404 }, refetch }` | `'22222222-2222-2222-2222-222222222222'` | heading `/no such camera/i`; `queryByText('Loading…')` null; `queryByText(camera.name)` null; no alert, no Retry | green, must stay green |

**Report:** `pnpm vitest run apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`, **verbatim** output, per-test colour (not one colour for the file). The 21 pre-existing tests must be green.

---

#### `[T002] [P] [US1]` — Real-hook navigation tests (the observation #2522 asks for)

**File (sole owner, new):** `apps/management-web/src/features/cameras/CameraDetailPageNavigation.test.tsx`

Model: `apps/management-web/src/features/layouts/LayoutEditorDialogChainRetention.test.tsx` — `vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test')` before any dynamic import; a fresh `configureStore` with `camerasApi` per test; stubbed `fetch` with per-URL held/resolved responses; `react-oidc-context` and `CameraViewer` mocked as in `CameraDetailPage.test.tsx` (record viewer props). **`useGetCameraQuery` is NOT mocked** — that is the point of the file; say so in its header comment.

Mount `createMemoryRouter([{ path: '/cameras/:cameraIdentifier', element: <CameraDetailPage /> }], { initialEntries: ['/cameras/A'] })` in `RouterProvider`, **once**. Navigate only with `act(() => router.navigate(...))` — a second `render` would drop RTK's `lastResult` and make the test pass for the wrong reason.

| Name | Steps | Assert | Expected today |
|---|---|---|---|
| `Does not show the previous camera while the next one loads` | resolve A; wait for A's heading; hold B's GET; navigate to B | `Loading…` shown; A's name, fab and RTSP URL absent; no viewer render recorded after the navigation carries A's `cameraIdentifier` | **RED** (A's record renders at B's URL) |
| `Shows the next camera once it arrives` | continue: release B's GET | B's heading, fab, RTSP URL shown; nothing of A | **RED** only if it cannot reach this step; otherwise green — report which |
| `Does not blank the camera on screen while it is refreshed` | A loaded; hold A's next GET; `store.dispatch(camerasApi.util.invalidateTags([{ type: 'Camera', id: A }]))` (test-only use of `api.util` is fine) | A's heading stays; `Loading…` never appears (check before releasing) | green, must stay green |

Assert on the page, not on the hook's return — reading `isFetching`/`currentData` back would check the test's own input.

**Report:** `pnpm vitest run apps/management-web/src/features/cameras/CameraDetailPageNavigation.test.tsx`, verbatim, per-test colour. **If the first test is green today, stop and report it** — the premise of #2522 would be wrong against the pinned library and `T003` must not proceed.

---

### Phase 4b — GREEN (`frontend-engineer`)

#### `[T003] [US1]` — Gate the loading surface on an in-flight request with no record for this identifier

**File (sole owner):** `apps/management-web/src/features/cameras/CameraDetailPage.tsx`
**Depends on:** T001 and T002 red observed. **Brief:** their verbatim output. You may not edit the tests.

1. `:41` — add `isFetching` to the destructuring.
2. `:52` — `if (isLoading) {` becomes
   `if (isLoading || (isFetching && currentData === undefined && error === undefined)) {`
3. Add a short *why* comment above the gate (plan §Comment obligation). No spec/issue numbers in code.

**Forbidden:** changing `record`, the "No such camera" markup or its FR-008 comment, the alert, any control gate, any render site; touching `router.tsx` (no route `key`), `cameras.api.ts`, `apps/shared/`, `kiosk-web`, or any other page.

**Done when:** T001 and T002 green with assertions unmodified; all 21 pre-existing tests green unmodified; `pnpm lint && pnpm typecheck && pnpm typecheck:e2e && pnpm test` clean.

---

## Phase 5 — Verify

#### `[T004]` — `/verify`

Run `spec.md` §"Independent end-to-end test procedure" (history jump after the 60 s cache expiry, request held) and write `specs/256-the-camera-the-url-left-behind/verification.md`. Record what was seen at each step, including the within-60 s control. **Latency: N/A** — say so explicitly. Check the AppHost's start time against the commit before trusting the observation.

## Phase 6 — QA

#### `[T005]` — `frontend-reviewer` + light `security-reviewer`

Run the counterfactual table in `plan.md` §Testing (each mutation must turn at least one test red). Security question: can the loading surface render anything that varies with a refused identifier? (No: constant text; `error` set never reaches it.)

## Phase 7 — PR

#### `[T006]` — `gh pr create --base develop`

Body: `Closes #2522`; verbatim red output from T001 and T002; the Decision (isFetching gate, route key rejected, with the one-line why); the reachability finding (history jump to an expired entry only — no in-app detail→detail link exists); assumption A1 marked as a guess; `Phase 3: feature issue #2522 on Project #13`.

---

## Task table

| ID | [P] | Story | Agent | File(s) | Depends on |
|---|---|---|---|---|---|
| T001 | `[P]` | US1 | `test-writer` | `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` | — |
| T002 | `[P]` | US1 | `test-writer` | `apps/management-web/src/features/cameras/CameraDetailPageNavigation.test.tsx` (new) | — |
| T003 | | US1 | `frontend-engineer` | `apps/management-web/src/features/cameras/CameraDetailPage.tsx` | T001, T002 red observed |
| T004 | | — | `/verify` | `specs/256-the-camera-the-url-left-behind/verification.md` | T003 |
| T005 | | — | `frontend-reviewer` + `security-reviewer` | — | T004 |
| T006 | | — | orchestrator | — | T005 |
