# Verification 470 — `CellPage.test.tsx` is split along the concerns its `describe` blocks already name

Phase 5 (ADR-0037). Everything below was observed on branch `470-split-cellpage-tests` on 2026-11-01, base
`develop` at 405ad00e. Test-only change, phase 4a colour **characterisation**: there is no red test to quote;
the evidence is that the same tests, unedited, pass in their new homes.

## Before (T001, unsplit tree)

| Record | Value |
|---|---|
| `CellPage.test.tsx` | 3027 lines, 66 tests, 66 passed |
| Sorted assertion lines (`expect(`, `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow`) | 161 |
| Lines from `describe('CellPage'` to end of file (multiset) | 2512 |
| Whole `kiosk-web` suite | 25 files, 329 tests, 20.9 s |

## After (T013)

| Check | Result |
|---|---|
| Test names with status, 8 files, file column excluded | 66; `diff` against before **empty** |
| Sorted assertion lines, 8 files | 161; `diff` **empty** |
| Body-line multiset, 8 files vs before | differs only by: 7 added `describe('CellPage', () => {`, 7 added `});`, 8 added `beforeEach(resetCellPageMocks);`, and the removed old `beforeEach` block (10 statements + its `beforeEach(() => {` / `});`) that now lives in `resetCellPageMocks()` in the harness. No other line differs. |
| Each file run alone | `CellPage` 7, `WallFab` 5, `FabSkew` 12, `OmittedInputs` 9, `ResolvedText` 8, `LabelAge` 5, `LiveUpdates` 14, `LabelSet` 6 = 66, all passed |
| Whole `kiosk-web` suite | 32 files, 329/329, **25.1 s** (+20 %, plan §R4 threshold was +25 %) |
| File sizes (lines) | 185, 225, 239, 303, 365, 402, 468, 486; harness 526. Cap was 600 (SC-003). |
| `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` (`kiosk-web`) | clean |
| `dotnet test tests/Architecture.Tests -c Release` | 3254/3254 |
| `git diff --name-only origin/develop...HEAD` outside the allowed set | none (FR-006) |

Every commit in the stack was run before being made: after each move, the new file and the shrunken
`CellPage.test.tsx` were run together and their counts matched the plan (5/61, 12/49, 9/40, 8/32, 5/27, 14/13,
6/7 moved/left), with `tsc`, `eslint` and `prettier` clean.

## Counterfactual: the harness is not vacuous (T005)

In `cellPageHarness.tsx`, the `layoutHub` mock's `capturedCallbacks = callbacks;` was replaced by
`void callbacks;` (the hub still starts; the test can no longer reach its callbacks). `CellPage.test.tsx`:

```text
Failed Tests 27
FAIL … CellPage > per-tile overlay highlight (US3) > Scenario 1: lights only the tile bound to the highlighted overlay, then reverts
FAIL … CellPage > per-tile overlay highlight (US3) > Scenario 2: lights every tile bound to the reused overlay (highlight-all-matching)
FAIL … CellPage > live-update resilience (spec 011 US2) > Shows the degraded badge while live updates are down and clears it on recovery
```

Restored; 66/66 again. A first attempt that renamed the mocked module failed for the wrong reason (module
resolution, not wiring) and was discarded.

## What went wrong on the way (kept visible)

- A first run of the seven moves committed nothing: the pre-commit hook rejected the unformatted harness
  (the script rewrites it each time; `prettier --write` had only covered the test files).
- My `git checkout -- apps/kiosk-web` to clean up restored from the **index**, which still held the failed run's
  staged files, so the rebuilt first move briefly contained all seven new files. Caught by `git show --stat`
  per commit, reset to the harness commit, rebuilt with explicit `git add` of two files per commit. The final
  tree is byte-identical to the first attempt; the history is the planned one.

## Premises corrected on the way

- The issue's `shard-N.filter` requirement does not apply to Vitest (no change made).
- #3196 left this file untouched (no local `jsonResponse`), so the "moving a test multiplies duplication"
  concern did not arise.
- It was not a pure move: the ~515-line preamble needed a shared module, and registering `vi.mock` from a
  non-test module is a new pattern in this repo (spiked first, plan §R1).

## Not observed

- CI on Linux: the first run is the PR's.
- Whether the +20 % suite time is stable: one before and one after measurement, on a machine used for other
  work in between.

## QA (phase 6, `/code-review medium`): 7 findings

| Finding | Disposition |
|---|---|
| `vi.mock` in the harness only reaches modules loaded after it; import order is unchecked | **Checked, not a defect today:** the first statement of all 8 files is the harness import. **Comment added** at the top of the harness saying to import it first. No lint rule enforces it. |
| `capturedCallbacks` / `reportLag` are live `export let` bindings; `?.` could turn a broken binding into a vacuous pass | **Counterfactual, both bindings.** Cutting `capturedCallbacks` failed 27 tests (T005); cutting the `reportLag` and `reportLagByCamera` wiring fails 4 (`CellPage*` suite: 4 failed, 167 passed); restored. The same `?.` reads were in the monolith. Not changed. |
| tasks.md has two unchecked tasks | **Not a defect:** T015/T016 are the PR and its close-out. |
| `.specify/feature.json` is tool state that conflicts across branches | **Not changed:** every spec PR commits it (see 469's verification). |
| Comments elsewhere still cite `CellPage.test.tsx` | **Partly fixed.** One pointer written in the moved text (`CellPage.test.tsx:1667`, in `CellPageLabelSet`) now names `CellPageOmittedInputs.test.tsx`. Not changed: `wallGrid.test.ts:64` still correct (that test stayed), and pointers by line number or helper name in `CameraViewerSamplerWindow.test.tsx`, `LayoutGridLabelPairing.test.tsx`, `RemoteSurface.test.tsx`, `WallPage.test.tsx`, `LayoutGrid.tsx` (a production file, out of scope) and older specs. Line-number pointers rot on any edit; they are history, not references. |
| Harness comments say "this file" / "here" | **Fixed** (6 phrases reworded; comments only). Re-run: 329/329, lint, `tsc`, `prettier` clean, assertion lines still identical. |
| `store` imported from two paths; cache cleanup differs across files | **Not a defect:** both paths are the same module instance; per-describe `afterEach` cleanup moved with its group exactly as it was, and Vitest isolates each file. |
