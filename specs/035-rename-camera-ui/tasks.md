# Tasks: Rename a camera from the management app

**Feature**: `035-rename-camera-ui` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1873 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**16 tasks across four phases.** A mutation, a dialog, a button, and a test.

**Most of the machinery exists** and the plan says so. Two places are not
mechanical, and they take seven of the sixteen tasks between them: a rename is
refused **three** ways where the address correction had two, and the typed name
must reach the server unaltered.

**Nothing to add**: no new dependency, no migration, no backend change. **No new
shared predicate** in `problemDetail.ts` (research §1) and **no shared
edit-dialog** (research §2) — both were considered and rejected with what would
justify them later.

---

## Phase 1: The mutation and the schema

**Goal**: The endpoint becomes callable, and the name rule stays single.

- [x] T001 [US1] Add a `renameCamera` mutation to `apps/shared/src/api/cameras.api.ts`, mirroring `changeCameraAddress`: `PATCH` to `/${cameraIdentifier}`, body `{ name }`, `If-Match` via the existing `ifMatch(version)` helper, `fabId` threaded as the other camera endpoints thread it
- [x] T002 [US1] In the same mutation in `apps/shared/src/api/cameras.api.ts`, invalidate `{ type: 'Camera', id: cameraIdentifier }` **and** `{ type: 'Camera', id: 'LIST' }` — the detail page refetches into its new name (FR-012) and the listing row follows. Identical to `changeCameraAddress`
- [x] T003 [P] [US1] Add `renameCameraSchema = registerCameraSchema.pick({ name: true })` to `apps/shared/src/api/cameras.schema.ts`. **Derive, never restate** — that file already establishes the pattern for the address and records why: the rule for a usable name must not be able to differ between registering a camera and correcting one. **Add no case normalisation**: `.trim()` is already there and is permitted, lower-casing is not
- [x] T004 [P] [US1] Correct the stale doc comment in `apps/shared/src/api/cameras.schema.ts` that reads *"No `name`: it is not editable (spec 029 FR-012, tracked as #1850), so there is nothing for a correction to carry."* Spec 033 made it editable; left alone this tells the next reader the opposite of the truth

**Checkpoint**: the endpoint is callable and the name rule has one definition.

---

## Phase 2: The dialog

**Goal**: A form that pre-fills, sends what was typed, and keeps it on refusal.

**Mirrored from `EditCameraAddressDialog`, not extracted.** Research §2 records
why: what looks shared is a *shape*, not a *behaviour* — the two differ in field,
schema, mutation, and refusal branching. Revisit at a third caller.

- [x] T005 [US1] Create `apps/management-web/src/features/cameras/RenameCameraDialog.tsx` mirroring `EditCameraAddressDialog.tsx`: React Hook Form + Zod against `renameCameraSchema` (ADR-0079), `useRenameCameraMutation`, the version threaded from the caller, and the shared `Dialog` primitive
- [x] T006 [US1] Pre-fill the field with the camera's current name in `apps/management-web/src/features/cameras/RenameCameraDialog.tsx` (**FR-003**). A correction is an edit, not a retype — a blank field makes the operator reconstruct the thing they are fixing before they can fix it
- [x] T007 [P] [US1] **FR-010 — assert the name is sent exactly as typed**, in `apps/management-web/src/features/cameras/RenameCameraDialog.test.tsx`. Assert on the **mutation mock's argument**, not on form state. Two assertions: an ordinary rename, and a **case-only** one — `Line-4-Inlet` → `line-4-inlet`. That second is a real change to what an operator reads that normalises identically, and spec 033 found the same trap in the repository predicate, the aggregate's guard **and** EF's change tracker. A client that lower-cased before sending would be the fourth, and the symptom is a rename that reports success and changes nothing
- [x] T008 [P] [US1] **FR-011 — assert the operator's typing survives a refusal**, in `apps/management-web/src/features/cameras/RenameCameraDialog.test.tsx`. A refused rename must not cost them their input

**Checkpoint**: the dialog works for the happy path.

---

## Phase 3: Three refusals, three answers

**Goal**: Each refusal names its own remedy and neither of the other two.

This is why the feature is not purely mechanical. `EditCameraAddressDialog`
distinguishes two; a rename produces three.

- [x] T009 [US2] Implement the refusal branching in `apps/management-web/src/features/cameras/RenameCameraDialog.tsx`: **taken** → the server's own detail **plus** an action clause; **stale** → the existing shared lost-update wording; **retired** → the existing shared terminal wording. Recognise the taken code **at the call site** with `problemCode(error) === 'CAMERA_NAME_TAKEN'`, following `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx`. **No fourth predicate in `problemDetail.ts`** — one call site does not earn a shared helper, and research §1 records what would change that
- [x] T010 [US2] **The three refusals — three assertions on rendered text, in one task** in `apps/management-web/src/features/cameras/RenameCameraDialog.test.tsx`. (a) A **taken** name names the conflicting name and fab **and says to choose a different one**. (b) A **stale** version says *reload* and does **not** say to choose a different name. (c) A **retired** camera says it is retired and cannot be changed. They are one task deliberately: asserting "an error appeared" passes while two of the three say the wrong thing, and splitting them invites two being dropped as repetitive
- [x] T011 [US2] **Assert a taken name does NOT inherit the lost-update wording**, in `apps/management-web/src/features/cameras/RenameCameraDialog.test.tsx` — the rendered text must not contain *"reload"* or *"someone else changed"*. Both refusals are **409**, and the danger is not the shared helper (`isStaleConflict` already correctly returns false for a taken name) but the **branching order at the call site**. Wrong in both halves: nobody changed the camera, and reloading will not release the name
- [x] T012 [US2] **Prove the branching fires.** Temporarily make the taken-name branch fall through to the stale wording in `apps/management-web/src/features/cameras/RenameCameraDialog.tsx`, run `apps/management-web`'s tests, watch T010(a) and T011 go red, then revert. Same discipline as spec 031 T010, spec 033 T006 and spec 034 T012: an assertion that has never failed is a claim, not a check

**Checkpoint**: US1 and US2 are shippable. Phase 4 is the control and the proof.

---

## Phase 4: The control, and proof

- [x] T013 [US1] Add the rename control to `apps/management-web/src/features/cameras/CameraDetailPage.tsx`, gated on `retired` exactly as the two controls beside it are. Order the header **Rename · Correct the address · Retire camera · Back to cameras** — destructive last before the link. Wire `RenameCameraDialog` alongside the two existing dialogs
- [x] T014 [US3] **FR-009 — assert the rename control is ABSENT for a retired camera** in `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` — `queryBy…` returning null, **not** "clicking it fails". Add the counterpart asserting an **active** camera does offer it, or the absence assertion passes against a page that renders no controls at all
- [x] T015 [US3] **FR-013 — compare the renderings** in `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`. A camera in a fab the operator does not hold must render **identically** to one that never existed — now including the rename control, which is one more thing that could appear for one cause and not the other. Compare the rendered output, not that both showed an error
- [x] T016 [US1] Extend `e2e/camera-detail.spec.ts` with a rename test: register a camera, open it, rename it, and assert the new name in the heading **and** in the listing (SC-005). Use the existing `registerCamera` helper and `signInAsOperator`. **No `fetch` to the API may appear** — spec 030 *removed* a test that reached around the app to arrange state, because repairing it would have produced a test exercising the API while claiming to exercise the application, and spec 032's retire test was written after that lesson. Then run the full frontend suite and the Playwright run, and put the verification note on the PR following [quickstart.md](./quickstart.md)

---

## Dependencies

```
T001 ─▶ T002            (the mutation)
T003 ─▶ T004            (the schema — independent of the mutation)
   │       │
   └───┬───┘
       ▼
     T005 ─▶ T006 ─▶ T007, T008
       │
       ▼
     T009 ─▶ T010, T011 ─▶ T012
       │
       ▼
     T013 ─▶ T014, T015
       │
       ▼
     T016
```

**T012 needs T010 and T011**, because it proves those specific assertions fire.

---

## Parallel opportunities

- **T003, T004** with **T001, T002** — different files, and the schema does not
  depend on the mutation.
- **T007, T008** — same test file, independent assertions.
- **T014, T015** with **T016** — `CameraDetailPage.test.tsx` vs
  `e2e/camera-detail.spec.ts`.

The chain is mostly linear: a mutation, then a dialog, then its wording, then a
button. Saying so is more useful than inventing parallelism.

---

## Implementation strategy

**MVP is T013.** After the control is wired an operator can correct a misnamed
camera from the app, which is the whole user-visible change. T014–T016 are proof.

**Do Phase 3 before Phase 4.** The refusal branching is the risky part and the
button is the easy part; wiring the button first makes it tempting to call the
feature done and treat the wording as polish.

**Do not start with the e2e test.** It is the slowest feedback loop in the
feature and proves the least about the part most likely to be wrong.

---

## Three things most likely to go wrong

1. **A taken name is reported as a lost update.** Both are `409`. The shared
   helper is already right — `isStaleConflict` returns false for a taken name —
   so the failure is in the *branching order* at the call site, where an
   `isConflict`-shaped check would hand it *"someone else changed this, reload to
   see their version."* Wrong in both halves: nobody changed the camera, and
   reloading will not release the name. T010 and T011 assert it; T012 proves they
   fire.

2. **The client normalises the name before sending.** `.trim()` is already in the
   schema and is fine. Lower-casing is not, and it is exactly what gets added in
   passing to "match the server's uniqueness rule". A case-only correction is a
   real change that normalises identically — spec 033 found that trap in three
   separate layers and this would be the fourth. The symptom is a rename that
   reports success and changes nothing, so T007 asserts on the mutation's
   argument rather than on anything the UI shows.

3. **The control is disabled rather than absent.** Disabling is the smaller diff
   and looks more informative. For a terminal state it says an action is
   conceptually available when it is not, and it diverges from the two controls
   beside it. T014 asserts absence **and** the active-camera counterpart, so it
   cannot pass against a page that renders nothing.
