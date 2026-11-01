# Spec 477 — `OverlayDraftForm` is broken up by responsibility

**Feature Branch**: `477-overlay-draft-form-split`
**Created**: 2026-11-02
**Status**: Draft
**Issue:** #3192 (feature-level; found by the frontend refactoring survey; sibling of #3190, #3193, #3198).
**ADRs:** ADR-0037 (phases), ADR-0084 (frontend 50-LOC-per-function row: recorded there as **not yet enforced**),
ADR-0113 (If-Match version), ADR-0119 (conflict wording keyed on the code), ADR-0151 (the `unavailable` button and the
form-level submit guard), ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** A decomposition inside existing conventions; follows spec 476 (`LayoutEditorDialog`), whose method it reuses.
**Latency budget:** none — operator console form; not on the event-to-overlay path.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the existing
`OverlayDraftForm*.test.tsx` family and `OverlayEditorReseedRegression.test.tsx`; they must pass with every assertion
**unmodified**. An assertion that has to be edited is evidence the behaviour moved: block, do not adjust (CLAUDE.md).

## Problem

`apps/management-web/src/features/overlays/OverlayDraftForm.tsx` is **627 lines**; one component, `OverlayDraftForm`, is
**476** (lines 152–627), against ADR-0084's 50 LOC per function. Measured on `develop` (2026-11-02), not only grepped:

- **21 hook calls** in that component (`useCreateOverlayDraftMutation`, `useEditDraftOverlayRevisionMutation`,
  `useGetOverlayQuery`, `useAuth`, 2 × `useRef`, 2 × `useCallback`, 3 × `useEffect`, `useMemo`, `useForm`,
  `useFieldArray`, `useWatch`, 3 × `useState`, `useDebouncedValue`, `useResolveOverlayTextQuery`,
  `useListAllCameraChoicesQuery`). **The issue said 13; the spec uses the measured 21** (the generic-aware count, which
  spec 476's first count missed).
- **Three `react-hooks` suppressions** (lines 207 `refs`, 234 `exhaustive-deps`, 294 `refs`), as the issue says. They were
  judged individually in #2118 and are **not to be removed or changed here**; the split isolates them.
- The covering family is **11 files, 3,857 lines**: `OverlayDraftForm.test` (959), `…ChainRecovery` (551), `…SaveGate`
  (405), `…Layers` (402), `…ChainAnnouncement` (376), `…Shapes` (373), `…ResolvePreview` (304), `…Colour` (166),
  `…GetToken` (96), `…Review` (95) and `OverlayEditorReseedRegression` (130).
- Concerns interleaved in the component, each with comments recording a real failure:

  | # | Concern | Where (lines) |
  |---|---|---|
  | A | The chain read (`currentData`, not `data`; `refetchOnMountOrArgChange`) and the create/edit mutation state | 162–191 |
  | B | The token callback: stable identity over a ref (the first `react-hooks/refs` suppression) | 193–209 |
  | C | Resetting **both** mutation states **on unmount**, mount-once (the `exhaustive-deps` suppression) | 211–235 |
  | D | The form: `defaultValues`, `useForm`, `useFieldArray`, `useWatch`, the re-seed effect, `onSubmit` for create and edit | 237–306, 357–391 |
  | E | Element selection: the chosen row id, the render-time "select the row just added" adjustment, the selected index and editor key, and `addElement` / `moveElement` / `removeElement` / `selectFirstInvalidElement` | 271–301, 393–420 |
  | F | The `onElementsChange` callback ref and its effect (the second `refs` suppression) | 293–298 |
  | G | The resolved-text preview: debounce, the skip-able query, `settled`, `isResolving` | 308–336 |
  | H | The camera-choices query | 338–355 |
  | I | The refusal text (`staleConflict`, `notDraft`, `nameTaken`, `backendError`, `offerReload`) | 422–443 |
  | J | The Save gate: `saveRef`, `colourRefused`, `saveBlocked`, the form-level submit guard | 445–495 |
  | K | The JSX (a name field, a read-only wrapper, `OverlayEditor`, the chain-recovery notice, the button row) | 497–627 |

### What the issue and spec 476 do not settle (premise check, 2026-11-02)

1. **A decomposition, not a deduplication: the line total will not fall.** Moving code into hooks and sub-components adds
   signatures and imports. Expect the file total to **rise**. The value is a component that reads as a list of what it
   composes; ADR-0084's limit is not enforced, so no failing gate is being fixed. (Spec 476: component −68 %, file total
   +42 %.)
2. **The covering family has not been checked concern by concern, and spec 476 found that a large family still left nine of
   nineteen deliberate breaks unnoticed.** This family is 3,857 lines, not 2,757, but size is not coverage. The plan must
   **probe first** (break each concern once in the unsplit form, run the family, read what failed) and write a covering
   test, in a **new file**, before moving any concern whose break nobody noticed.
3. **Concern E is the delicate one.** `useFieldArray` regenerates row ids on every reset, so a stale `chosenId` falls back
   to the default selection; "select the row just added" is done **during render** (`addedAtCount`), not in an effect; and
   the editor is keyed by `chosenIndex >= 0 ? selectedId : 'default'` so a default selection does not remount it. Moving
   this into a hook must keep the render-time adjustment in the render body and must not change what keys the editor.
4. **C is mount-once on purpose** (no `open` prop here, unlike `LayoutEditorDialog`): the cleanup resets both states on
   unmount only, and the empty dependency array is the point of the one `exhaustive-deps` suppression. It is moved as it
   stands, suppression and comment together.
5. **`saveBlocked`'s submit guard is the same shape as `useSubmitGuard`** (#3198, merged): `useSubmitGuard(saveBlocked,
   onSubmit)`. Adopting it is a separate trivial follow-up, **not** part of this change (as in spec 476).
6. **Two units' bodies are the same as in `LayoutEditorDialog`** (the token callback B, the chain read A, the refusal
   text I shape), and spec 476's units are in a parked PR (#3217). This spec **does not share code with them**: nothing is
   imported across the two features, and no second caller is created.

## Scope

**In:** `OverlayDraftForm.tsx`: its concerns moved into hooks and/or sub-components **next to the file** in
`apps/management-web/src/features/overlays/`, one extraction per commit, each green on its own (bisect).

**Out (explicitly):**
- Any behaviour change, including "obvious" fixes found on the way (two issues, per CLAUDE.md).
- `OverlayEditor`, `ChainRecoveryNotice`, `OverlayDraftFormFrameGrabber`, `BackdropControls` (already separate).
- Every caller (`OverlayCreatePage`, `OverlayEditPage`, the dialog); the form's props and exports (`OverlayDraftForm`,
  `OverlayEditTarget`, `OverlayDraftFormProps`) keep their shape.
- Adopting `useSubmitGuard` for `saveBlocked`; the `LayoutEditorDialog` split (#3193, parked PR #3217).
- The three suppressions' judgement (#2118): kept, isolated.
- Moving anything to `apps/shared`: nothing here has a second caller.

## User Scenarios & Testing

### User Story 1 (P1) — The form behaves exactly as before

As an operator I see the same form: the same selection behaviour when I add, move or remove an element, the same preview, the
same Save gate and the same refusal wording. As the maintainer I can prove it.

**Independent test:** the eleven existing test files pass with no line edited.

```gherkin
Scenario: the existing tests are the safety net
  Given the OverlayDraftForm*.test.tsx family and OverlayEditorReseedRegression.test.tsx
  When they are run after each extraction
  Then every test passes
  And git diff shows no line of those test files changed

Scenario: selection follows the element, not the index
  Given three elements with the second selected
  When the first is removed, or the second is moved down, or a new element is added
  Then the selection follows the element it was on (or lands on the new row), as before

Scenario: a reset does not strand the selection
  Given an explicit choice, then a reseed that regenerates row ids
  Then the selection falls back to the first Text element, as before

Scenario: a refused save never leaks into the next mount
  Given a refused save, then the form unmounts
  When a form mounts for a different target
  Then no stale refusal is shown (both mutation states were reset on unmount)

Scenario: Save stays blocked in exactly the same cases
  Given a save in progress, a refused colour, or an edit whose chain read is missing, in flight or failed
  Then Save is aria-disabled and a submit (click or Enter) is refused, as before
```

### User Story 2 (P2) — The component is short enough to read

As a developer I want `OverlayDraftForm` to read as a list of what it composes, with each concern in a unit I can name.

```gherkin
Scenario: the component is short
  When the split is done
  Then OverlayDraftForm is well under its former 476 lines
  And each extracted unit is under 50 code lines where the logic allows (the issue's own bar)

Scenario: the suppressions are isolated
  Then each of the three react-hooks suppressions sits in one small unit with its justification, unchanged
```

### Edge cases

- **Hook order and render-time state.** Hooks move into custom hooks; the render-time `setState` adjustments (E) stay in the
  render body of whichever hook owns them; no hook becomes conditional.
- **`readOnly` (the PLACEHOLDER seed).** While `readOnly`, `onChange` is a no-op and the element operations return early:
  that gating must survive the move exactly.
- **`onElementsChange` fires from an effect on `watchedElements`**, reading the newest callback through a ref; a unit that
  held the effect but not the ref (or the reverse) would change when it fires.
- **`useResolveOverlayTextQuery` is skipped unless the text contains `{{`**, and its result is withheld until the debounce
  has caught up (`settled`); the preview must not show a response for earlier text.
- **Comments are evidence, not decoration.** Each records a real failure and moves with its code, once.
- **A first extraction that does not help is a stop sign** (#3190, #3198, #3193): measure, then ask.

## Requirements

- **FR-001** Every test in the eleven family files passes after each commit; none of those files is modified.
- **FR-002** `OverlayDraftForm` composes extracted hooks/sub-components for the concerns A–K (the plan decides which
  become hooks and which components); its own body is the composition and the JSX that remains.
- **FR-003** No change to API calls, query arguments, mutation arguments, rendered text, `aria-*` attributes, class
  names, ids, keys, or the order other hooks run in.
- **FR-004** The exports and props of `OverlayDraftForm.tsx` are unchanged; no caller changes.
- **FR-005** The three `react-hooks` suppressions are each isolated in one unit, unchanged in rule and justification; no new
  suppression is added.
- **FR-006** Comments move with their code; none is dropped, none duplicated; any pointer such as "above" or "below" is
  rewritten to name the new unit.
- **FR-007** Each commit builds and passes the family on its own; one concern per commit.
- **FR-008** No change outside `apps/management-web/src/features/overlays/` (new files beside the form), `specs/477-…` and
  `.specify/feature.json`.
- **FR-009** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `management-web`, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** The eleven family files: same test count and names before and after, all passing, `git diff` on them empty.
- **SC-002** `OverlayDraftForm`'s own length and each new unit's length, reported before/after **with the file total**,
  not the component drop alone; code lines and comment lines reported separately.
- **SC-003** A search of the form and its new units for `eslint-disable` finds exactly the **three** original suppressions,
  each in one unit, rule and justification unchanged.
- **SC-004** The whole `management-web` suite passes, with its count and duration before and after.
- **SC-005** For every concern (A–K), a deliberate break is noticed by a named test; where the family notices nothing, a
  new characterisation test is written first, green on the unsplit form, and proved able to fail (the message read). Where a
  behaviour is defended jointly with another (as `currentData` is in spec 476) and cannot be pinned alone, that is
  recorded with the double-break evidence instead.

## Assumptions

- The family is strong on the concerns its file names announce (ChainRecovery, SaveGate, GetToken, ResolvePreview, Layers,
  Shapes, Colour, Review); the probes decide where it is not.
- Extracting a custom hook preserves behaviour as long as the hooks are called unconditionally and the render-time
  adjustments stay in the render body.

## Dependencies

Independent of #3217 (spec 476) and #3193. A later, separate step may adopt `useSubmitGuard` for `saveBlocked`.
