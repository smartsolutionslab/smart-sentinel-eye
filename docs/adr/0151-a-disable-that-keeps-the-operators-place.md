# ADR-0151: A disable that keeps the operator's place

**Status:** **Proposed**
**Date:** 2026-09-15
**Extends:** ADR-0077 (Radix UI headless components + custom design system)
**Amends:** —
**Supersedes:** —
**Superseded by:** —

## Context

A browser blurs a focused element the instant it becomes natively `disabled`,
moving focus to `<body>`. Nothing announces this, nothing restores it, and the
operator's next `Tab` starts from the top of the document.

This repository has now hit that fact three separate times, in three separate
specs, and fixed it the same way each time:

| Spec | Control | Where the reasoning is written |
|---|---|---|
| 154 | `OverlayEditor` Undo / Redo | `OverlayEditor.tsx:649-657` |
| 156 | `ChainRecoveryNotice` Retry / Reload | `ChainRecoveryNotice.tsx:31-40` |
| 160 | Both editor dialogs' Save | `OverlayEditorDialog.tsx`, `LayoutEditorDialog.tsx` |

Each fix replaces the native `disabled` attribute with `aria-disabled`, which
keeps the control focusable and clickable, and pairs it with a **guard** that
actually refuses the action. The guard is not optional and not cosmetic:
`aria-disabled` is an announcement, not a behaviour, so without one the control
is merely lying about being unavailable.

The three guards take three different shapes, because the control types differ:

- **Spec 154** — `handleUndoClick`/`handleRedoClick` already no-op, because
  `undo()`/`redo()` return `false` with nothing to do.
- **Spec 156** — `activate()` opens with `if (reReading) return;`.
- **Spec 160** — the form's own `onSubmit` returns before ever calling
  `handleSubmit`.

Three instances is where a pattern stops being a coincidence. But the reason
this needs a *decision* rather than a convention note is that the obvious
enforcement is actively dangerous.

### The naive rule would introduce bugs

**`aria-disabled` restores implicit form submission.** A natively-disabled
default submit button suppresses Enter-to-submit; an `aria-disabled` one does
not. So a mechanical `disabled` → `aria-disabled` conversion on a submit button
*creates* a defect unless a submit-event guard lands in the same change — the
form will submit on Enter while the button reads as unavailable.

Spec 160 hit exactly this, flagged it as "the single way this change could end up
worse than the bug", and found that neither of its two Enter-key tests actually
proved anything: `user-event` does not implement the browser's implicit
submission algorithm — for `Enter` on an `<input>` it dispatches a synthetic
**click on the submit button** — and the e2e test pressed `Enter` with focus
already *on* Save. Both were button activations wearing an implicit-submission
costume. Real coverage needed a Playwright test pressing `Enter` in a *text
field*, and proving it discriminates required disabling the guard and watching
the PATCH count go from 1 to 2.

A lint rule that bans `disabled=` on buttons, unaccompanied, would mass-produce
that defect across the codebase and be green the whole time.

### The population

Measured on `develop` at `36796e84`:

- **32** `disabled={…}` sites across **17** `.tsx` files in `apps/`.
- **6** production `aria-disabled={…}` sites across **4** files.

So a blanket rule would put 32 call sites in scope, in features nobody is
currently working in — camera dialogs, rules, system variables, audit — each
needing a guard written and tested to avoid the trap above.

### Not every disabled control has the problem

A control only loses the operator's place if it can become disabled **while it
holds focus**. The canonical case is a control disabled *as a result of
activating it* — Save disabling on `isLoading`, Retry disabling on `reReading`,
Undo disabling because the click exhausted the stack. All three precedents are
exactly this shape.

A control disabled by a condition the operator is not interacting with — a bulk
action disabled because nothing is selected — can still be focused when the
condition flips, but it is a rarer and less predictable path. Treating those
identically is what makes the population 32 instead of a handful.

## Decision

**Proposed — requires acceptance before any of the following binds.**

### 1. The rule, stated narrowly

A control that can become unavailable **while it holds focus** uses
`aria-disabled`, never the native `disabled` attribute, and pairs it with a
guard in the handler that refuses the action.

"While it holds focus" is satisfied whenever activating the control is what makes
it unavailable. That is the shape of all three existing instances and it is
decidable by reading the control's own handler.

### 2. The guard is part of the rule, not a follow-up

`aria-disabled` without a guard is a regression, not a partial fix. For a submit
button the guard belongs on the **form's** `onSubmit`, before validation —
covering both routes, since implicit submission does not go through the button's
`onClick`.

### 3. Enforcement: the `Button` primitive, not a lint rule

`apps/shared/src/ui/primitives/Button.tsx` keeps `disabled:pointer-events-none
disabled:opacity-50` in its base classes. Those are what make a natively
disabled button *look* right, and so they are what make the wrong choice
comfortable.

The proposal is to make the sanctioned path the easy one rather than to ban the
unsanctioned one: give `Button` an explicit `unavailable` prop that emits
`aria-disabled` plus the dimming classes, and document that `disabled` remains
correct for controls that cannot be focused when they disable.

A `no-restricted-syntax` rule banning `disabled=` is **rejected** — see below.

### 4. Existing sites are not converted en masse

The 32 native sites stay as they are. This rule binds new controls and any
control a spec is already touching. A sweep would mean 32 guards written by
someone with no reason to be in those files, with the implicit-submission trap
live in every one of them.

## Consequences

**The next instance costs nothing to get right.** Three specs have each spent
review cycles rediscovering the same browser behaviour from scratch; a named
prop and one ADR reference replaces that.

**The dangerous mechanical conversion is explicitly ruled out.** Anyone reaching
for a codemod finds §4 and the implicit-submission trap written down.

**A real inconsistency persists, deliberately.** Two spellings of "unavailable"
will coexist in the codebase — 32 native, 6 aria — and no tool will flag the
difference. That is the cost of not sweeping, and it is a genuine cost: a reader
cannot tell whether a given `disabled=` was considered and kept, or never
examined.

**The rule's trigger requires judgement.** "Can become unavailable while it holds
focus" is not syntactic, so no analyzer can decide it. This is the second ADR in
two days to admit its rule is unenforceable by tooling (see ADR-0150 §3), and the
admission is deliberate: this repository's §II drifted twice, its Phase 3 board
gate drifted for sixteen specs, and §IV recorded a built leg as unbuilt — each
time because a rule's limits were assumed rather than stated.

**`Button` gains API surface** for a concern most of its call sites do not have.

## Alternatives Considered

**A lint rule banning `disabled=` on interactive elements.** Rejected as
actively harmful, not merely insufficient: it would push 32 call sites toward
`aria-disabled` with no guard, and the implicit-submission trap would ship a real
defect in every form it touched — silently, since `user-event`'s Enter handling
cannot detect it. A rule whose mechanical satisfaction creates bugs is worse than
no rule.

**Strip `disabled:` from `Button` entirely.** The strongest forcing function:
natively disabled buttons would simply stop looking disabled, so every site would
have to make a choice. Rejected because it breaks the 32 existing sites'
appearance immediately, converting a latent inconsistency into a visible
regression, and it punishes the correct uses equally.

**Sweep all 32 sites now.** Rejected on the cost above. Recorded as the honest
upgrade path if the two spellings prove confusing in practice: it is a real
option, just not one worth spending a day on before anyone has been confused.

**Do nothing; the comments at the three call sites are enough.** This is what the
repository has been doing, and the evidence is that it does not transfer. Spec
156 cites spec 154's comment explicitly and still had to rediscover the
consequences for its own control; spec 160 then hit the implicit-submission trap
that neither predecessor documented, because neither had a submit button.

## Implementation Notes

- The `Button` change is small; the documentation of *when* to reach for it is
  the substance. Put it where the next author will be — the prop's own doc
  comment — not only in this ADR.
- Any new `aria-disabled` site needs its guard tested by **counterfactual**:
  remove the guard, watch the test fail. Spec 160's implicit-submission
  assertion passed while unable to observe the regression until it was moved
  after a real settle point, and the proof it finally discriminated was
  `Expected: 1, Received: 2` with the guard disabled.
- If a submit button is converted, the Enter path must be tested in a **real
  browser**. `user-event` cannot express it, and a vitest test asserting it is
  measuring a synthetic click.
- The three existing instances are the reference implementations, one per guard
  shape: `OverlayEditor.tsx:649-676`, `ChainRecoveryNotice.tsx:264-268`, and
  spec 160's `handleFormSubmit` in either editor dialog.
