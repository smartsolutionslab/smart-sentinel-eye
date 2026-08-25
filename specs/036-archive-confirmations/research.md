# Phase 0 Research: Archiving asks before it happens

**Feature**: `036-archive-confirmations` · 2026-08-25

Five questions. **The fifth found a defect in this feature's own spec**, which is
what it was asked to look for.

---

## 1. Every page can name what it is archiving

FR-003 requires the confirmation to name its subject. All four already have one
in hand — and three of them already render it a few lines above the button:

| Page | Available at the call site |
|---|---|
| `RulesPage` | `rule.name`, `rule.version`, `rule.fab` |
| `SystemVariablesPage` | `variable.name`, `variable.version`, `variable.fab` |
| `LayoutsPage` | `chain.name` (rendered as the heading), `newest.revisionNumber` |
| `OverlaysPage` | `chain.name` (likewise), `newest.revisionNumber` |

**Decision**: name the subject by its **name**, and for layouts and overlays add
the revision — *"Archive revision 4 of Cnc-Hall?"*. No identifier appears in any
confirmation.

The worry that prompted this check — that a layout might only have its
identifier at the call site, making the confirmation read *"Archive revision 4
of 0192f3c1-…"* — does not apply. `chain.name` is right there.

---

## 2. Published and draft are distinguishable, so FR-008 is expressible

`newest.state` is available and already rendered:

```tsx
v{newest.revisionNumber} · {newest.state}
```

**Decision**: the layout and overlay confirmations take the state and say the
kiosk sentence **only** when the revision is `Published`.

This matters because the spec's edge cases forbid claiming consequences that do
not apply: archiving a **draft** strands nothing (a published revision may still
exist to branch from) and no kiosk is showing a draft. A confirmation that
warned about kiosks either way would be crying wolf on the safe case and would
train operators to skip reading it — which is the failure mode a confirmation
has.

---

## 3. One component, four sets of words

**Decision**: **one** `ArchiveConfirmation` component in `management-web`,
taking the subject and its consequences, over four near-identical dialogs.

**Rationale, and it is the opposite of spec 035's**: there, two dialogs shared a
*shape* — a form, a schema, a mutation — while their behaviours differed, and
extraction was rejected. Here the **behaviour is identical** in all four (ask,
then archive on confirm, then nothing on dismiss) and only the **words** differ.
Words are what a component takes as props.

FR-011 requires it anyway: *"one shared confirmation behaviour … so that
dismiss-does-nothing and no-double-submit hold identically in all four."* Four
copies would be four places for that to drift, and this feature exists because
something drifted.

**Alternatives considered:**

- **Four separate dialogs**, mirroring `RetireCameraDialog`. Rejected: that
  dialog is bound to one mutation and one set of consequences, which is right
  for one caller and wrong for four.
- **Call `ConfirmDialog` directly at each site.** Nearly right — it already
  carries the behaviour — but each caller would then repeat the open-state, the
  pending wiring and the mutation call. The wrapper is thin and removes exactly
  that repetition.

`ConfirmDialog` (spec 032) supplies the behaviour underneath: `role="alertdialog"`,
focus defaulting to cancel, a `pending` prop that blocks a second submit, and a
danger-styled action. **Nothing new is needed.**

---

## 4. Every page already holds dialog state

All four already manage at least one open-state — an editor dialog, a dry-run
panel, a set-value form. A page holding two competing open-states is a real
complication, not a formality.

**Decision**: the archive confirmation's state is *which subject is pending
confirmation*, held as a nullable value rather than a boolean:

```
null | { name, version, … }
```

That makes two open-states impossible to confuse — the confirmation is open
exactly when a subject is pending — and it carries the data the wording needs
without a second lookup. `RulesPage` already uses this shape for its dry-run
panel (`useState<{ name, fab } | null>(null)`), so it is the page's own idiom.

---

## 5. SC-006 is wrong as written, and this is the finding

The spec says:

> **SC-006**: No archive operation's behaviour changes, verified by the four
> contexts' existing tests passing **unchanged**.

**One existing test cannot pass unchanged.** `RulesPage.test.tsx`:

```tsx
it('Archives a rule from its row action, naming the rule’s fab', async () => {
  await user.click(screen.getByRole('button', { name: 'Archive' }));
  expect(archiveMock).toHaveBeenCalledWith({ name: 'high-oee', version: 0, fabId: 'munich' });
});
```

It clicks Archive and expects the mutation to have fired. After this feature,
clicking Archive opens a confirmation and the mutation does **not** fire. The
test must gain a confirmation step.

### Exactly one, and the others are fine

| Test file | Archive mock | Asserts on it |
|---|---|---|
| `RulesPage.test.tsx` | yes | **yes — must change** |
| `LayoutsPage.test.tsx` | yes | no — mocked only to satisfy the hook |
| `OverlaysPage.test.tsx` | yes | no — likewise |
| `SystemVariablesPage.test.tsx` | none | no |

### What SC-006 was reaching for, and how it should read

It conflated two different things:

- **The archive operation itself** — what it does, refuses and announces. That
  genuinely does not change, and the **backend** contexts' suites
  (`Automation.Application.Tests` and the rest) will pass untouched. FR-012 is
  about this, and it holds.
- **How the interface reaches that operation.** That is precisely what this
  feature changes, so a frontend test asserting the old interaction must change.

**Decision**: SC-006 splits. The backend suites pass unchanged; the one frontend
test is **updated, and the update is a task rather than a surprise** — it gains
the confirmation step and keeps its existing assertion, which then proves the
mutation still receives exactly what it did before.

That is a stronger check than leaving it alone would have been: it asserts both
that the confirmation is required and that confirming produces the identical
request.

---

## Summary of decisions

| # | Question | Decision |
|---|---|---|
| 1 | Naming the subject | All four have a name in hand. No identifiers in any confirmation |
| 2 | Published vs draft | `newest.state` is available; the kiosk sentence appears only for `Published` |
| 3 | One component or four | **One**, wrapping `ConfirmDialog`. Behaviour identical, words differ — the inverse of spec 035 |
| 4 | Competing open-states | Nullable pending-subject, matching `RulesPage`'s existing idiom |
| 5 | SC-006 | **Wrong as written.** One frontend test must change; the backend suites are untouched |

**No new dependency, no migration, no backend change.**
