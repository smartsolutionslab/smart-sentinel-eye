# Implementation Plan: Double-break `OverlayLayerList`'s add/remove/move guards

**Branch**: `485-layerlist-double-break` | **Date**: 2026-11-08 | **Spec**: [spec.md](./spec.md) | **Issue**: #3226

## Summary

Spec 477 pinned the draft form's element guards through a stub seam and noted that `OverlayLayerList` repeats them, so a break in the
hook is invisible through the real tree. This plan measures that: it breaks every guard in the list, in the hook, and in both, and
reads the failure messages. Where the **end-to-end behaviour** (an operator's click on an `aria-disabled` Add / Remove / Move changes
nothing) is not pinned by any real-tree test, a new test in a new file pins it. No production file changes (FR-005).

## Technical Context

TypeScript / React, vitest + Testing Library. `shared` runs under node by default (`// @vitest-environment jsdom` header);
`management-web` under jsdom. The real tree is `OverlayDraftForm` rendering the real `OverlayLayerList` and `useElementSelection`.

## Constitution Check

§IV: not on the path (spec). §Testing: behaviour-preserving → characterisation; a gap test is green on today's code first (ADR-0139).
ADR-0036: no production change, no speculative test of an unreachable state (FR-004).

## Decisions

**D1. What "noticed" means here — two runs per break.** Each layer already has its own seam test: the list's guards are pinned by
`OverlayLayerList*.test.tsx` (isolated props), the hook's by `OverlayDraftFormProps.test.tsx` (stub seam). So a **single-layer** break
is, by construction, noticed by that layer's own tests. The question the issue asks is different: is the **behaviour** pinned when
both layers' guards are gone? Each break is therefore run twice:

1. against the **real-tree** suite only (`management-web` overlays folder, which renders the real list inside the real form), the
   evidence for "does anything pin the behaviour end to end";
2. against the **full** covering set (adds the shared list tests and the stub-seam tests).

A "both" break that fails (1) is defended end to end. One that fails only (2) is pinned by seam tests that each go green again as soon
as the other layer is restored, which is the gap spec 477 recorded; it gets a real-tree test.

**D2. The break matrix.** Named, exactly-once substitutions (`probe485.cjs`; list file `L`, hook file `H`, call site `C`):

| Id  | Guard                   | L (list)                         | H (hook)                       | C (call site)        |
| --- | ----------------------- | -------------------------------- | ------------------------------ | -------------------- |
| A1  | add under `readOnly`    | `add`: drop `readOnly \|\|`      | `addElement`: drop `readOnly`  | —                    |
| A2  | add at the cap          | `add`: drop `!canAdd`            | `addElement`: drop `MAX` term  | `canAdd` constant    |
| R1  | remove under `readOnly` | `remove`: drop `readOnly \|\|`   | `removeElement`: drop it       | —                    |
| R2  | remove the last element | `remove`: drop `!canRemove`      | `removeElement`: drop `<= 1`   | `canRemove` constant |
| M1  | move under `readOnly`   | `move`: drop `readOnly`          | `moveElement`: drop `readOnly` | —                    |
| M2  | move past either end    | `move`: drop the `target` bounds | (none: list only)              | —                    |

Each runs as `L`, `H`, `LH`, and for A2/R2 also `CLH` (the call site is a third layer). The hidden Add menu under `readOnly` is a
fourth defence for A1; a variant `A1+menu` renders the dropdown under `readOnly` (the `readOnly || !canAdd ?` branch) so the
handler guard is reachable at all. If the menu cannot be opened under `readOnly`, A1's handler guards are **unreachable through the
DOM** and are recorded as equivalent with that evidence (FR-004), not given a test.

**D3. Reaching the guard in a test.** The buttons are `aria-disabled`, not `disabled`, so `user.click` reaches the handler and a
real-tree test needs no seam: render `OverlayDraftForm` with `readOnly`, click `Remove A`, `Move B down`, and assert the element set
(the layers `<ol>` names) and the live region are unchanged; with one element click `Remove`. At the cap (and under `readOnly`) the list renders a plain
unavailable `Add` instead of the menu, so no menu entry exists to click: whether A1/A2's handler guards are reachable at all is
exactly what the probe answers (D2). Tests live in **new** files beside the existing ones (`OverlayDraftFormGuards.test.tsx`).

**D4. Order.** Baseline suites and counts first; then the matrix, run in foreground batches of about five (one vitest at a time; a
break left applied if the harness reaps a run is checked and restored from git, never `git stash`/`checkout -- .`); then gap tests,
each proved by re-applying its break; then the full suites.

**D5. Production code is read-only.** The probe script restores only the three files it breaks, from git, and fails if the tree is
dirty on them before it starts. Nothing under `src/` of the apps is committed.

## Phases and commits

1. `docs(485): plan`, `docs(485): tasks`.
2. `test(485): pin <what> end to end` (one commit per new file, green on today's code; the break that fails it quoted in the commit
   body).
3. `docs(485): verification note, tick tasks`.

Every commit leaves both app suites green.

## Risks and how they are covered

- **A "both" break may be unreachable** (the Add menu not rendered under `readOnly`; `canAdd` false at the cap hides the menu): D2
  records it as equivalent with the DOM evidence instead of forcing a test.
- **A third layer may make a guard dead** (call-site `canRemove` makes the hook's `<= 1` unreachable from the list): reported as
  jointly defended with the `CLH` break; any guard found dead is reported to the user, not removed (FR-005).
- **A test that passes for the wrong reason** (the same text asserted on the input): each new test is proved by its own break, and the
  asserted value is read from the DOM state, not from the prop that drives it.

## Project Structure

```text
specs/485-layerlist-double-break/   spec.md, plan.md, tasks.md, verification.md, checklists/
apps/management-web/src/features/overlays/OverlayDraftFormGuards.test.tsx   (only if the probe finds a gap; new)
```
