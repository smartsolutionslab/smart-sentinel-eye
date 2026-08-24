# Tasks: Retire a camera from the management app

**Feature**: `032-retire-camera-ui` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: #1860

**19 tasks.** This is a small feature and the list is not padded to disguise
that. What it is not is a button — six of these tasks exist because the
requirement they cover is one a passing test suite will happily let you get
wrong.

**No setup phase and no migration.** React, RTK Query, Radix, Tailwind and
Playwright are all present and in use. The only addition is
`@radix-ui/react-alert-dialog`, which is T001.

**No backend change.** `POST /cameras/{camera}/retire` exists, is idempotent and
advertises `204/400/403/404`. If a change proves necessary that contradicts
spec 028's contract and is a **finding to raise, not to absorb**.

---

## Phase 1: The confirmation primitive

**Goal**: The thing that does not exist anywhere in this product.

**No precedent to follow.** `RulesPage`, `OverlaysPage`, `LayoutsPage` and
`SystemVariablesPage` all archive on a single click and confirm nothing — see
[research.md](./research.md) §1. This is the first destructive confirmation, so
it is built shared, and the shape it sets is what the next one copies.

- [ ] T001 Add `@radix-ui/react-alert-dialog` at the `1.1.x` line to `apps/shared/package.json` — matching the six Radix packages already there — and run `pnpm install`. **The only new dependency in this feature.** Adding another Radix primitive *is* ADR-0077, not a deviation from it
- [ ] T002 [P] Add a `danger` variant to `ButtonVariant` and the `variants` map in `apps/shared/src/ui/primitives/Button.tsx`, using existing design tokens (ADR-0078). A confirm-and-retire control rendered as `primary` is visually identical to *Save* and *Register*
- [ ] T003 Create `apps/shared/src/ui/primitives/ConfirmDialog.tsx` on `@radix-ui/react-alert-dialog` — `role="alertdialog"` via `AlertDialog.Content`, `AlertDialog.Cancel` and `AlertDialog.Action`, the action `danger`-styled, and a `pending` prop that disables the action while a request is in flight. **Focus must default to cancel**, which the primitive gives for free and a hand-rolled `role` override would not: for an irreversible action that is the difference between a stray Enter dismissing and a stray Enter retiring a camera
- [ ] T004 [P] Tests in `apps/shared/src/ui/primitives/ConfirmDialog.test.tsx` — **three call-count assertions, not three "it rendered" assertions**: cancelling calls `onConfirm` **zero** times; confirming calls it **exactly once**; clicking the action twice while `pending` calls it **exactly once**. The third is FR-015 and it is the one that fails on a real double-click
- [ ] T005 [P] In `apps/shared/src/ui/primitives/ConfirmDialog.test.tsx`, assert the rendered element carries `role="alertdialog"` — not `dialog`. This is the assertion that fails if someone later "simplifies" the primitive back onto the existing `Dialog`, which is the alternative [research.md](./research.md) §2 rejected

**Checkpoint**: a shared confirmation primitive exists, with its safety
properties asserted, and no caller yet.

---

## Phase 2: The mutation

**Goal**: A client for an endpoint that has had none since spec 028.

**Independent of Phase 1.** Either order; both block Phase 3.

- [ ] T006 Add a `retireCamera` mutation to `apps/shared/src/api/cameras.api.ts` — `POST` to `/${cameraIdentifier}/retire`, `fabId` threaded exactly as `getCamera` and `changeCameraAddress` thread it, body absent. Mirror the existing endpoints' shape rather than inventing one
- [ ] T007 In `retireCamera` in `apps/shared/src/api/cameras.api.ts`, set `invalidatesTags` to `{ type: 'Camera', id: cameraIdentifier }` **and** `{ type: 'Camera', id: 'LIST' }` — identical to `changeCameraAddress`. [data-model.md](./data-model.md) records why one invalidation satisfies FR-009, FR-010 and FR-011 together: RTK Query **refetches** for live subscribers rather than evicting, so the detail page re-renders with the new status and the retired camera stays readable, with no code written for either
- [ ] T008 [P] **FR-016 — assert the request carries no `If-Match`.** Test in `apps/shared/src/api/cameras.api.test.ts` (or the nearest existing api test file) that the built request has **no** `If-Match` header. The endpoint declares no `409`, `412` or `428`, so a version would invent a failure mode the backend does not have — and `CameraDetailPage` already holds the version for the address-correction flow, which makes threading it in the natural mistake. The assertion is what catches it; nothing else would

**Checkpoint**: the endpoint is callable and provably unversioned.

---

## Phase 3: The control and its words

**Goal**: An operator can retire a camera, and is told what it costs first.

**Needs Phases 1 and 2.**

This phase is where a naive implementation passes its tests and is still wrong.
The wording tasks are deliberately **not** collapsed into one.

- [ ] T009 [US2] Create `apps/management-web/src/features/cameras/RetireCameraDialog.tsx` consuming the Phase 1 `ConfirmDialog` — takes the camera's name and identifier, calls `useRetireCameraMutation`, and passes `pending` from the mutation's `isLoading`. No React Hook Form and no Zod: a confirmation has no fields, and ADR-0079 governs forms, so applying it here would be ceremony
- [ ] T010 [US1] Render the retire control in `apps/management-web/src/features/cameras/CameraDetailPage.tsx`, gated on `retired` exactly as the *Correct the address* control three lines above it already is — the same `retired ? null : <Button…>` shape, not a new pattern
- [ ] T011 [P] [US2] **FR-003/005/006/007 — four separate assertions** in `apps/management-web/src/features/cameras/RetireCameraDialog.test.tsx`: the confirmation contains (a) the camera's **name**, (b) that retirement is **permanent / cannot be undone**, (c) that the **live stream stops**, (d) that the **name becomes available for reuse** in that fab. One assertion that "a confirmation appeared" passes while three of the four sentences are missing. (c) and (d) are the two an operator cannot discover from the camera's own page, and (d) is the payoff spec 028 built that nothing has ever surfaced
- [ ] T012 [P] [US2] **FR-008 — assert the mutation was called zero times** after cancelling, in `apps/management-web/src/features/cameras/RetireCameraDialog.test.tsx`. Assert the **mock's call count**, not that the dialog closed. A confirmation that closes cleanly and retires anyway passes a close-assertion
- [ ] T013 [US1] **FR-004 — assert the retire control is ABSENT for a retired camera** in `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` — `toHaveCount(0)` / `queryBy…` returning null, **not** "clicking it fails". Mirrors how spec 030 FR-007 asserted the address-correction control. Add the counterpart asserting an active camera **does** offer one, or the first assertion passes against a page that renders nothing at all
- [ ] T014 [US1] **FR-012 — assert the page makes no claim of authorship** after a successful retirement, in `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`. Assert the **absence** of `/camera retired/i` and `/you retired/i` from the rendered output. A test checking "the state changed" passes while the page says something the app cannot support. **Do not delete this as pointless**: retiring is idempotent and answers `204` either way, so open the same camera in two tabs and retire in both — both succeed, and a page announcing *"Camera retired"* has told one of them something false. There is no toast infrastructure and **none is added**; the page state is the feedback
- [ ] T015 [US3] **FR-013 — compare the renderings**, in `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`. A camera in a fab the operator does not hold must render **identically** to one that never existed — now including the retire control, which is one more thing that could appear for one case and not the other. Compare the rendered output, not that both showed an error. *"No retire button, because this isn't yours"* and *"no retire button, because there is no camera"* must be indistinguishable, because a camera record carries its RTSP address

**Checkpoint**: US1, US2 and US3 are all shippable here. Phase 4 is the
end-to-end proof, not the feature.

---

## Phase 4: End to end, and the finding

- [ ] T016 [US1] Replace the explanatory comment in `e2e/camera-detail.spec.ts` — the one recording why a retired-camera test was absent — with the test it says becomes writable "once #1860 lands". Register a camera, open it, retire it through the confirmation, assert it is **gone from the listing**, then navigate back to its own address and assert it **still opens and is marked retired**. All three in one run (SC-005)
- [ ] T017 [US1] **No `fetch` to the API may appear in `e2e/camera-detail.spec.ts`.** Use the existing `signInAsOperator` and the local `registerCamera` helper; retiring is now something the app can do, so the app does it. Spec 030 **removed** a test rather than repair one that reached around the UI — a relative fetch resolving against the app origin with no bearer token — because repairing it would have produced a test exercising the API while claiming to exercise the application, hiding this very gap behind green. If arranging state seems to need a fetch, stop and reconsider rather than adding a token
- [ ] T018 **File the archive-confirmation inconsistency as a GitHub issue.** After this feature, cameras confirm before a terminal action and `RulesPage`, `OverlaysPage`, `LayoutsPage` and `SystemVariablesPage` still archive on a single click asking nothing. **A finding to raise, not to fix here.** Do **not** touch those four files, and do **not** drop the camera confirmation to match them — spec 028 made retirement irreversible by decision, and a one-click irreversible action is exactly what FR-002 exists to prevent
- [ ] T019 Full suite — `pnpm typecheck && pnpm lint && pnpm test`, then the Playwright run. Verification note on the PR following [quickstart.md](./quickstart.md), including §3 (nothing claims authorship) and §4 (the two renderings compared)

---

## Dependencies

```
T001 ──▶ T003 ──▶ T004, T005          (the primitive)
T002 ──▶ T003
                    │
T006 ──▶ T007, T008 │                  (the mutation — independent of T001–T005)
                    │
                    ▼
              T009 ──▶ T010
                    │
                    ├──▶ T011, T012     (the words)
                    └──▶ T013, T014, T015
                              │
                              ▼
                        T016 ──▶ T017 ──▶ T019
                        T018 (independent — file it any time)
```

**Phases 1 and 2 are independent of each other.** Both block Phase 3.

---

## Parallel opportunities

- **T002** with T001 — different files, and `Button.tsx` does not depend on the
  new package.
- **T004, T005** — same file, so one task's worth of work, but independent of
  anything in Phase 2.
- **The whole of Phase 2 (T006–T008)** with the whole of Phase 1.
- **T011, T012** with **T013, T014, T015** — different test files
  (`RetireCameraDialog.test.tsx` vs `CameraDetailPage.test.tsx`).
- **T018** — a GitHub issue, dependent on nothing.

---

## Implementation strategy

**MVP is Phase 3's checkpoint**, not Phase 4. Once T015 is done all three user
stories are shippable: an operator can retire a camera, is told what it costs,
and the security property still holds. T016–T017 prove it end to end and close
the gap spec 030 explicitly declined to fake — worth doing, but the feature is
real before them.

**Do Phase 2 first if you want the smaller risk out of the way.** T006–T008
mirror `changeCameraAddress` almost exactly. Phase 1 carries the only new
dependency and the only accessibility judgement.

**Do not start Phase 3 by writing the dialog's copy last.** The four sentences
in T011 are the feature's substance; a dialog built first and worded afterwards
tends to get worded to fit the layout.

---

## Three things most likely to go wrong

1. **The success message gets written.** There is no toast infrastructure, so
   adding one and saying *"Camera retired."* reads as polish. It is a claim the
   app cannot support — the endpoint answers `204` whether or not this operator
   caused the transition. T014 asserts the **absence** of that sentence, which
   looks like a strange thing to test until you retire the same camera from two
   tabs and watch both be congratulated.

2. **The control is disabled rather than absent.** Disabling is the smaller diff
   and feels more informative. For a terminal state it tells the operator an
   action is conceptually available when it is not, and it diverges from the
   address-correction control three lines above it. T013 asserts absence, and
   asserts the active-camera counterpart so it cannot pass vacuously.

3. **A helpful message undoes non-enumeration.** The refusal for another fab's
   camera is the same `404` as a camera that never existed, and any sentence
   distinguishing them turns a security property into an enumeration oracle.
   This regresses by *kindness*, which is why T015 compares renderings rather
   than asserting that both showed an error — and why it must be re-checked now
   that the page carries a control that could appear for one and not the other.
