# Verification 472 — `OverlaysPage.test.tsx` is split along the concerns its `describe` blocks already name

Phase 5 (ADR-0037). Observed on branch `472-split-overlayspage-tests` on 2026-11-01, base `develop` at f249f604.
Test-only change, phase 4a colour **characterisation**: no red test; the evidence is that the same tests pass in
their new homes and that the only edited non-assertion lines are the 43 the plan allowed.

## Before (T001, unsplit tree)

| Record | Value |
|---|---|
| `OverlaysPage.test.tsx` | 1549 lines, 56 tests, 56 passed |
| Sorted assertion lines | 87 |
| Non-blank lines from the first `describe(` (line 134) on | 1248 |
| Lines matching `^\s*(publish\|archive\|branch\|revert)State = ` | 43 |
| Whole `management-web` suite | 81 files, 684 tests, 90.6 s |

## After (T011)

| Check | Result |
|---|---|
| Test names with status, six files, file column excluded | 56; `diff` against before **empty** |
| Sorted assertion lines | 87; `diff` **empty** |
| Body lines, **without** reversing the rewrite | 1248 vs 1248 non-blank; **86** differ = 43 removed + 43 added |
| Body lines **after** reversing `mutationState.<x> =` → `<x>State =` | **0** differ |
| Each file run alone (SC-005) | `OverlaysPage` 9, `Archive` 10, `ChainActions` 14, `Refusals` 8, `Unavailable` 8, `Navigation` 7 = 56, all passed |
| Whole suite | 86 files, 684/684, **94.9 s** (+4.8 %) |
| File sizes (lines) | 184, 237, 248, 256, 271, 289; harness 136. Cap was 400 (SC-003). |
| `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` | clean |
| `dotnet test tests/Architecture.Tests -c Release` | 3254/3254 |
| `git diff --name-only origin/develop...HEAD` outside the allowed set | none (FR-007) |

Every step in the stack was run before it was committed: after each move the new file and the shrunken
`OverlaysPage.test.tsx` were run separately and their counts matched the plan (10/46, 14/32, 8/24, 8/16, 7/9),
with `tsc` and `eslint` silent.

**The hidden order dependence (plan §R4) did not appear.** The two groups with no `beforeEach` of their own
(*archive confirmation*, *every chain shape*) pass in a fresh file, so nothing relied on state left over from an
earlier group. No `beforeEach` was added anywhere.

## Counterfactuals: the harness is not vacuous (T005)

| Break in `overlaysPageHarness.tsx` (restored each time) | Result |
|---|---|
| remove the `useListOverlaysQuery` mock line | **52 of 56 tests fail** |
| archive hook reads `mutationState.publish` instead of `.archive` | **1 test fails**: `Surfaces a refusal from any of the four mutations, not only publish` |

The plan expected "the archive tests that set archive state" to fail; in fact only one test sets archive state to
anything but the default (the other eight assignments are resets to `{ isLoading: false }`), so one failure is
the correct count, and the harness's `mutationState.archive` read is covered by exactly that test.

## What went wrong on the way (kept visible)

- **My first body comparison compared almost nothing.** It started each file at its first `describe(`, which drops
  the doc comment above it, and the next version looked for a multi-line import that Prettier keeps on one line,
  so it matched nothing and printed a wall of "missing" lines. Both were flaws in the check, not in the split.
  Replaced by `body472.cjs`, which finds the last import statement of each file; its result above is the one that counts.
- **The extraction script's own safety check fired once** on a doc comment that mentions `publishState` in prose
  ("Flipping `publishState`…"). Correct outcome: comments are kept verbatim, so the check now looks at code only.
- **Moved files were left unformatted in the working tree** after later steps, because the script regenerates every
  moved file each step but `prettier --write` ran only on the current one. Committed versions were formatted; caught
  by `git status` after the last step, fixed by formatting everything (tree back to the committed state).

## Not observed

- CI on Linux: the first run is the PR's.
- Whether the +4.8 % suite time is stable: one measurement each way.
