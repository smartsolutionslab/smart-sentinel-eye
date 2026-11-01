# Spec 476 — `LayoutEditorDialog` is broken up by responsibility

**Feature Branch**: `476-layout-editor-dialog-split`
**Created**: 2026-11-01
**Status**: Draft
**Issue:** #3193 (feature-level; found by the frontend refactoring survey; sibling of #3190, #3192, #3198).
**ADRs:** ADR-0037 (phases), ADR-0084 (frontend 50-LOC-per-function row: recorded there as **not yet enforced**),
ADR-0113 (If-Match version), ADR-0151 (the `unavailable` button and the form-level submit guard), ADR-0139 /
ADR-0144 (phase 4a).
**No new ADR.** A decomposition inside existing conventions.
**Latency budget:** none — operator console dialog; not on the event-to-overlay path.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the existing
`LayoutEditorDialog*.test.tsx` family; they must pass with every assertion **unmodified**. An assertion that has to
be edited is evidence the behaviour moved: block, do not adjust (CLAUDE.md).

## Problem

`apps/management-web/src/features/layouts/LayoutEditorDialog.tsx` is **501 lines**, of which one component,
`LayoutEditorDialog`, is **444** (lines 58–501), against ADR-0084's 50 LOC per function. Measured on `develop`
(2026-11-01), not only grepped:

- **21 hook calls** in that component (`useCreateLayoutDraftMutation`, `useEditDraftRevisionMutation`,
  `useGetLayoutQuery`, 2 × `useEffect`, 4 × `useState`, `useDebouncedValue`, `useListAllCameraChoicesQuery`,
  `useListOverlaysQuery`, `useMemo`, `useForm`, 2 × `useWatch`, `useAuth`, 2 × `useRef`, `useCanEditOverlays`,
  `useCallback`). **The issue said 11; the spec uses the measured 21.**
- It holds seven interleaved concerns, each with comments recording a real failure it was written to avoid:

  | # | Concern | Where (lines) |
  |---|---|---|
  | A | The chain read (`currentData`, not `data`; `refetchOnMountOrArgChange`) and the mutation state for create vs edit | 59–96 |
  | B | Resetting **both** mutation states when the dialog closes (a mode-selected reset would clear the wrong one) | 98–112 |
  | C | The camera picker: raw filter text, its debounced copy, the page-walk query, the `knownCameras` accumulator merged **during render**, the clear-on-close adjustment (`wasOpen`), and the status sentence | 114–192, 328–370 |
  | D | The form: `defaultValues`, `useForm`, the re-seed effect, `onSubmit` for create and edit | 193–247 |
  | E | The refusal text (`staleConflict`, `backendError`) | 249–257 |
  | F | The Save gate: `saveRef`, `saveBlocked` (6 terms across 4 specs), the form-level submit guard | 259–326 |
  | G | The tile view: its open index, the watched cells, `assignOverlay`, and the token ref with the one `react-hooks/refs` suppression | 373–386 |

- The JSX (lines 388–500) is ~110 lines of its own: name field, truncation notice, camera filter field and
  status, `GridDesigner`, `ChainRecoveryNotice`, the button row, `TileView`.

### What the issue did not say (premise check, 2026-11-01)

1. **This is a decomposition, not a deduplication, and the line count will not fall.** Moving code into hooks and
   sub-components adds signatures and imports. Expect the total to **rise slightly**. The benefit is a component
   short enough to read and named units that can be understood (and later tested) one at a time. ADR-0084's limit is
   not enforced, so there is no failing gate being fixed.
2. **The order and timing of several statements is load-bearing.** The `knownCameras` merge and the `wasOpen`
   adjustment are done **during render** (React's documented "adjust state when a prop changes"), not in an effect,
   because the effect forms were the cascading-render bug the comments describe; the merge converges on contents,
   not array identity, or the component aborts with "too many re-renders". Moving them into a hook must keep them
   during render, in the same order relative to the other hooks.
3. **The close-reset effect depends on `[open, createState, editState]`** (the whole result objects), not on the
   `reset` functions; that is existing behaviour, kept as is.
4. **`saveBlocked` has its own submit guard** (`handleFormSubmit`), which #3198 (PR #3215, form-dialog hooks)
   deliberately left alone because it differs from the plain `isLoading` guard. Whether it adopts `useSubmitGuard`
   after #3215 merges is a separate, trivial follow-up, **not** part of this change.
5. **The `react-hooks/refs` suppression was judged in #2118** and stays; the spec asks only that it be isolated in
   one small unit.
6. **The covering family is large (2,757 lines in 7 files)** but has not been checked cluster by cluster. The plan
   must find, per concern, which test pins it, and write a covering test **first** where none does (a refactor with
   no covering test is a rewrite).

## Scope

**In:** `LayoutEditorDialog.tsx`: its seven concerns moved into hooks and/or sub-components **next to the file** in
`apps/management-web/src/features/layouts/`, one extraction per commit, each green on its own (bisect).

**Out (explicitly):**
- Any behaviour change, including "obvious" fixes found on the way (two issues, per CLAUDE.md).
- `LayoutsPage.tsx` and every other caller; the dialog's props and exports (`LayoutEditorDialog`,
  `LayoutEditTarget`, `LayoutEditorDialogProps`) keep their shape.
- `GridDesigner`, `TileView`, `ChainRecoveryNotice` (already separate).
- Adopting #3198's `useSubmitGuard` for `saveBlocked`, and `OverlayDraftForm` (#3192).
- The `react-hooks/refs` suppression's judgement (#2118): kept, isolated.
- Moving anything to `apps/shared`: nothing here has a second caller.

## User Scenarios & Testing

### User Story 1 (P1) — The dialog behaves exactly as before

As an operator I see the same dialog: the same camera filter and status sentences, the same Save gate, the same
refusal text and the same tile view. As the maintainer I can prove it.

**Independent test:** the seven existing test files pass with no line edited.

```gherkin
Scenario: the existing tests are the safety net
  Given the LayoutEditorDialog*.test.tsx family
  When it is run after each extraction
  Then every test passes
  And git diff shows no line of those test files changed

Scenario: the render-time state adjustments still happen during render
  Given the camera picker
  When the camera query returns a page, then a different filter excludes a camera a tile holds
  Then that tile still shows its camera by name, and the component does not abort with "too many re-renders"

Scenario: a close clears the right mutation state
  Given a refused edit, then the dialog is closed from the parent
  When it is reopened on a different draft
  Then no stale refusal is shown (both mutation states were reset)

Scenario: Save stays blocked in exactly the same cases
  Given a chain read in flight, failed, or not yet made, or no camera ever seen, or a save in progress
  Then Save is aria-disabled and a submit (click or Enter) is refused, as before
```

### User Story 2 (P2) — The component is short enough to read

As a developer I want `LayoutEditorDialog` to read as a list of what it composes, with each concern in a unit I
can name, so a change to the camera picker does not mean reading the Save gate.

**Independent test:** line counts of the component and of each extracted unit, reported before/after.

```gherkin
Scenario: the component is short
  When the split is done
  Then LayoutEditorDialog is well under its former 444 lines
  And each extracted unit is under 50 lines where the logic allows (the issue's own bar)

Scenario: the suppression is isolated
  Then the single react-hooks/refs suppression sits in one small unit with its justification
```

### Edge cases

- **Hook order.** Hooks move into custom hooks; the relative order of the stateful hooks and of the during-render
  adjustments must not change in a way a test can see, and no hook may become conditional.
- **Which hook owns `open`-driven state.** The close-reset (B), the filter clear (C) and the form re-seed (D) all
  react to `open`/`editTarget` changes; they must keep reacting to the same changes.
- **Props the sub-components need.** `GridDesigner` receives `form`; a sub-component that takes the whole form must
  not trigger extra renders that change what a test observes.
- **Comments are evidence, not decoration.** Each records a real failure (spec 153 `currentData`, spec 160 Save
  gate, the "too many re-renders" crash, the stale-conflict advice). Each moves with its code and appears once.
- **A first extraction that does not help is a stop sign** (the lesson of #3190 and #3198): measure after it.

## Requirements

- **FR-001** Every test in the seven `LayoutEditorDialog*.test.tsx` files passes after each commit; none of those
  files is modified.
- **FR-002** `LayoutEditorDialog` composes extracted hooks/sub-components for the concerns A–G (the plan decides
  which become hooks and which components); its own body is the composition and the JSX that remains.
- **FR-003** No change to API calls, query arguments, mutation arguments, rendered text, `aria-*` attributes, class
  names, ids, or the order other hooks run in.
- **FR-004** The exports and props of `LayoutEditorDialog.tsx` are unchanged; no caller changes.
- **FR-005** The `react-hooks/refs` suppression is isolated in one unit; no new suppression is added.
- **FR-006** Comments move with their code; none is dropped, none duplicated.
- **FR-007** Each commit builds and passes the family on its own; the extraction is one concern per commit.
- **FR-008** No change outside `apps/management-web/src/features/layouts/` (new files beside the dialog),
  `specs/476-…` and `.specify/feature.json`.
- **FR-009** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `management-web`, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** The seven test files: same test count before and after, all passing, `git diff` on them empty.
- **SC-002** `LayoutEditorDialog`'s own length and each new unit's length, reported before/after **together with the
  file total** (not the component drop alone); each extracted unit under 50 lines where the logic allows.
- **SC-003** A search of the new files and the dialog for `eslint-disable` finds exactly the one `react-hooks/refs`
  suppression, in one unit.
- **SC-004** The whole `management-web` suite passes, with its count (684) and duration before and after.
- **SC-005** For every concern (A–G), a named test in the family pins it, or a new characterisation test written
  first and seen green on the unmigrated tree.

## Assumptions

- The existing family is strong enough on most concerns; the plan finds the gaps rather than assuming (the premise
  check in #3198 found exactly one unpinned behaviour in a similar family).
- Extracting a custom hook preserves behaviour as long as the hooks are called unconditionally and in the same
  relative order.

## Dependencies

Independent of #3215 (PR for #3198) and of #3192. A later, separate step may adopt `useSubmitGuard` for
`saveBlocked` once #3215 is merged.
