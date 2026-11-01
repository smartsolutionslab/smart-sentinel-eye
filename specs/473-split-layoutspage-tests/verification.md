# Verification 473 — `LayoutsPage.test.tsx` is split along the concerns its `describe` blocks already name

Phase 5 (ADR-0037). Observed on branch `473-split-layoutspage-tests` on 2026-11-01, base `develop` at 0a4ab03c.
Test-only change, phase 4a colour **characterisation**: no red test; the evidence is that the same tests pass in
their new homes and that the only edited non-assertion lines are the 12 the plan allowed.

## Before (T001, unsplit tree)

| Record | Value |
|---|---|
| `LayoutsPage.test.tsx` | 1228 lines, 45 tests, 45 passed (11 of them nested under *archive confirmation*) |
| Sorted assertion lines | 78 |
| Non-blank lines from the first `describe(` (line 121) on | 976 |
| Lines matching `^\s*publishState = ` | 12 |
| Whole `management-web` suite | 86 files, 684 tests, 96.3 s |

## After (T010)

| Check | Result |
|---|---|
| Test names with status, five files, file column excluded | 45; `diff` against before **empty** |
| Nested names that still begin `LayoutsPage — archive confirmation LayoutsPage — ` | **11** (same as before) |
| Sorted assertion lines | 78; `diff` **empty** |
| Body lines, **without** reversing the rewrite | 976 vs 976 non-blank; **24** differ = 12 removed + 12 added |
| Body lines **after** reversing `mutationState.publish =` → `publishState =` | **0** differ |
| Each file run alone (SC-005) | `LayoutsPage` 9, `Archive` 16, `OneChain` 8, `Refusals` 6, `Unavailable` 6 = 45, all passed |
| Whole suite | 90 files, 684/684, **102.2 s** (+6.2 %) |
| File sizes (lines) | 156, 170, 221, 228, 392; harness 119. Cap was 420 (SC-003). |
| `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` | clean |
| `dotnet test tests/Architecture.Tests -c Release` | 3254/3254 |
| `git diff --name-only origin/develop...HEAD` outside the allowed set | none (FR-007) |

Every step in the stack was run before it was committed: after each move the new file and the shrunken
`LayoutsPage.test.tsx` were run separately and their counts matched the plan (16/29, 8/21, 6/15, 6/9), with `tsc`
and `eslint` silent. The working tree was clean after the last step (every generated file is now formatted each step).

**The hidden order dependence (plan §R5) did not appear.** *Archive confirmation* and its nested groups have no
`beforeEach` of their own and pass in a fresh file, so nothing relied on state left over from an earlier group. No
`beforeEach` was added anywhere.

## Counterfactuals: the harness is not vacuous (T005)

| Break in `layoutsPageHarness.tsx` (restored each time) | Result |
|---|---|
| remove the `useListLayoutsQuery` mock line | **42 of 45 tests fail** |
| the publish hook ignores `mutationState.publish` (reads a fixed `{ isLoading: false }`) | fails every test that sets it: the four stale-conflict tests and the Publish, Edit (new draft) and More-actions tests of spec 273 |

## What went wrong on the way (kept visible)

- **My expected segment count was wrong, not the script.** I wrote "10 describes + `ACTION_LABELS`"; there are 9
  top-level describes (the 10 groups in the table count a nested pair separately) plus `ACTION_LABELS` = 10
  segments. The script's own check caught my miscount on its first run.
- **A first check of nested names returned 0.** Vitest joins ancestor titles with a space, not ` > `, so my grep
  looked for a separator that does not exist. Checked the real format before trusting any number; the 11 are
  counted with the right separator, before and after.

## Not observed

- CI on Linux: the first run is the PR's.
- Whether the +6.2 % suite time is stable: one measurement each way (the unchanged tree measured 92.9 s, 94.9 s and
  96.3 s across the three splits today, so a few seconds is within what this machine does between runs).
