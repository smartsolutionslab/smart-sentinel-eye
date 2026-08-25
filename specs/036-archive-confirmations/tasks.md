# Tasks: Archiving asks before it happens

**Feature**: `036-archive-confirmations` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1866 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**19 tasks across four phases.** One component, four call sites, and the words.

**The words are the feature.** The behaviour already exists — spec 032 built
`ConfirmDialog` shared precisely so this second destructive action would copy it
— so what is genuinely being decided here is what four confirmations *say*.
Eleven of the nineteen tasks are about that.

**Nothing to add**: no backend change, no new dependency, no migration.
`ConfirmDialog` and `Button`'s `danger` variant both already exist. If a backend
change proves necessary that is a **finding to raise, not absorb**.

**This does not wait for issue 1877** (the stranding fix). The confirmation is
worth having whichever way that resolves; only two of the four sentences would
change.

---

## Phase 1: The component

**Goal**: One confirmation, four callers.

- [x] T001 [US1] Create `apps/management-web/src/features/ArchiveConfirmation.tsx`, wrapping `apps/shared/src/ui/primitives/ConfirmDialog.tsx`. Props: the subject's display name, the consequence lines to render, a `pending` flag and an `onConfirm` callback. **One component for four callers, not four dialogs** — the inverse of spec 035's call and for the inverse reason: there two dialogs shared a *shape* while their behaviours differed; here the behaviour is identical in all four and only the words differ, and words are props
- [x] T002 [US1] Pass `pending` straight through to `ConfirmDialog` in `apps/management-web/src/features/ArchiveConfirmation.tsx` so the underlying no-double-submit guard applies to all four callers without any of them re-implementing it (**FR-011**). The primitive already blocks the second click; this must not shadow it with its own flag

**Checkpoint**: a confirmation exists with no caller.

---

## Phase 2: Four sets of words

**Goal**: Four confirmations that say four different true things.

Not one task. The consequences genuinely differ, and collapsing them is how they
converge on one sentence that is true of everything and useful for nothing. Each
is specified in [contracts/archive-confirmations.md](./contracts/archive-confirmations.md).

- [x] T003 [P] [US2] The **rules** wording in `apps/management-web/src/features/rules/RulesPage.tsx`: names the rule, says it cannot be undone, and says the rule cannot be published again — a replacement means **cloning** it into a new rule with its own history. Taken from `Rule`'s own documentation. **Do not claim evaluation stops**: plausible, unverified, so unsaid
- [x] T004 [P] [US2] The **variables** wording in `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx`: names the variable, says it cannot be undone, and says its **current value is cleared** and it can never be given another. Both halves matter and neither is visible from the page — verified from `Variable.Archive` setting `Value = Unset` and `SetValue` refusing afterwards
- [x] T005 [P] [US2] The **layouts** wording in `apps/management-web/src/features/layouts/LayoutsPage.tsx`: names the layout **and the revision number**, and says the layout **can never be edited or published again**. **FR-007 forbids softening that to "cannot be undone"** — that phrase is true of all four and understates this one: the layout does not merely stay archived, it becomes unusable
- [x] T006 [P] [US2] The **overlays** wording in `apps/management-web/src/features/overlays/OverlaysPage.tsx`, same structure and same prohibition as T005 — `Overlay` exposes the same six behaviours with the same guards
- [x] T007 [US3] The **kiosk sentence, conditional on `Published`**, in `apps/management-web/src/features/layouts/LayoutsPage.tsx` and `apps/management-web/src/features/overlays/OverlaysPage.tsx`. `newest.state` is available at the call site. A layout's says kiosks showing it are sent away immediately; an overlay's says kiosks using it stop showing it. **A draft's confirmation must not mention kiosks at all** — archiving a draft strands nothing and disturbs no kiosk, and a warning that fires either way is one operators learn to click through

**Checkpoint**: four confirmations, each saying something the others do not.

---

## Phase 3: The four call sites

**Goal**: Archive asks instead of doing.

Each page changes from *archive on click* to *ask on click, archive on confirm*.

**State is the pending subject, nullable — not a boolean.** Every one of these
pages already holds another open-state (an editor dialog, a dry-run panel, a
set-value form), and a nullable subject makes two open-states impossible to
confuse while carrying the data the wording needs. `RulesPage` already uses this
idiom for its dry-run panel.

- [x] T008 [P] [US1] Wire `ArchiveConfirmation` into `apps/management-web/src/features/rules/RulesPage.tsx` — the Archive button sets the pending rule; confirming calls `archiveRule` with exactly the arguments it passes today
- [x] T009 [P] [US1] Same in `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx` for `archiveVariable`
- [x] T010 [P] [US1] Same in `apps/management-web/src/features/layouts/LayoutsPage.tsx` for `archiveRevision`, carrying `newest.revisionNumber` and `newest.state` into the pending subject so T007's conditional has what it needs
- [x] T011 [P] [US1] Same in `apps/management-web/src/features/overlays/OverlaysPage.tsx` for `archiveOverlayRevision`

**Checkpoint**: no archive in the app happens on one click.

---

## Phase 4: Evidence

- [x] T012 [US1] **Dismiss archives nothing — all four**, asserted as the mock's **call count being zero**, in `apps/management-web/src/features/rules/RulesPage.test.tsx`, `.../systemVariables/SystemVariablesPage.test.tsx`, `.../layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`. Assert the count, not that the dialog closed: a confirmation that closes cleanly and archives anyway passes any assertion about closing
- [x] T013 [P] [US2] **Each confirmation names its subject**, in `apps/management-web/src/features/rules/RulesPage.test.tsx`, `.../systemVariables/SystemVariablesPage.test.tsx`, `.../layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`. Assert the rendered text contains the rule / variable / layout / overlay **name**, and for layouts and overlays the **revision number** — and assert it does **not** contain the identifier. *"Archive revision 4 of 0192f3c1-…"* is not a confirmation, and *"Are you sure?"* is the phrase this feature exists to replace
- [x] T014 [P] [US2] **The sentence that must not be softened**, in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`: assert the rendered text says the layout or overlay **can never be edited or published again**. Assert that specific claim, not merely that *"cannot be undone"* appears — that phrase is true of all four and is exactly what this one gets softened into
- [x] T015 [US3] **The conditional, both directions, in one task** in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `.../overlays/OverlaysPage.test.tsx`: a **Published** revision's confirmation mentions kiosks, and a **Draft** revision's does **not**. One task deliberately — asserting only the published case passes against a confirmation that always warns, which is the failure this requirement exists to prevent
- [x] T016 [P] [US1] **No double submit**, in `apps/management-web/src/features/rules/RulesPage.test.tsx`: confirming twice while the request is in flight calls the mutation **exactly once**. One page is enough because T002 routes all four through the same guard — and T012 covers that they all use it
- [x] T017 [US1] **Update the one test that cannot pass unchanged.** `apps/management-web/src/features/rules/RulesPage.test.tsx` has *"Archives a rule from its row action, naming the rule's fab"*, which clicks Archive and asserts `expect(archiveMock).toHaveBeenCalledWith({ name: 'high-oee', version: 0, fabId: 'munich' })`. After this feature that click asks a question and sends nothing. **Add the confirmation step and KEEP the assertion.** Do **not** delete it to make the test green — that would remove the only check that archiving still sends the right request, at exactly the moment the path to it changed. Kept, it proves both that the confirmation is required and that confirming sends precisely what it sent before
- [x] T018 [US2] **Prove the wording assertions fire.** Temporarily soften the layout confirmation in `apps/management-web/src/features/layouts/LayoutsPage.tsx` to just *"This cannot be undone."*, run `apps/management-web`'s tests, watch T014's layout assertion go red, then revert. Same discipline as spec 031 T010, spec 033 T006, spec 034 T012 and spec 035 T012: an assertion that has never failed is a claim, not a check
- [x] T019 Full verification — `pnpm typecheck && pnpm lint && pnpm test`, then the Playwright run. Confirm the **backend** suites are untouched (SC-006 as corrected): `git diff` over `tests/Automation.Application.Tests`, `tests/SystemVariables.Application.Tests`, `tests/LayoutComposition.Application.Tests` and `tests/OverlayDesigner.Application.Tests` must be **empty**. Verification note on the PR following [quickstart.md](./quickstart.md), including T018's deliberate softening

---

## Dependencies

```
T001 ─▶ T002                          (the component)
          │
          ▼
   T003, T004, T005, T006 ─▶ T007      (the words)
          │
          ▼
   T008, T009, T010, T011              (the call sites)
          │
          ├──▶ T012, T013, T016, T017
          └──▶ T014 ─▶ T018
                 │
               T015
                 │
                 ▼
               T019
```

**T018 needs T014**, because it proves that specific assertion fires.

---

## Parallel opportunities

- **T003–T006** — four different pages, four independent sentences. The most
  genuinely parallel work in the feature.
- **T008–T011** — likewise, four different pages.
- **T013, T014, T016** with **T015** — different assertions, and T015 spans two
  files the others also touch, so one author per file.
- **T017** is deliberately **not** parallel with T012: both edit
  `RulesPage.test.tsx`, and T017 is the delicate one.

---

## Implementation strategy

**MVP is T011.** Once the four call sites ask first, no archive in the app
happens on one click — SC-001 is met and the feature is real. Everything after
is evidence.

**Do the words before the wiring.** T003–T007 before T008–T011: wiring first
makes it tempting to call the feature done and treat the wording as polish,
which is precisely how four confirmations end up saying the same thing.

**Do T017 last among the tests**, and read it twice. It is the only existing
test being changed, and the tempting change is the wrong one.

---

## Three things most likely to go wrong

1. **The four confirmations converge on one sentence.** Writing four in a
   sitting, *"This cannot be undone"* is where they all land — true every time,
   and for a layout it omits that the layout becomes **permanently unusable**.
   T005 and T006 specify the required claim, T014 asserts it, and T018 proves
   the assertion fires by softening it and watching the failure.

2. **The kiosk warning fires for drafts too.** It is one conditional, and
   dropping it makes every archive look equally dangerous. An overstated warning
   is one operators learn to click through, which costs more than the warning
   buys. T015 asserts **both** directions in one task, because the published-only
   assertion passes against a confirmation that always warns.

3. **`RulesPage`'s existing assertion gets deleted rather than moved.** The
   quickest way to green is to drop `expect(archiveMock).toHaveBeenCalledWith(…)`
   instead of adding the confirmation step. That removes the only check that
   archiving still sends the right request, at the exact moment the path to it
   changed. T017 says so in its own text for this reason.
