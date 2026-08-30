# Tasks — 048 the camera picker finds every camera

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)

Thirteen tasks. A small, frontend-only change — the length of this file should
not be mistaken for the size of the work.

---

## Do not

- **Do not change the overlay picker** (FR-012). It sits beside the camera
  picker in the same dialog and it is *not* paginated — the endpoint returns
  every overlay. Changing it would be a change without evidence. This was
  checked, not assumed.
- **Do not widen `FormField`.** Rejected in research R5: it is a shared
  composite used by both apps, and adding a description slot for one caller is a
  change to everything to serve one screen.
- **Do not replace the native `<select>` with a combobox.** That is the deferred
  search story. It needs a name filter the camera source does not have *and* a
  primitive **Radix does not ship** — a Select is not a combobox and does not
  filter.
- **Do not touch `listCameras`.** `CamerasPage` uses it correctly for its own
  paging. The new endpoint goes *alongside* it.
- **Do not write any C#.** Research R7: the server already pages, counts, sorts
  by name, and scopes to the caller's fabs before counting. If you find yourself
  needing a server change, **the scope was misjudged** — stop and take it back
  through the gate rather than absorbing it.
- **Do not write bare `#NNNN` issue numbers** in committed docs. The automation
  closes merely-mentioned issues on merge. Write "issue NNNN".

---

## Phase 1 — The paging, where its arithmetic can be tested

- [x] T001 Add `listAllCameraChoices` to `apps/shared/src/api/cameras.api.ts` as a `queryFn` that pages internally and returns one result: `{ items, count, complete }`. Request `sort=name`, `order=asc`, `limit=200`, walking `offset`. **`complete` is carried, not derived** — the producer knows *why* it stopped and a consumer recomputing `items.length < count` re-implements a decision it cannot see (data-model.md).
- [x] T002 Bound the loop at **5 pages / 1000 cameras** (research R3), and stop early when a page returns fewer rows than requested. Four times the constitution's 250-camera target, so the target is met with room while an unbounded loop — 50 sequential requests on a 10,000-camera fab, behind an open dialog — cannot happen.
- [x] T003 De-duplicate by `cameraIdentifier` when concatenating pages (research R4). Offset paging over a list someone else is editing can deliver a boundary camera twice; in a `<select>` that is two identical options and a duplicate React key.
- [x] T004 [P] `apps/shared/src/api/cameras.api.test.ts` — the arithmetic, directly. **Every fixture exceeds one page before anything is asserted**: a 10-camera fixture passes with the whole feature deleted, which is the trap spec 045 hit with an already-aligned wall and spec 046 hit again with text seeded at mount. Cover: 250 across two pages returns all 250; a camera duplicated at the offset-200 boundary appears once; 1200 cameras yield exactly 5 requests and `complete: false`; `count` is the source's total, not `items.length`.
- [x] T005 [P] **Mutation-test T004 before trusting it.** Each of these must kill at least one test, and any that does not means the test is decoration: *stop after the first page*; *drop the de-duplication*; *remove the bound*; *derive `complete` from `items.length`*. Record which test each kills.

**Checkpoint**: the paging is correct and proven where a boundary bug is visible. Nothing user-facing yet.

---

## Phase 2 — US1: the picker stops being silent *(P1 — ships alone)*

- [ ] T006 [US1] Render the truncation notice in `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx` when `complete` is false, stating **both** numbers — how many are shown and how many exist. Vague wording ("some cameras may not be shown") carries no information and teaches operators to ignore it.
- [ ] T007 [US1] Associate the notice with every camera `<select>` in `GridDesigner.tsx` via `aria-describedby` (research R5). **One notice for the dialog, not one per tile** — the list is fetched once and shared, and twelve copies is noise on screen and considerably worse through a screen reader.
- [ ] T008 [US1] Distinguish the three empty states in `GridDesigner.tsx` (FR-003): **"this fab has no cameras"**, **"the camera list could not be retrieved"**, and **"still loading"**. Today the first two render identically as an empty dropdown, and an operator who cannot tell them apart goes looking for the wrong problem — the same class of defect as the truncation itself, a state rendered as a more innocent state.
- [ ] T009 [US1] Tests in `apps/management-web/src/features/layouts/`: the notice appears with both numbers on a truncated list; **the notice is absent on a complete list**; the `aria-describedby` association exists — asserted, not eyeballed, because a notice that is painted but not announced satisfies a screenshot and not a screen-reader user; each of the three empty states renders distinguishably.
- [ ] T010 [US1] **Mutation-test T009**: *render the notice unconditionally* must fail the absence test. A notice that is always there says nothing, so its absence is the assertion that gives it meaning.

**Checkpoint**: **US1 is shippable on its own, and should ship even if US2 does not.** It does not raise the count by one camera — it ends the deception. An operator who knows the list is incomplete can go and ask someone; an operator who does not know cannot. That is the whole harm this feature was raised for.

---

## Phase 3 — US2: every camera is reachable *(P2)*

- [ ] T011 [US2] Replace `useListCamerasQuery({ limit: 50 })` with the paging endpoint in `LayoutEditorDialog.tsx`. `cameras.map(...)` in `GridDesigner` **keeps its shape** — FR-011 (a selection surviving a refresh) is protected most cheaply by not changing what the field renders.
- [ ] T012 [US2] Test through the dialog with a **two-page fixture**: the alphabetically last camera is present and selectable, and a selection already made survives the list being extended (FR-011). This complements T004 rather than repeating it — a component test proves the total is right without proving *which* camera was dropped at a boundary.

**Checkpoint**: complete to 1000, honest beyond it.

---

## Phase 4 — Verify

- [ ] T013 Run the frontend suites **the way CI runs them, not a subset** — `format:check`, then `lint`, `typecheck` and `test` across all three app packages. Spec 045 shipped a green subset and CI caught a test never run locally. Then write `verification.md` stating plainly **what could not be done**: every check here runs against a fixture, and none against a real fab of 250 cameras. If a populated fab is unavailable, say so — do not narrow the claim to what the fixtures happened to cover. Spec 046's verification note is the model, and the reason: it shipped a defect that all sixteen of its mutations missed.

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003 ─▶ T004 ─▶ T005          Phase 1
                          │
              ┌───────────┴───────────┐
              ▼                       ▼
   T006 ─▶ T007 ─▶ T008 ─▶ T009 ─▶ T010    US1  (ships alone)
              │                       
              └──────────▶ T011 ─▶ T012     US2
                                     │
                                     ▼
                                   T013
```

**US1 does not depend on US2.** It depends only on the endpoint existing, so it
can land first and alone.

---

## Parallel opportunities

- **T004 and T005** are the same file and are sequential in practice, but they
  are independent of anything in Phase 2 — the paging tests can be written while
  the notice is being built.
- **T006–T008** touch two files (`LayoutEditorDialog.tsx`, `GridDesigner.tsx`)
  and are one mechanism threaded through both. **Not parallel.**
- **Phase 2 and Phase 3 are parallel after T004**, and deliberately so: US1 is
  the shippable half and must not wait on US2.

---

## Implementation strategy

**Ship US1 first and separately.** It is the half that removes the harm, and it
is landable without a single extra camera being reachable. US2 raises the count;
US1 stops the lying. If review or CI stalls US2, US1 must not be stuck behind
it.

**Phase 3's gate is already satisfied.** The feature issue is on Project #13 and
was verified there — 457 items on the board, and it is among them. Nothing to
add; `/speckit-tasks` adds nothing to the board on its own.

**No C# means no coverage gate.** The ADR-0065 thresholds cover Domain,
Application and Shared assemblies, none of which are touched. That is not a
reason to test less; it is a reason not to claim the gate as evidence.

---

## Three things most likely to go wrong

1. **An off-by-one at a page boundary, hidden by a test that only counts.**
   Offsets, a bound, de-duplication and a truncation flag all interact. A test
   asserting "250 options rendered" passes while the camera at offset 200 is
   the wrong one. T004 exists to test the arithmetic where the boundary is
   visible, and T005 exists because the test itself needs proving.

2. **The notice becomes decoration.** If it renders whenever the list loads, or
   says something vague, operators learn to ignore it and the feature has
   achieved nothing while looking finished. The absence test (T009) and its
   mutation (T010) are what stop this, which is why they are tasks rather than
   nice-to-haves.

3. **A fixture that never exceeds one page.** Every assertion about completeness
   is vacuous below 200 cameras. Spec 045 wrote this lesson down and *still*
   shipped five vacuous tests; spec 046 wrote it down again and shipped a defect
   every one of its sixteen mutations missed. The pattern is not carelessness —
   it is that the passing state and the broken state look identical until
   something induces the condition.

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| All 250 cameras are returned across two pages | T004 | any single-page fixture |
| A boundary duplicate appears once | T004 + T005 | a test that counts options |
| The bound is real and stops at 5 requests | T004 + T005 | reading the constant |
| `complete` reflects the producer's reason for stopping | T004 + T005 | `items.length < count` agreeing today |
| The notice states both numbers when truncated | T009 | — |
| **The notice is absent when complete** | T009 + T010 | assuming it is conditional |
| The notice is announced, not merely painted | T009 (`aria-describedby`) | a screenshot |
| The three empty states are distinguishable | T009 | — |
| A selection survives the list being extended | T012 | — |
| **That a 250-option dropdown is usable** | **nothing** | **every test above** |
| **That two round trips feel acceptable to an operator** | **nothing** | **every test above** |
| **That 1000 is the right bound** | **nothing — it was chosen, not measured** | — |

The last three rows are the honest ones. Every check runs against a fixture, so
the arithmetic is provable and the *experience* is not. **The blind spot here is
scale**, and it is named in advance rather than discovered in review.
