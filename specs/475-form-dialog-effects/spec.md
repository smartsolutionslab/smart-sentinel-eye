# Spec 475 — The form-dialog reset, clear and submit-guard logic lives in one place

**Feature Branch**: `475-form-dialog-effects`
**Created**: 2026-11-01
**Status**: Draft
**Issue:** #3198 (feature-level; found by the frontend refactoring survey).
**ADRs:** ADR-0037 (phases), ADR-0079 (forms: React Hook Form + Zod), ADR-0151 (the submit guard),
ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** Pure extraction of code that already exists; the explanatory comments move with it.
**Latency budget:** none — operator console dialogs; not on the event-to-overlay path.
**Phase 4a colour:** behaviour-preserving → **characterisation** for the six dialogs (their existing tests,
unmodified); **red first** for the new shared hook(s) (ADR-0139).

## Problem

Form dialogs in `management-cameras` and `management-web` repeat three pieces of machinery, each with a long comment
recording a bug it was written to avoid. Read on `develop` (2026-11-01), not only grepped:

1. **Clear on close.** `RegisterCameraDialog`, `SystemVariableDialog` and `RuleDialog` each define
   `const clearOnClose = useEffectEvent(() => { … })` plus `useEffect(() => { if (!open) clearOnClose(); }, [open])`.
   The effect scaffolding is identical; **the bodies differ** (`reset()` in Register; `reset(DEFAULT_INPUT)` plus
   `setFabId('')` and `setFabError(null)` in the other two; all three call `resetMutationState()`). The comment on
   why watching `open` beats an `onOpenChange` handler (a parent-driven close never reaches the handler) is repeated.
2. **Reset on open.** `EditCameraAddressDialog` and `RenameCameraDialog` define `resetForOpen` via `useEffectEvent`,
   fired when `open` becomes true, with RTK Query's `reset` kept **out** of the dependency array (listing it churns
   mid-submit — issue #2624, spec 267). **They do not have the same dependencies:** Edit uses `[open]`, Rename uses
   `[open, currentName]`, so Rename re-fires when `currentName` changes while the dialog is open. A shared hook
   keyed only on `open` would silently change that.
3. **The ADR-0151 submit guard.** `handleFormSubmit(event)` refuses a second submit while a mutation is in flight.
   **Six of the eight copies are byte-identical** (`if (isLoading) { event.preventDefault(); return; } void
   onSubmit(event);`): `RegisterCameraDialog`, `EditCameraAddressDialog`, `RenameCameraDialog`, `RuleDialog`,
   `SystemVariableDialog`, `WallForm`. **The other two use a different condition** (`saveBlocked`):
   `LayoutEditorDialog` and `OverlayDraftForm`.

Plus the `FormErrorSummary` / `renderedFields` wiring in `SystemVariableDialog` and `RuleDialog` (named in the issue;
see Scope).

### What the issue did not say

- **The saving is small, and the spec says so first.** The bodies of the clear/reset functions stay in each
  dialog (they differ), so what is shared is the effect scaffolding and the submit guard: a few lines per dialog.
  The measured outcome of the comparable #3190 was −2.4 % of the page lines; expect the same order here. What this
  buys is that the **#2624 and "parent-driven close" invariants and their comments exist once**, with a test that
  fails when they are broken, instead of being copied into three or five files that can drift.
- **It spans two apps.** Three of the six dialogs are in `management-cameras`, three in `management-web`, so a
  shared hook must live in `apps/shared` (the issue says so too).
- **Two of the eight files overlap other open issues.** `LayoutEditorDialog` is #3193 and `OverlayDraftForm` is
  #3192; both touch effect logic, and both use a different submit condition anyway.
- **Every one of the six dialogs has a test file** (`RegisterCameraDialog`, `EditCameraAddressDialog` +
  `.cache`, `RenameCameraDialog`, `RuleDialog`, `SystemVariableDialog`, `WallForm`), so the safety net exists.

## Scope

**In:** the six dialogs above: their clear-on-close and reset-on-open effect pairs (five dialogs) and their
byte-identical submit guard (six), moved into shared hook(s) in `apps/shared`, with the explanatory comments moving
into the hook. The final shape (one hook or several; names) is the plan's decision.

**Out (explicitly):**
- `LayoutEditorDialog` and `OverlayDraftForm` (#3193, #3192: different `saveBlocked` guard, and their own refactors).
- The `FormErrorSummary` / `renderedFields` wiring: not repeated byte-identically (it carries each form's own
  field list), and extracting it would be generality for two callers; the plan records the decision.
- The `react-hooks/incompatible-library` suppressions in `RuleDialog` / `SystemVariableDialog` / `WallForm`: judged
  in #2118 and delivered; they stay as they are.
- Any behaviour change, including the `[open, currentName]` dependency in `RenameCameraDialog`, which is kept.

## User Scenarios & Testing

### User Story 1 (P1) — The six dialogs behave exactly as before

As an operator I see the same dialogs: they start clean after closing, reset on opening, and refuse a second submit
while one is in flight. As the maintainer I can prove it.

**Independent test:** each dialog's existing test file passes with no line edited.

```gherkin
Scenario: the existing dialog tests are the safety net
  Given the test files of the six dialogs
  When they are run after the extraction
  Then every test passes
  And git diff shows no line of those test files changed

Scenario: a second submit is refused while one is in flight
  Given a dialog whose mutation is loading
  When the form is submitted again
  Then the submit event is default-prevented and onSubmit is not called, as before

Scenario: Rename still resets when the name changes while open
  Given RenameCameraDialog is open
  When currentName changes
  Then the form and mutation state reset, as before (the [open, currentName] dependency is kept)

Scenario: a close driven by the parent still clears
  Given a dialog closed by the parent flipping open, not by the dialog's own onOpenChange
  Then the form and mutation state are cleared, as before
```

### User Story 2 (P2) — The invariants live in one place, with a test that fails when they break

As a developer I want the #2624 rule (do not list RTK Query's `reset` in the effect dependencies) and the
"watch `open`, not `onOpenChange`" rule written once, with a test, so a new dialog inherits them.

```gherkin
Scenario: the dependency rule is pinned
  Given the shared reset-on-open hook
  When reset is (wrongly) added to the effect dependencies
  Then the hook's own test fails, showing the mid-submit pending state is lost (the #2624 failure)

Scenario: the scaffolding exists once
  Given the six dialogs
  When the extraction is done
  Then none declares its own useEffectEvent clear/reset pair or its own handleFormSubmit guard
```

### Edge cases

- **`useEffectEvent` and the `react-hooks` lint rules.** The dialogs rely on `useEffectEvent` so the effect fires
  only on `open` while the handler still sees the latest `reset`/`resetMutationState`; the hook must keep that
  property and must not trip `react-hooks/exhaustive-deps` or `incompatible-library`.
- **Hook order.** Moving effects into a hook changes where they sit relative to other hooks in each dialog; no
  dialog may change the order other hooks run in.
- **Focus and Radix `onOpenChange` interplay** (ADR-0151 focus-return): the guard must stay on the form's
  `onSubmit`, not move to a button.
- **The comments are evidence, not decoration.** Each records a real failure; none is dropped, and each appears once.

## Requirements

- **FR-001** Every test in the six dialogs' test files passes after the change; none of those files is modified.
- **FR-002** The clear-on-close effect pair (three dialogs), the reset-on-open effect pair (two dialogs) and the
  submit guard (six dialogs) are supplied by shared code in `apps/shared` and absent from the dialogs.
- **FR-003** Each dialog keeps its own clear/reset **bodies** and its own dependency set; in particular
  `RenameCameraDialog` keeps `[open, currentName]`.
- **FR-004** The shared hook(s) have their own tests, each seen failing first (ADR-0139), including a test that
  fails when `reset` is added to the reset-on-open dependencies (#2624) and one that fails when a parent-driven close
  does not clear.
- **FR-005** No change to API calls, rendered text, `aria-*`, test ids or the order other hooks run in.
- **FR-006** The explanatory comments move into the shared hook(s); none is lost, none is duplicated.
- **FR-007** No change outside `apps/management-cameras/`, `apps/management-web/` (the six dialogs only),
  `apps/shared/src/hooks/` (+ its export in `package.json`), plus `specs/475-…` and `.specify/feature.json`.
- **FR-008** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for every touched app, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** The six dialogs' test files: same test count before and after, all passing, `git diff` on them empty.
- **SC-002** The total lines of the six dialogs fall by a measured amount the PR reports **alongside** the lines of
  the new hook(s) and their tests (not the drop alone).
- **SC-003** A search of the six dialogs for `useEffectEvent` and `function handleFormSubmit` finds nothing; the
  same search of `apps/` finds only the shared hook(s) and the two out-of-scope files (`LayoutEditorDialog`,
  `OverlayDraftForm`).
- **SC-004** The hook tests' counterfactuals fail for the right reason (the failure message is read, not just counted).
- **SC-005** The whole suite of each touched app passes, with durations before and after.

## Assumptions

- The existing dialog tests are strong enough to be the characterisation (the issue says so). The plan checks the
  two edge cases that matter (the Rename dependency and the parent-driven close) and writes a covering test first
  where a dialog's tests do not pin them.
- A custom hook that wraps `useEffectEvent` + `useEffect` preserves the effect semantics the dialogs rely on.

## Dependencies

#3196 (merged). Coordinates with #3192 and #3193 (out of scope here, same area).
