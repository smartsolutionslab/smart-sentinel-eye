# Implementation Plan: Archiving asks before it happens

**Branch**: `036-archive-confirmations` · **Spec**: [spec.md](./spec.md) · **Date**: 2026-08-25
**Issue**: 1866

## Summary

One component and four call sites. The behaviour it needs already exists —
spec 032 built `ConfirmDialog` shared specifically so the second destructive
action would copy it rather than diverge, and this is that second action.

**The wording is the substance.** Four confirmations, four different
consequences, and one of them is the sharpest sentence in the app: a layout
whose published revision is archived can never be edited or published again.

## Technical Context

**Language**: TypeScript 5.7, React 19
**Dependencies**: RTK Query (ADR-0075), Radix + Tailwind tokens (ADR-0077/0078)
**Target**: `apps/management-web/src/features/` — four pages plus one new
component
**Backend**: **untouched** (FR-012)

**No new dependency, no migration, no backend change.**

## Constitution Check

| Principle | Assessment |
|---|---|
| **§IV Latency budget** | **N/A** — nothing on the event-to-overlay path |
| **§IX No speculative generality** | One component for **four** callers, wrapping a primitive built for this. Not speculative; FR-011 requires the sharing |
| **Smallest possible change** | Four call sites change from *archive* to *ask, then archive*. No archive operation is touched |
| **Mirror existing patterns** (ADR-0036) | `ConfirmDialog` for the behaviour, `RetireCameraDialog` for the wording shape, `RulesPage`'s nullable pending-subject for the state |

**No violations.**

## Phases

Four. The second is the feature.

### Phase 1 — The component

`ArchiveConfirmation` in `apps/management-web/src/features/`, wrapping
`ConfirmDialog`. Takes the subject's name, the consequences to state, a
`pending` flag and a confirm callback.

**One component rather than four**, which is the inverse of spec 035's call and
for the inverse reason: there two dialogs shared a *shape* while their
behaviours differed; here the behaviour is identical in all four and only the
words differ. Words are props.

### Phase 2 — Four sets of words

The substance, and the only part that is not mechanical.

| Page | Must say |
|---|---|
| Rules | cannot be published again; a replacement means cloning it into a new rule |
| Variables | its current value is **cleared**; it can never be given another |
| Layouts | **can never be edited or published again** — and, when published, kiosks showing it are sent away now |
| Overlays | **can never be edited or published again** — and, when published, kiosks using it stop showing it |

**FR-007 forbids softening the layout and overlay sentence to "cannot be
undone."** That phrase is true of all four and understates these two: they do
not merely stay archived, they become unusable.

**FR-008 is conditional.** The kiosk sentence appears only for a `Published`
revision. Archiving a draft strands nothing and disturbs no kiosk, and a
confirmation that warned either way would train operators to stop reading it.

### Phase 3 — The four call sites

Each page changes from *archive on click* to *ask on click, archive on confirm*.

State is the **pending subject**, nullable — `null | { … }` — not a boolean.
Every one of these pages already holds another open-state, and a nullable
subject makes the two impossible to confuse while carrying the data the wording
needs. `RulesPage` already uses this shape for its dry-run panel.

### Phase 4 — Evidence, including one test that must change

**`RulesPage.test.tsx` cannot pass unchanged** ([research.md](./research.md) §5).
It clicks Archive and expects the request to have been made; after this feature
that click asks a question instead. It is updated to confirm first and keep its
existing assertion — which then proves **both** that the confirmation is
required and that confirming sends exactly what it sent before.

Everything else is additive: dismiss-archives-nothing, the named subject, the
sentences, and no-double-submit — for all four.

## Sizing

| Phase | Files | Risk |
|---|---|---|
| 1 | 1 added | Low — the behaviour exists |
| 2 | (in Phase 1's file and the four pages) | **The wording** |
| 3 | 4 changed | Competing open-states |
| 4 | 4–5 test files | One update, the rest additive |

## Three things most likely to go wrong

1. **The four confirmations converge on one sentence.** Writing four in a
   sitting, *"This cannot be undone"* is where they all end up — true every
   time, and for a layout it omits that the layout becomes permanently
   unusable. FR-007 names the wrong wording explicitly because this is the
   likely outcome, not a hypothetical one.

2. **The kiosk warning appears for drafts too.** It is one conditional, and
   dropping it makes every archive look equally dangerous. A confirmation that
   overstates is one operators learn to click through, which costs more than the
   warning buys.

3. **The `RulesPage` test gets "fixed" by deleting its assertion.** The quickest
   way to make it green is to drop `expect(archiveMock).toHaveBeenCalledWith(…)`
   rather than add the confirmation step. That would remove the only check that
   archiving still sends the right request — at exactly the moment the path to
   it changed.

## Out of scope

Fixing the stranding (issue 1877); any change to an archive operation;
un-archive; confirmations anywhere else; bulk archive.

**Noted**: this does not wait for 1877. The confirmation is worth having
whichever way that resolves, and only two of the four sentences would change.
