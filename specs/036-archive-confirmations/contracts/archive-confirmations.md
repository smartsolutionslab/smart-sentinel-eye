# Contract: What each archive confirmation says

**Feature**: `036-archive-confirmations` · 2026-08-25

A wording contract. No endpoint changes; no archive operation changes. What
changes is that four operations now ask first, and what they say when they do.

---

## Shared by all four

| | |
|---|---|
| Behaviour | `ConfirmDialog` — `role="alertdialog"`, focus defaults to **cancel**, `pending` blocks a second submit |
| Names the subject | **always**, by name — never by identifier |
| Says it cannot be undone | **always** |
| Dismiss | archives **nothing** |
| Confirm | archives **exactly once** |

Focus defaulting to cancel is not decoration: for an irreversible action it is
the difference between a stray Enter backing out and a stray Enter going
through with it.

---

## Rules

> **Archive `high-oee`?**
>
> This cannot be undone. The rule cannot be published again — authoring a
> replacement means cloning it, which creates a new rule with its own history.

Verified from `Rule`'s own documentation: *"The only path back to Draft is to
clone the rule (preserves the audit trail)."*

**Not claimed**: that evaluation stops. Plausible, and not checked, so not said.

---

## System variables

> **Archive `line-3-throughput`?**
>
> This cannot be undone. The variable's current value is cleared, and it can
> never be given another.

Verified: `Variable.Archive` sets `Value = VariableValue.Unset.Instance`, and
`SetValue` refuses once archived.

Both halves matter and neither is obvious from the page. The value disappearing
is immediate and visible elsewhere; the refusal is what anything trying to set
it will hit from then on.

---

## Layouts

> **Archive revision 4 of `Cnc-Hall`?**
>
> This cannot be undone, and **this layout can never be edited or published
> again**.
>
> *(published revisions only)* Kiosks showing this layout will be sent away from
> it immediately.

**The second sentence is the point of this feature.** FR-007 forbids softening
it to *"cannot be undone"* — that is true of all four and understates this one.
A layout does not merely stay archived; it becomes unusable, because
`BranchDraft` needs a published revision, `Revert` needs a published revision,
and `Publish` and `EditDraft` need a draft. After archiving the published
revision there is neither.

The kiosk sentence is **conditional on `Published`**. Archiving a draft strands
nothing and disturbs no kiosk. A confirmation that warned either way would
overstate the safe case, and an overstated warning is one operators learn to
click through.

Verified: the kiosk's `onArchived` calls `navigate('/', { replace: true })` for
the matching layout.

---

## Overlays

> **Archive revision 2 of `Line-1 Title`?**
>
> This cannot be undone, and **this overlay can never be edited or published
> again**.
>
> *(published revisions only)* Kiosks using this overlay will stop showing it.

Same structure and same reason as layouts — `Overlay` exposes the same six
behaviours with the same guards.

The kiosk consequence differs in kind and the wording follows it: an archived
overlay is marked unavailable in the cells using it, rather than navigating the
kiosk away. Verified from the kiosk's `onOverlayArchived`.

---

## What is deliberately absent

- **No identifiers.** *"Archive revision 4 of 0192f3c1-…"* is not a
  confirmation. Every page has the name in hand.
- **No "Are you sure?"** — the phrase this feature exists to replace.
- **No claim that archiving can be reversed.** It cannot, anywhere, and issue
  1877 tracks whether that should remain true for layouts and overlays.
- **No promise about what happens next.** These confirmations describe the
  operation in the future tense; nothing afterwards claims what took place.
