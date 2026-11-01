# Verification 475 — The form-dialog reset, clear and submit-guard logic lives in one place

Phase 5 (ADR-0037). Observed on branch `475-form-dialog-effects` on 2026-11-01, base `develop` at 1f19ea0a (+ #3213's comment-only
change on top of it before the PR). Phase 4a: **red first** for the three hooks, **characterisation** for the six dialogs,
and one **new characterisation test** (the unpinned Rename behaviour).

## Before (T001, unmigrated tree)

| Record | Value |
|---|---|
| `management-cameras` dialog tests (Register, EditCameraAddress, EditCameraAddress.cache, Rename) | 40 passed; 72 assertion lines |
| `management-web` dialog tests (Rule, SystemVariable, WallForm) | 63 passed; 135 assertion lines |
| The six dialogs | 1404 lines (the plan said "roughly 2,000"; **that was wrong**, it was an unmeasured guess) |
| `useEffectEvent` / `function handleFormSubmit` in production files | 5 files / 8 definitions |
| Whole suites | `shared` 1293 tests 52.6 s; `management-cameras` 146 tests 22.4 s; `management-web` 684 tests 99.0 s |

## Pinning the one missing behaviour (T002)

`RenameCameraDialog.test.tsx` has no `rerender` call, so "the form resets when `currentName` changes while open" was unpinned.
`RenameCameraDialogReset.test.tsx` (a new file; no existing test file edited) characterises it and was **green on the unmigrated tree**
(4 passed). A green-on-first-run test must be able to fail, so the break that matters was applied to the dialog (a hook keyed only on
`open` would do exactly this): dropping `currentName` from Rename's dependency array failed the two tests about it, for the right reason
("Received" the typed text instead of the new name; "expected 1 to be greater than 1" reset calls), and left the other two green. Restored.

## Red first (the hooks are new behaviour)

`apps/shared/src/hooks/useFormDialog.test.tsx` committed before the module existed:

```text
FAIL  src/hooks/useFormDialog.test.tsx
Error: Cannot find module './useFormDialog.js' imported from apps/shared/src/hooks/useFormDialog.test.tsx
Test Files  1 failed (1)   Tests  no tests
```

Green after the hooks: 15 tests. One line was added to the test file between the two (`// @vitest-environment jsdom`): `shared` defaults
to `node`, and the red run never reached that error because the module import failed first. No assertion changed.

## Counterfactuals: each rule can fail, for the right reason (T005; failure messages read)

| Break (one at a time, `git diff` empty after each) | Failing test and message |
|---|---|
| callback in `useResetOnOpen`'s effect deps, no `useEffectEvent` (#2624) | `Does not fire again when only the callback changes identity` — `expected "vi.fn()" to not be called at all, but actually been called 1 times` |
| `useClearOnClose`'s effect no longer driven by `open` (parent close missed) | `Clears when the parent flips open to false…` and `Calls the latest callback…` — `expected to be called 1 times, but got 0 times` |
| `preventDefault` dropped from `useSubmitGuard` | `Refuses a second submit while one is in flight…` — `expected to be called 1 times, but got 0 times` |
| `resetKey` dropped from `useResetOnOpen`'s deps | `Fires again when the reset key changes while open…` — `expected to be called 2 times, but got 1 times` |

## Characterisation: the six dialogs

| Check | Result |
|---|---|
| Test names with status, both apps | 40 and 63; `diff` against before **empty** |
| Sorted assertion lines | 72 and 135; `diff` **empty** |
| `git diff origin/develop...HEAD` on the seven existing dialog test files | **empty** — none edited (FR-001) |
| In code (comments stripped), `useEffectEvent` / `function handleFormSubmit` in the six dialogs | none; in `apps/`, `useEffectEvent` code only in the shared hook, `function handleFormSubmit` only in `LayoutEditorDialog` and `OverlayDraftForm` (out of scope) |
| Distinctive comments | "The obvious rewrite is wrong here", "extra render of an already-closed", "a second submit while": once, in the hook, zero in the dialogs. `issue #2624` appears once in the hook and once as a **one-line pointer** in `RenameCameraDialog` |
| `shared` / `management-cameras` / `management-web` suites | 1308 (+15 hook tests) / 150 (+4 pin tests) / 684 (unchanged); 52.5 s / 23.6 s / 98.6 s vs 52.6 / 22.4 / 99.0 before |
| `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` (three apps) | clean; no `react-hooks` complaint about `useEffectEvent` inside a hook |
| `dotnet test tests/Architecture.Tests -c Release` | 3254/3254 |
| Files changed outside the allowed set (FR-007) | none |

## The size, stated as measured

| | Lines |
|---|---|
| `RegisterCameraDialog` / `EditCameraAddressDialog` / `RenameCameraDialog` | 166→141 / 221→202 / 219→200 |
| `RuleDialog` / `SystemVariableDialog` / `WallForm` | 417→398 / 200→175 / 181→172 |
| **Six dialogs** | **1404 → 1288 (−116, −8.3 %)** — the gate projection after the first dialog was −122 |
| New code | hooks 80 (59 of them the moved comments), hook tests 176, Rename pin test 99 = **+355** |
| Net for the repository | about **+239 lines** |

The maintainer was asked after the first dialog (T007) with these projections and chose to adopt in the other five. What it buys: the three
rules (#2624, "watch `open`, not `onOpenChange`", the ADR-0151 guard) exist once, each next to a test seen failing for the right reason.

## What went wrong on the way (kept visible)

- **The plan's size claim was wrong** ("roughly 2,000 lines"; the six dialogs are 1404). Caught when recording the baseline, before any
  decision depended on it, and corrected here and in the PR.
- **Several commands ran in the wrong directory** (relative paths resolved from the repo root): a hook-export edit and the first
  counterfactual run did nothing and said so loudly. Re-run with the working directory anchored and printed; the first counterfactual
  results above are from the correct run.
- **The shared package defaults to a `node` test environment**, which the red run could not show (see above).
- **My edit script assumed `useEffect` had no other use** in `RuleDialog` / `SystemVariableDialog`; lint flagged `useEffect` as undefined.
  Fixed by keeping the import where another effect remains (both files).

## Not observed

- CI on Linux: the first run is the PR's.
- The dialogs in a browser; the evidence is their existing tests under jsdom.

## QA (phase 6, `/code-review medium`): 5 findings, 2 fixed

| Finding | Disposition |
|---|---|
| `useSubmitGuard` is named like a hook but calls no React hook; it returns a new function each render | **Not changed.** It sits with its two siblings under one `use…` family, and every dialog calls it at the top level next to them. A new function identity each render is exactly what the inline `function handleFormSubmit` it replaces did. Renaming would touch seven files and the tests for a naming preference. If a caller ever needs it inside a callback, rename then. |
| The hook docs use C#/XML-doc tags (`<c>`, `<see cref>`), which render as raw markup; issue references belong in the PR body | **Partly fixed.** `<c>` and `<see cref>` were mine and C#-only: replaced with backticks and `{@link …}`. `<p>`/`<i>` stay: they are this repository's existing habit in TypeScript comments (the Overlays and Layouts tests use them). The `#2624` / ADR-0151 references and the long comments **stay in the hook on purpose**: the issue's own constraint is that the explanatory comments record real failures and move with the code. |
| `LayoutEditorDialog` / `OverlayDraftForm` still carry their own `handleFormSubmit` with no marker | **Recorded, not marked.** They are #3193 and #3192's files (a different `saveBlocked` condition) and FR-007 forbids touching them; the pointer goes in the #3198 close-out comment (T016). |
| `.specify/feature.json` is in the diff | **Not changed:** every spec PR commits it. |
| The call-site comment says "Every close path lands here exactly once", but `useClearOnClose` also runs once on mount while closed | **Fixed (comment only).** The three call sites now say "Runs on every close and once on mount while closed", and the hook doc states that `clear` must therefore be idempotent and free of side effects (toasts, telemetry). Proved comment-only: the code with comments stripped hashes identically in all four edited files. |
