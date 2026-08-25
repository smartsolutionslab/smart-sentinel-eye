# Contract: the two confirmations, verbatim

**Feature**: `038-chain-derived-row-actions` · **Plan**: [plan.md](../plan.md)

One row now confirms two different destructive actions. They archive the same
kind of thing server-side and mean entirely different things to an operator, so
the words are given here rather than left to implementation — the lesson spec 036
recorded when it found that confirmations written in one sitting converge on one
sentence.

**The whole point is that these two must not sound alike.**

---

## Archive — taking the layout out of service

Offered only when the chain has a live revision, and it targets that revision.

**Title**

> Archive revision {n} of {name}?

**Body**

> This takes the layout out of service. You can bring it back later by editing
> it, and **the tiles are kept**.
>
> Kiosks showing this layout will be sent away from it immediately.

**Confirm button**: `Archive`

The overlay's says *the label is kept*, and its kiosk sentence is
*Kiosks using this overlay will stop showing it* — both unchanged from spec 037,
which differ because the consequences genuinely differ.

### The kiosk sentence is no longer conditional

Spec 036 gated it on `published: newest.state === 'Published'`. Archive is now
offered **only** when a live revision exists and targets that revision, so the
flag is true every time this dialog opens.

**Remove the flag; render the sentence unconditionally.** A flag that is always
true is worse than no flag — it reads as a live condition and invites a future
caller to pass `false`.

Spec 036's FR-008 asked that the warning not fire when it does not apply. That is
now satisfied structurally: the confirmation that does not apply to kiosks is a
*different confirmation*, below.

---

## Discard draft — throwing away work in progress

Offered only when the chain has a draft, and it targets the **newest** draft.

**Title**

> Discard draft revision {n} of {name}?

**Body**

> This throws away the draft. The work in it cannot be recovered.
>
> {name} stays exactly as it is — *(rendered only when the chain has a live
> revision)*

...continuing that sentence:

> — revision {live} is still published and kiosks are unaffected.

When the chain has **no** live revision, the second paragraph is omitted
entirely. There is nothing reassuring to say: discarding the only draft of a
never-published layout leaves nothing live, and claiming otherwise would be the
same class of falsehood this feature removes.

**Confirm button**: `Discard`

### What it must not say

| Forbidden | Why |
|---|---|
| *takes the layout out of service* | False. The live revision is untouched and still on kiosks. **This is the exact sentence the current row shows here, and removing it is this feature's reason for existing.** |
| anything about **kiosks being sent away** | False. No kiosk is showing a draft. |
| *you can bring it back later by editing it* | False. A discarded draft is gone; editing branches a **new** draft from the live revision. |
| *the tiles are kept* / *the label is kept* | Misleading. The live revision's tiles are kept because it was never touched — the discarded draft's edits are not. |

Every one of those is true of the **Archive** confirmation and false here, which
is precisely why one dialog serving both was the defect.

---

## The component

`ArchiveConfirmation` gains one optional prop:

```ts
verb?: string;   // default 'Archive'
```

used for both the title (`{verb} {subject}?`) and the confirm button.

Not a misnomer creeping in: **both actions archive a revision** server-side. The
component confirms archiving a revision; the verb names it in the operator's
terms. Renaming it to something broader was considered and rejected as scope —
it would reach into the rules and system-variable callers this feature does not
touch. Revisit if a third verb appears.

Everything else is unchanged and must stay so: `role="alertdialog"`, focus
defaulting to cancel, the `pending` guard against a second submit, and dismissal
sending nothing (spec 032, spec 036 FR-002 and FR-011).

---

## What to assert, and what asserting badly looks like

| Assert | Because |
|---|---|
| Archive sends the **live** revision's number; Discard sends the **draft's** — **on the same chain** | Both succeed either way. Asserted on one chain, a swap fails; asserted separately, a swap passes twice. |
| The discard confirmation does **not** contain *out of service* | This is the falsehood being removed. A test that only checks the new sentence appears passes against a dialog that says both. |
| The discard confirmation does **not** mention kiosks | Same reason, and it is the sentence most likely to be copied across. |
| The archive confirmation **does** mention kiosks, with no draft-state fixture varying it | It is unconditional now. A test that still varies a fixture to check it would pass while the flag lingered. |
| Both name their revision number, and the two differ | FR-008. Two dialogs about one chain that do not say which revision are one dialog with two buttons. |
| Dismissing either calls its mutation **zero** times | Spec 036 FR-002. A confirmation that closes cleanly and acts anyway passes any assertion about closing. |
