# Tasks: Open one camera, and fix it

**Input**: Design documents from `/specs/030-camera-detail-view/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/camera-detail-ui.md)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to
- Exact file paths in every description

## No setup phase, no backend change, no migration

Everything this needs is already installed and already used somewhere:
`react-router-dom` 7.1.3 (kiosk-web), RTK Query, React Hook Form + Zod
(`RegisterCameraDialog`), Radix, Playwright.

**Spec 029 left the wire in the right shape**, so this is a frontend feature.
**If a backend change proves necessary, that contradicts spec 029's contract and
is a finding to raise, not absorb** — the same instruction spec 029 carried, and
it caught two real things there.

## A note on ordering

Phases follow the plan rather than user-story priority, because two pieces of
groundwork serve all three stories and one of them is the feature's only shared
code change. US1 is reachable at Phase 4, US2 at Phase 5, US3 across both.

**Phases 1 and 2 are independent of the routing decision** and come first
deliberately: if the Phase 2 gate overturns the shell conversion, nothing in
them is wasted. **Phase 3 is the droppable unit** — overturning it removes six
files of churn and leaves only the cameras surface routed.

---

## Phase 1: The API client

**Goal**: The two endpoints spec 029 shipped become callable, and the listing
type stops lying about what the wire returns.

- [x] T001 [P] `getCamera` query in `apps/shared/src/api/cameras.api.ts` — `GET /{cameraIdentifier}`, providing `{ type: 'Camera', id }` so a correction can invalidate exactly this camera
- [x] T002 [P] `CameraDetail` type in `apps/shared/src/api/cameras.api.ts` — identifier, version, fab, name, rtspUrl, registeredAt, status, per data-model.md
- [x] T003 Add `version` and `status` to `CameraSummary` in `apps/shared/src/api/cameras.api.ts` — spec 029 returns both on **every listing row** and the interface was never updated; it is a plain interface with no runtime validation, so nothing broke, it is simply out of date with the wire
- [x] T004 `changeCameraAddress` mutation in `apps/shared/src/api/cameras.api.ts` — `PATCH /{cameraIdentifier}`, headers from the **existing** `ifMatch(version)` helper in `apps/shared/src/api/gateway.ts`, invalidating that camera's tag **and** `LIST`
- [x] T005 [P] `changeCameraAddressSchema` in `apps/shared/src/api/cameras.schema.ts` — reuses `registerCameraSchema`'s RTSP rules so FR-009 rejects client-side exactly what the API would reject, rather than a second opinion about what a valid address is

**Checkpoint**: the endpoints are reachable from the app; nothing renders yet.

---

## Phase 2: Refusals into words

**Goal**: A camera's two interesting refusals stop mapping to the wrong advice.

**This is the feature's only shared-code change.** `problemDetail.ts` is used by
layouts, overlays and system variables, so it must be **additive**.

> **Provisional, pending #1857.** That issue argues the proper fix is to key
> `isStaleConflict` on the **code alone** and rename `CAMERA_VERSION_MISMATCH`
> to `CAMERA_VERSION_STALE`. The rename is a **backend** change and is out of
> scope here, and code-only keying *without* it would still fail for cameras —
> `CAMERA_VERSION_MISMATCH` does not end in `_STALE`. So this phase does the
> frontend-only fix that works today. **Do not read it as the settled
> convention**; #1857 supersedes it.

- [x] T006 Recognise the 412 stale case in `apps/shared/src/api/problemDetail.ts` — `isStaleConflict` must be true for status **412** with `CAMERA_VERSION_MISMATCH`, **and unchanged** for 409 + `*_STALE`. Comment it as provisional per #1857
- [x] T007 [P] `isTerminalRefusal` (or equivalent) in `apps/shared/src/api/problemDetail.ts` — true for `CAMERA_RETIRED`, so a terminal refusal stops inheriting the lost-update wording. **Keyed on the code, not the status**: `CAMERA_RETIRED` is a 409 and so matches `isConflict`
- [x] T008 [P] Unit tests in `apps/shared/src/api/problemDetail.test.ts` — 412 + `CAMERA_VERSION_MISMATCH` is stale; 409 + `CAMERA_RETIRED` is **not** stale and **is** terminal; **409 + `LAYOUT_REVISION_STALE` still behaves exactly as before**, and `LAYOUT_NAME_TAKEN` is still neither
- [x] T009 Confirm the existing consumers are untouched — run the layouts, overlays and system-variables suites; **their tests must pass without edits**. An edit needed there means the change was not additive

**Checkpoint**: the words are right, and nothing that used the helper moved.

---

## Phase 3: The router

**Goal**: The shell routes, so a camera can have a location.

**The droppable unit.** If the Phase 2 gate overturns the shell conversion, this
phase shrinks to routing the cameras surface alone and T012–T014 disappear.

- [x] T010 `apps/management-web/src/app/router.tsx` — `createBrowserRouter`, mirroring `apps/kiosk-web/src/app/router.tsx`. Routes for the six surfaces, `/cameras/:cameraIdentifier`, and **`/oidc/callback`** — `react-oidc-context` intercepts it before the router sees it, but the route must exist; kiosk-web records the same requirement
- [x] T011 Convert `Shell` to `RouterProvider` in `apps/management-web/src/App.tsx` — the `AuthGate` still wraps the router, not the other way round, so sign-in and session expiry behave as they do now
- [x] T012 Nav buttons become links in `apps/management-web/src/App.tsx` — **links, not buttons calling `useNavigate`**. The button form keeps every existing selector green and loses middle-click, open-in-new-tab and copy-link, which is most of what FR-002 is for
- [x] T013 **Re-key the `ErrorBoundary` on the location** in `apps/management-web/src/App.tsx` — it is keyed on `view` today so a crashed page is replaced fresh and the nav survives (**spec 011 FR-016**, not this feature's requirement). Its own task because **nothing in spec 030 would fail if this regressed**
- [x] T014 [P] Update `apps/management-web/src/App.test.tsx` — it asserts the shell's `useState` toggle, which no longer exists
- [x] T015 [P] Update the nav selector in `e2e/audit.spec.ts` — `getByRole('button', …)` → link
- [x] T016 [P] Update the nav selector in `e2e/layouts.spec.ts`
- [x] T017 [P] Update the nav selector in `e2e/overlays.spec.ts`
- [x] T018 [P] Update the nav selector in `e2e/rules.spec.ts`
- [x] T019 [P] Update the nav selector in `e2e/system-variables.spec.ts`

> `e2e/cameras.spec.ts` is **not** in this list — cameras is the default surface,
> so it never clicks a nav button to reach it.

**Checkpoint**: every surface still reachable, crash panel still scoped, six selectors updated.

---

## Phase 4: User Story 1 — Open one camera (P1)

**Goal**: A camera opens, at its own location.

**Independent test**: Open a camera from the list; copy the location into a new
tab and get the same camera; press back and land on the list.

- [ ] T020 [US1] `CameraDetailPage` in `apps/management-web/src/features/cameras/CameraDetailPage.tsx` — reads `:cameraIdentifier` via `useParams`, calls `useGetCameraQuery`, shows fab, name, address, registration time, status. **The version is held, never displayed** — it is machinery, not information
- [ ] T021 [US1] Rows link to the detail route in `apps/management-web/src/features/cameras/CamerasPage.tsx` — the list keeps working exactly as it does now (FR-011)
- [ ] T022 [US1] **A retired camera is visibly marked** in `CameraDetailPage.tsx` (FR-007)
- [ ] T023 [US1] **Not-found handling that adds nothing** in `CameraDetailPage.tsx` — a 404 renders "no such camera" and **must not** say "you do not have access". The API answers identically for another fab's camera and one that never existed; a helpful branch here undoes that at the last hop (FR-008)
- [ ] T024 [P] [US1] Component tests in `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` — renders a camera; renders a retired one as retired; **renders a 404 identically whatever the cause**
- [ ] T025 [P] [US1] Assert the detail view does **not** issue the list query — SC-002 and FR-003. A page that renders correctly while fetching the catalogue passes every other test here

**Checkpoint**: US1 is shippable.

---

## Phase 5: User Story 2 — Correct the address (P2)

**Goal**: The address can be corrected, and every refusal says something useful.

**Independent test**: Correct an address and see it change. Requires US1 and Phases 1–2.

- [ ] T026 [US2] `EditCameraAddressDialog` in `apps/management-web/src/features/cameras/EditCameraAddressDialog.tsx` — mirrors `RegisterCameraDialog` (React Hook Form + Zod, ADR-0079). **Address only; no name field** (FR-010, #1850)
- [ ] T027 [US2] Wire the dialog into `CameraDetailPage.tsx`, passing the version the operator was shown, and **hide the control entirely for a retired camera** (FR-007)
- [ ] T028 [US2] Refusals rendered in `EditCameraAddressDialog.tsx` using the Phase 2 helpers — stale → *reload to see their version*; retired → *this camera is retired*; and **what the operator typed is kept** so they need not retype it
- [ ] T029 [P] [US2] Component test — a **stale version** shows a message containing *reload* and **not containing "try again"**. Asserting "an error appeared" passes while the words are wrong, and the wrong words cause the lost update the mechanism exists to prevent
- [ ] T030 [P] [US2] Component test — a **retired** camera shows a message saying *retired* and **not** *someone else changed this*. `CAMERA_RETIRED` is a 409, so it matches `isConflict` and inherits the wrong words by default
- [ ] T031 [P] [US2] Component test — **the edit control is absent** for a retired camera. Assert absence, not that submitting fails: discovering the refusal on submit is not FR-007
- [ ] T032 [P] [US2] Component test — an invalid address is refused **before any request is made** (FR-009), and the displayed address after a refused correction is the stored one (FR-004)

---

## Phase 6: End to end, and polish

- [ ] T033 [US1] e2e in `e2e/camera-detail.spec.ts` — open a camera from the list; **reload the URL directly** and get the same camera; back returns to the list
- [ ] T034 [US2] e2e in `e2e/camera-detail.spec.ts` — correct an address and see it reflected without a full reload
- [ ] T035 [US3] e2e in `e2e/camera-detail.spec.ts` — a **retired** camera opens, is marked, and offers no edit control
- [ ] T036 [US3] e2e in `e2e/camera-detail.spec.ts` — as the **Dresden** operator, open a **Munich** camera by URL and an identifier that never existed; **compare the rendered output**, not merely that both showed something (FR-008 / SC-004)
- [ ] T037 Full suite — `frontend` lint, typecheck and unit tests; the Playwright suite with nothing excluded
- [ ] T038 Verification note on the PR following [quickstart.md](./quickstart.md), including the two refusal wordings, the FR-008 comparison, and the crash-panel check

---

## Dependencies

```
T001 … T005  (API client)  ─┐
                            ├─► T020 … T025   US1
T006 … T009  (words)  ──────┤        ↓
                            │   T026 … T032   US2
T010 … T019  (router) ──────┘        ↓
                                T033 … T038
```

**Phases 1 and 2 depend on nothing** and are safe to start before the gate
settles the routing question. **Phase 3 blocks US1** only because a camera needs
a location to open at.

## Parallel opportunities

- **T001, T002, T005** — query, type and schema; T003/T004 touch the same file as T001 and follow it.
- **T007 and T008** — the predicate and its tests, once T006 lands.
- **T014 through T019** — six independent test files, no shared state.
- **T024, T025** — different assertions, same new test file; split if convenient.
- **T029 through T032** — four independent component tests.

## Implementation strategy

**Phases 1 and 2 first, deliberately.** They serve all three stories and neither
depends on the routing decision, so a gate reversal wastes none of it.

**MVP is Phase 4** — a camera that opens is the feature; correcting it is the
payoff.

**Phase 3 is the reversible one.** Overturning the shell conversion drops
T012–T019 and leaves T010–T011 scoped to cameras.

---

## Three things most likely to go wrong

**The refusal says the wrong thing and every test still passes.** Both stale and
retired produce *a* message, so any test asserting "an error was shown" is green
while the operator is told to *try again* on a stale version — which replays
their change over the other writer's, the exact lost update `If-Match` exists to
prevent. **T029 and T030 assert the words**, including the words that must
*not* appear.

**The app helpfully undoes spec 029's discretion.** A `catch` that renders "you
do not have access to this camera" reintroduces the enumeration FR-006 and
SC-003 were built to prevent — at the last hop, in the one layer that spec could
not test. It will look like an improvement in review. **T023 and T036** are the
guard, and T036 compares the rendered output rather than trusting two 404s to be
the same thing.

**The crash panel quietly loses its scope.** The `ErrorBoundary` is keyed on
`view` so the nav survives a page crash — **spec 011 FR-016**, not this
feature's requirement. Under a router it must key on the location. **Nothing in
spec 030 would fail if this regressed**, which is exactly why T013 is its own
task rather than a line inside T011.
