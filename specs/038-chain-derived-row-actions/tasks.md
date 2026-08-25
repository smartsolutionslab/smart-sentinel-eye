# Tasks: A row offers the actions its chain actually supports

**Feature**: `038-chain-derived-row-actions` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1879 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**26 tasks across five phases.** Two components, no service change, and eight
chain shapes.

**The row stops asking about `newest` and starts asking about the chain.** One
descriptor — `{ live, draft, newest, summarised, fullyArchived }` — computed once
per row, and every action and every piece of row text read off it. The defect
being fixed is not a missing condition; it is the wrong question.

**Eight shapes, not five.** The spec enumerated five. Enumerating by
*construction* found `{A, D}`, `{D, D}` and `{P, D, D}` as well, because `Publish`
archives only the prior **Published** revision and drafts therefore accumulate.
**`{D, D}` — two open drafts, nothing published — is two clicks from a published
chain**, both offered by the row. Every shape and its reachability is in
[contracts/row-actions.md](./contracts/row-actions.md).

**Nothing to add**: no new dependency, no migration, no ADR, and **no service
change**. The service already accepts every action on every shape; only the app
stopped offering them. If a change under `src/` proves necessary that is a
**finding to raise, not absorb**.

**Do not suppress Edit while a draft is open.** FR-003 requires it stay offered,
even though that is the app's route to `{P, D, D}`. Research §8 records it as
observed, not fixed — quietly reversing a stated requirement during
implementation is worse than an extra button.

---

## Phase 1: The model

**Goal**: One place that knows which revision is live. Blocks everything.

- [ ] T001 Create `apps/management-web/src/features/chainView.ts` exporting a `chainView(revisions)` returning `{ live, draft, newest, summarised, fullyArchived }`, generic over a structural `{ revisionNumber, state }` and returning the caller's own revision type. **`live`** is the Published revision (at most one, enforced by the aggregate); **`draft`** is the **newest** Draft; **`newest`** is the highest-numbered revision; **`summarised`** is `live ?? draft ?? newest`; **`fullyArchived`** is `!live && !draft`. Document that `summarised` is deliberately separate from every action target — what a row *says about* a chain and what its buttons *do to* it are different questions, and collapsing them is a smaller version of the defect being fixed
- [ ] T002 Absorb spec 037's `isFullyArchived` in `apps/management-web/src/features/chainView.ts` as the `!live && !draft` field rather than leaving it beside the model (**FR-012**). It is not a special case: having neither a live revision nor a draft is *what being stranded means*. Delete the standalone helper from both pages in T008/T009
- [ ] T003 **The eight shapes, tested once**, in `apps/management-web/src/features/chainView.test.ts`: `{D}`, `{P}`, `{A}`, `{P,D}`, `{P,A}`, `{A,D}`, `{D,D}`, `{P,D,D}`. Assert each field per shape. **Testing them here rather than twice in the pages is the reason this is extracted** — the shape table gets one home instead of two that can drift
- [ ] T004 **`draft` is the *newest* draft**, asserted on the `{D,D}` chain in `apps/management-web/src/features/chainView.test.ts`. A model that assumes one draft passes every single-draft test, and `{D,D}` is reachable in two clicks from a published chain — branch, then revert, both offered by the row
- [ ] T005 **`summarised` follows `live` before `draft` before `newest`**, asserted on `{P,A}` (expects the *published* one) and `{A,D}` (expects the *draft*) in `apps/management-web/src/features/chainView.test.ts`. `{P,A}` is the filed defect's shape and the one where `summarised !== newest` matters most

**Checkpoint**: the model exists and no row uses it.

---

## Phase 2: The confirmation component

**Goal**: One dialog able to say two different verbs.

- [ ] T006 Add an optional `verb?: string` defaulting to `'Archive'` to `apps/management-web/src/features/ArchiveConfirmation.tsx`, used for **both** the title (`{verb} {subject}?`) and `confirmLabel`. **Not a misnomer creeping in** — both actions archive a revision server-side; the verb names it in the operator's terms. Renaming the component was considered and rejected as scope: it would reach into the rules and system-variable callers this feature does not touch. Leave `role="alertdialog"`, the cancel-focus default and the `pending` guard exactly as they are
- [ ] T007 Confirm the four existing callers still render identically with the default in `apps/management-web/src/features/rules/RulesPage.tsx`, `.../systemVariables/SystemVariablesPage.tsx`, `.../layouts/LayoutsPage.tsx` and `.../overlays/OverlaysPage.tsx`. This task is *verify and leave alone* — an optional prop with a default should touch nothing, and if any of the four needs editing that is a finding

**Checkpoint**: the dialog can say "Discard" and nothing does yet.

---

## Phase 3: The two rows

**Goal**: Actions that target the revision they act on.

Each shape's required behaviour is in
[contracts/row-actions.md](./contracts/row-actions.md).

- [ ] T008 [P] [US1] Replace `newest`-based gating with `chainView(chain.revisions)` in `apps/management-web/src/features/layouts/LayoutsPage.tsx`. Publish and Discard offered when `draft` exists; Edit when `live` exists **or** `fullyArchived`; Revert and Archive when `live` exists. Delete the local `isFullyArchived`
- [ ] T009 [P] [US1] The same in `apps/management-web/src/features/overlays/OverlaysPage.tsx`
- [ ] T010 [P] [US1] **Targets** in `apps/management-web/src/features/layouts/LayoutsPage.tsx`: Publish and Discard send `draft.revisionNumber`; Revert and Archive send `live.revisionNumber`; Edit branches and opens from `live ?? newest`. **Today Archive sends `newest`, which is the bug** — and it succeeds either way, which is why nobody noticed
- [ ] T011 [P] [US1] The same in `apps/management-web/src/features/overlays/OverlaysPage.tsx`. Its Edit branches without opening a designer, which is the twins' one difference and is not introduced here
- [ ] T012 [US2] **Delete the `published` flag** from the pending-archive state in both `apps/management-web/src/features/layouts/LayoutsPage.tsx` and `.../overlays/OverlaysPage.tsx`, and render the kiosk sentence unconditionally in the archive confirmation. Once Archive is offered only when a live revision exists and targets it, the flag is true every time the dialog opens. **A flag that is always true is worse than no flag** — it reads as a live condition and invites a future caller to pass `false`. Spec 036 FR-008 is then satisfied structurally: the confirmation that does not apply to kiosks is a *different* confirmation
- [ ] T013 [US2] Add the **Discard draft** action and its own pending-subject state to both `apps/management-web/src/features/layouts/LayoutsPage.tsx` and `.../overlays/OverlaysPage.tsx`, rendering a second `ArchiveConfirmation` with `verb="Discard"`. Nullable pending subject, not a boolean — each page now holds three open-states and a nullable subject makes them impossible to confuse
- [ ] T014 [P] [US3] **The badge** in `apps/management-web/src/features/layouts/LayoutsPage.tsx`: `v{live} · Published`, `v{live} · Published · draft v{draft}`, `v{draft} · Draft`, or `v{newest} · Archived`. **`Published` must appear exactly when a live revision exists** — two e2e assertions read this text (T023)
- [ ] T015 [P] [US3] The same badge in `apps/management-web/src/features/overlays/OverlaysPage.tsx`
- [ ] T016 [P] [US3] Point the row's summary at `summarised` — `tileSummary` in `apps/management-web/src/features/layouts/LayoutsPage.tsx` and the label preview in `.../overlays/OverlaysPage.tsx`. Same argument as the badge: a row that describes a discarded draft while a wall is live on kiosks is describing the wrong thing

**Checkpoint**: every shape offers something, and each button acts on the right revision.

---

## Phase 4: The words

**Goal**: Two confirmations that cannot be mistaken for one another.

Both bodies are given **verbatim** in
[contracts/row-confirmations.md](./contracts/row-confirmations.md). The whole
point is that they must not sound alike.

- [ ] T017 [P] [US2] The **discard** body in `apps/management-web/src/features/layouts/LayoutsPage.tsx`: the draft is thrown away and the work in it cannot be recovered; and — **only when the chain has a live revision** — that the layout stays as it is, naming the live revision. When there is no live revision the second paragraph is **omitted**: discarding the only draft of a never-published layout leaves nothing live, and reassuring the operator otherwise is the same falsehood this feature removes
- [ ] T018 [P] [US2] The same in `apps/management-web/src/features/overlays/OverlaysPage.tsx`
- [ ] T019 [US2] Confirm the **archive** bodies are unchanged from spec 037 in both pages — *out of service*, *bring it back by editing it*, *the tiles are kept* / *the label is kept*, plus the kiosk sentence now unconditional. This task is *verify and leave alone*: the archive wording was settled two specs ago and only its trigger changed

**Checkpoint**: the two dialogs say different things about different revisions.

---

## Phase 5: Evidence

- [ ] T020 [US1] **Every shape offers at least one action, asserted SHAPE BY SHAPE**, in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`. All eight, each its own assertion. **Not in aggregate** — the defect being fixed is precisely a shape nobody enumerated, and a single "every shape has a button" loop over a fixture list repeats that method
- [ ] T021 [US1] **Archive and Discard target different revisions, asserted ON THE SAME CHAIN**, in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`. A `{P,D}` chain: Archive sends the **live** revision's number, Discard sends the **draft's**. Both mutations succeed either way, so *"the request fired"* asserts nothing. **On the same chain deliberately** — asserted separately, a swap passes twice
- [ ] T022 [US1] **Rewrite the two tests that cannot pass**, in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`. Both are spec 037's *"Does not treat a published revision under an abandoned draft as recoverable"*, asserting that shape offers **no** edit button. It now does. Both carry a comment saying issue 1879 covers it, so the change was foreseen. **Rewrite them; do not delete them.** What they exist to prevent is branching from the *abandoned draft* instead of the published wall — asserting a button's absence was only a proxy for that while the button did not exist. The rewrite asserts the **stronger** form: editing a `{P,A}` chain opens the editor from the **published** revision's grid and tiles. Assert the payload the editor receives, not that the branch mutation fired — branching from the abandoned draft fires it too
- [ ] T023 [US3] **The row's text**, in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`: the badge names the **live** revision's number on a `{P,A}` chain. Assert the **number**, not the word *Published* — *Published* appears either way. Then confirm `e2e/layouts.spec.ts:56` and `:130` still match: both assert `/Published/` on a row that is shape `{P}` at that moment, whose badge is unchanged. **Confirm it; do not assume it**
- [ ] T024 [US2] **The confirmations**, in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`:
  - The **discard** dialog does **not** contain *out of service*, does **not** mention kiosks, and does **not** offer to bring anything back. Assert the **absence** of each — a dialog that says both passes any assertion about the new sentence, and the copied-across archive body is exactly how that happens.
  - The **archive** dialog **does** mention kiosks, with **no fixture varying a draft state**. It is unconditional now, and a test that still varies a fixture would pass while the flag lingered.
  - Both name their revision number and the two **differ**.
  - Dismissing either calls its mutation **zero** times (spec 036 FR-002, now for two dialogs rather than one)
- [ ] T025 **The deliberate break, then full verification.** Swap the Archive and Discard targets in `apps/management-web/src/features/layouts/LayoutsPage.tsx` and `.../overlays/OverlaysPage.tsx` so each sends the other's revision number. Run the tests, record **which** assertions go red and **how many**, then revert. Both requests still succeed under the swap, so **if fewer than both pages' target assertions fail, the targets are not really being checked**. Same discipline as spec 031 T010, 033 T006, 034 T012, 035 T012, 036 T018 and 037 T026(b) — an assertion that has never failed is a claim, not a check
- [ ] T026 **No service change, proved.** `git diff origin/develop -- src/` must be **empty** (**FR-013**, **SC-006**). Show it rather than asserting it. Anything under `src/` changing is a **finding to raise, not a fix to keep** — the service already accepts every action on every shape, and needing to touch it would mean the diagnosis was wrong. Then `pnpm typecheck && pnpm lint && pnpm test` with counts, and the Playwright run with its count. Verification note on the PR per [quickstart.md](./quickstart.md), covering **both rows** for every behavioural claim (SC-007)

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003, T004, T005          (the model, tested once)
  │
  ▼
T006 ─▶ T007                               (the verb)
  │
  ├──▶ T008 ─▶ T010 ─┐                     (layouts row)
  ├──▶ T009 ─▶ T011 ─┤                     (overlays row)
  │                   ▼
  │            T012, T013                  (the flag, the new action)
  │                   │
  │            T014, T015, T016            (the row's text)
  │                   │
  │            T017, T018 ─▶ T019          (the words)
  │                   │
  └───────────────────┴──▶ T020, T021, T022, T023, T024
                                   │
                                   ▼
                                T025 ─▶ T026
```

**Phase 1 blocks everything.** Both pages read the descriptor, and building a row
against a model that does not exist yet means building it against `newest` again.

**T013 needs T006** — the discard dialog has no verb to say otherwise.

**T012 and T013 are one thought split in two**: the flag goes away *because* the
second confirmation exists. Doing T012 alone leaves the archive dialog warning
about kiosks on a chain that has no live revision, which is briefly worse than
today.

---

## Parallel opportunities

- **The two pages, at every step.** T008/T009, T010/T011, T014/T015, T017/T018 —
  different files, no shared state. This is the most parallel work in the feature
  and it exists because the model was extracted first.
- **T003, T004, T005** — same file, so one author, but independent of everything
  in Phases 2–4.
- **T016** touches both pages and is independent of the badge tasks it sits
  beside.
- **T020–T024** — different assertions over the same two test files; one author
  per file.
- **T007 and T019** are both *verify and leave alone*, need no code, and can run
  at any point after their subject lands.

---

## Implementation strategy

**MVP is T013.** Once both rows read the chain, target the right revision and
offer a separate discard, SC-001 and SC-002 are both met and the two defects are
gone. Phases 4 and 5 make it legible and prove it.

**Build the model first and test it first.** T003–T005 before any page changes.
The eight shapes are the substance of this feature, and a page built before they
are pinned down is a page built against an assumption.

**Do the pages in lockstep, not one then the other.** They are twins; finishing
one and starting the other invites the second to be "the same but simpler", which
is how they drift.

**Do T022 last among the tests, and read it twice.** It is the only place
existing tests are rewritten, and the tempting change — deleting the assertion
that no longer holds — removes the check at the moment its subject changed.

---

## Three things most likely to go wrong

1. **The descriptor is written assuming one draft.** `draft` reads naturally as
   *the* draft, and every single-draft fixture passes. `{D,D}` is two clicks from
   a published chain — branch, then revert — so the assumption is wrong in
   practice, not just in principle. T004 asserts it directly on that shape, and
   the field is documented as *the newest* draft rather than *the* draft.

2. **Archive and Discard get wired to the wrong revisions.** They call the same
   mutation with a different number, and **both succeed**. This is exactly the
   defect being fixed, so shipping it again is entirely possible. T021 asserts
   both on **one** chain so a swap fails rather than passing twice, and T025
   proves those assertions fire by performing the swap deliberately.

3. **The discard confirmation inherits the archive wording.** It is the same
   component with different children, and copying the block is the fast way to
   build it. The copied sentence claims the layout goes out of service — the
   exact falsehood this feature exists to remove, reintroduced by convenience.
   The contract gives both bodies verbatim, and T024 asserts the **absence** of
   that sentence rather than only the presence of the new one.
