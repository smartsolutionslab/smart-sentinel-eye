# Implementation Plan: The form-dialog reset, clear and submit-guard logic lives in one place

**Branch**: `475-form-dialog-effects` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/475-form-dialog-effects/spec.md`

## Summary

Three small hooks in `apps/shared/src/hooks/` — `useClearOnClose`, `useResetOnOpen`, `useSubmitGuard` — replace the effect
scaffolding and the submit guard that six dialogs copy. Each dialog keeps its **own clear/reset body and its own
dependency set**; the long comments that record real failures (#2624, "watch `open` not `onOpenChange`") move into the
hooks and appear once, next to a test that fails when they are broken. New hooks are **red first**; the six dialogs are
**characterisation** (existing tests, unmodified). One covering test is missing and goes in a **new file** (R4).

**Sizing, stated before building (the lesson of #3190).** The dialog bodies stay, so the saving is the scaffolding and
the repeated comments. Estimated 60–90 lines out of the six dialogs (of roughly 2,000), against ~70 lines of hook and ~200
of tests: the repository gets **larger**. The plan therefore has a **measure-and-decide step** after the first dialog
(T-D1 below), exactly because #3190's estimate was off by a factor of four.

## Technical Context

- **Stack**: TypeScript, React 19.3.1 (`useEffectEvent` already used by five of these dialogs), React Hook Form, RTK Query,
  Vitest + Testing Library, `eslint-plugin-react-hooks` 7.1.1.
- **Baseline (2026-11-01, `develop` at 1f19ea0a + the specs):** the six dialogs and their tests exist; suites to record in T001.
- **Touches**: `apps/shared/src/hooks/` (new hook file, its test, one export line), the six dialogs, one new
  characterisation test file. No dialog test file is edited.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green; new behaviour starts red | Dialogs: characterisation. Hooks: red first, failure quoted. |
| ADR-0036 — smallest change, no speculative generality | Each hook has ≥ 2 callers (guard 6, clear 3, reset 2); nothing for a caller that does not exist. |
| ADR-0151 — submit guard on the form's `onSubmit` | Kept: the guard stays on `<form onSubmit>`, not a button. |
| Lane may not weaken a gate | No test edited; the #2118 suppressions untouched. |
| No new ADR | Extraction inside existing conventions. |

## Research (decisions that shaped the plan)

### R1 — The API: three hooks in one file, `apps/shared/src/hooks/useFormDialog.ts`

```ts
// Clear when the dialog is closed (also on mount while closed: what the dialogs do today).
export function useClearOnClose(open: boolean, clear: () => void): void
// Reset when the dialog opens, and again when `resetKey` changes while open.
export function useResetOnOpen(open: boolean, reset: () => void, resetKey?: unknown): void
// ADR-0151: refuse a second submit while one is in flight.
export function useSubmitGuard(isLoading: boolean, onSubmit: (event: FormEvent) => unknown): (event: FormEvent) => void
```

Each wraps the callback in `useEffectEvent` **inside the hook** and calls it from the effect, so the effect depends on
`[open]` (and `[open, resetKey]`) only, while the callback still sees the latest `reset`/`resetMutationState`. Exported from
`apps/shared/src/hooks/index.ts` (pure React: it pulls in no gateway module, unlike `useCameraStream`).

### R2 — The dependency difference is an argument, not an array

`EditCameraAddressDialog` depends on `[open]`; `RenameCameraDialog` on `[open, currentName]`. A `deps?: unknown[]`
parameter would make the effect's dependency array dynamic (a lint error and a footgun). Instead `resetKey?: unknown` adds
**one** fixed slot: `useResetOnOpen(open, resetForOpen, currentName)` → `[open, resetKey]`; Edit passes nothing, so its
effective trigger stays `[open]` (a constant `undefined` slot never changes). Rename keeps re-firing on a `currentName`
change, exactly as today.

### R3 — What each dialog keeps

| Dialog | Replaces | Keeps |
|---|---|---|
| `RegisterCameraDialog` | clear effect pair, guard | `reset()` + `resetMutationState()` body |
| `RuleDialog` | clear effect pair, guard | `reset(DEFAULT_INPUT)`, `setFabId('')`, `setFabError(null)`, `resetMutationState()` body |
| `SystemVariableDialog` | clear effect pair, guard | same body shape as Rule |
| `EditCameraAddressDialog` | reset effect pair, guard | `resetMutationState()` body |
| `RenameCameraDialog` | reset effect pair (key `currentName`), guard | `reset({ name: currentName })` + `resetMutationState()` |
| `WallForm` | guard only | everything else |

`LayoutEditorDialog` and `OverlayDraftForm` are untouched (different `saveBlocked` guard; #3193, #3192).

### R4 — One missing covering test, in a new file (FR-001 forbids editing the existing ones)

Checked on `develop`: the parent-driven close is pinned by `toggleOpen(rerender, false)` tests in Register, Rule and
SystemVariable, and Edit has a `setOpen` helper. **`RenameCameraDialog.test.tsx` has no `rerender` call, so "the form
resets when `currentName` changes while open" is not pinned.** A refactor with no covering test is a rewrite, so
`RenameCameraDialogReset.test.tsx` is written **first** and seen **green on the unmigrated tree** (it characterises
today's behaviour), then must stay green. It is a new file, so no existing test file changes.

### R5 — Not extracted, with reasons

`FormErrorSummary` / `renderedFields` wiring (carries each form's own field list; two callers; not byte-identical);
`LayoutEditorDialog` / `OverlayDraftForm` (above); the `react-hooks/incompatible-library` suppressions (#2118).

## Project Structure

```text
apps/shared/src/hooks/
  useFormDialog.ts                 (new: useClearOnClose, useResetOnOpen, useSubmitGuard + the moved comments)
  useFormDialog.test.tsx           (new)
  index.ts                         (+1 export line)
apps/management-cameras/src/features/cameras/
  RegisterCameraDialog.tsx, EditCameraAddressDialog.tsx, RenameCameraDialog.tsx   (shrink)
  RenameCameraDialogReset.test.tsx (new: characterises the currentName re-reset)
apps/management-web/src/features/
  rules/RuleDialog.tsx, systemVariables/SystemVariableDialog.tsx, walls/WallForm.tsx   (shrink)
specs/475-form-dialog-effects/
```

## Delivery: one PR, commits that each stay green except the one red-test pair

1. `RenameCameraDialogReset.test.tsx`, **green** on the unmigrated tree (characterisation).
2. `useFormDialog.test.tsx` only, **red** (module not found) — failure quoted in the PR (`test(475): … (red)`).
3. `useFormDialog.ts` + export; hook tests green, unmodified.
4. **Counterfactuals** (not committed): `reset`/the callback added to the effect dependencies (the #2624 failure at hook
   level); effect moved off `open` (parent-driven close missed); guard that does not `preventDefault`. Each failure **read**.
5. **First dialog: `RegisterCameraDialog`.** Then **T-D1: measure** its lines before/after and extrapolate the six; if the
   projected saving is below what the maintainer accepted for #3190's pieces (consistency only), **stop and ask** before
   the remaining five.
6. One commit per remaining dialog: Rename, Edit, Rule, SystemVariable, WallForm.
7. Task ticks and the verification note.

Commit 2 leaves the hook suite failing until commit 3 (the accepted red/green pair).

## Verification (what "unchanged" means, concretely)

Recorded on the unmigrated tree, repeated after:

- **Dialog suites (characterisation):** per dialog, sorted `file :: full name :: status` and sorted assertion lines of its
  test files (incl. `EditCameraAddressDialog.cache.test.tsx`) — `diff` empty; `git diff origin/develop...HEAD` on those
  files empty.
- **New suites:** hook tests green; `RenameCameraDialogReset` green before **and** after the Rename commit.
- **Counterfactuals read for the right reason** (the lesson of spec 474): the failure message must name the broken
  property, not merely fail.
- **Greps (SC-003):** none of the six declares `useEffectEvent` or `function handleFormSubmit`; searched with the
  general pattern, over `apps/`, expecting only the shared hooks and the two out-of-scope files.
- **Line accounting:** the six dialogs before/after **and** the hook + test lines added, reported together.
- Each touched app's whole suite (`management-cameras`, `management-web`, `shared`) with durations;
  `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check`; `dotnet test tests/Architecture.Tests -c Release`.

## Risks

| Risk | Mitigation |
|---|---|
| A hook wrapping `useEffectEvent` trips `react-hooks/exhaustive-deps` or `incompatible-library` | Run lint on the hook and each dialog; if it trips, the finding is reported, not suppressed (#2118 forbids new suppressions in the lane). |
| The `resetKey` slot changes Edit's behaviour | A constant `undefined` slot never changes; a test asserts Edit-style use (no key) does not re-fire on rerender. |
| Hook order changes in a dialog | Each replacement sits at the position of the code it replaces; the dialog tests (which render every path) are the check. |
| The saving is smaller than hoped | Stated up front; measured after the first dialog with a stop-and-ask step (T-D1). |
| A comment is lost or duplicated in the move | Each comment is moved, then the dialogs are grepped for the distinctive phrases ("The obvious rewrite is wrong here", "#2624"): once in the hook, zero in the dialogs. |
