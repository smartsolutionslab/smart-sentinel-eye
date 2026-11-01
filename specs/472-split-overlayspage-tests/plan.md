# Implementation Plan: `OverlaysPage.test.tsx` is split along the concerns its `describe` blocks already name

**Branch**: `472-split-overlayspage-tests` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/472-split-overlayspage-tests/spec.md`

## Summary

Move the 56 tests of `apps/management-web/src/features/overlays/OverlaysPage.test.tsx` (1549 lines) into six files of
roughly 200–300 lines, one per concern, by moving the ~133-line preamble (mocks, mock state, builders) into one
test-support module each file imports first — the pattern spec 470 settled and proved. Unlike spec 470, **43 test-body
lines assign to the mock state**, so those lines get one mechanical rewrite (R2); nothing else in a test body changes.
Behaviour-preserving, so **characterisation**, not red-first (ADR-0139/0144 phase 4a).

## Technical Context

- **Stack**: TypeScript, React 19, Vitest + Testing Library, `apps/management-web` (jsdom,
  `setupFiles: ./src/test/setup.ts`).
- **Baseline (2026-11-01, `develop` at f249f604):** `OverlaysPage.test.tsx` 1549 lines, 56 tests passing; whole
  `management-web` suite 81 files / 684 tests / ~92 s.
- **Touches**: test files and one test-support module under `apps/management-web/src/`. No production file, no
  `shared`, no config.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | The design: characterisation, assertions unmodified (SC-001/002). |
| ADR-0036 — smallest change | One support module; the only body edit is the 43-line state rewrite, forced by ES-module semantics. |
| Lane may not weaken a gate / delete a test | 56 = 56 is a success criterion, checked by diff. |
| No new ADR | Test layout, follows spec 470. |

No violation.

## Research (decisions that shaped the plan)

### R1 — Sharing the preamble: a support module that registers the mock (spec 470's pattern)

`apps/management-web/src/test/overlaysPageHarness.tsx` (beside `setup.ts`) holds the `vi.fn` mocks, the mutation
state, `refusal`, the `vi.mock('…/overlays.api')`, the dynamic import of `OverlaysPage`, `chain`, `response` and
`renderPage`, all exported. Every test file imports it **first**, so the mock is registered before `store` or the
page load the real slices (spec 470 spiked and proved this in `kiosk-web`; SC-006 re-proves it here by
counterfactual). `OverlaysPage` is exported too because one group's local router helper renders it directly.

Rejected: copying the ~55-line mock block into each of six files. It leaves the 43 body lines byte-identical but
duplicates the setup six times (FR-006) and invites the copies to drift.

### R2 — The 43 assignment sites: one mechanical rewrite, nothing else

The tests assign `publishState = { isLoading: true }` (and `archiveState`, `branchState`, `revertState`) to module
variables the `vi.mock` factory reads. An imported binding cannot be assigned from another module, so the harness
exports one mutable object and the factory reads from it:

```ts
export const mutationState: Record<'publish' | 'archive' | 'branch' | 'revert', { isLoading: boolean; error?: unknown }> = {
  publish: { isLoading: false }, archive: { isLoading: false }, branch: { isLoading: false }, revert: { isLoading: false },
};
// factory: usePublishOverlayRevisionMutation: () => [publishMock, mutationState.publish], …
```

**The allowed edit, exactly:** a line whose first token is `publishState =`, `archiveState =`, `branchState =` or
`revertState =` has that identifier replaced by `mutationState.publish`, `.archive`, `.branch`, `.revert`; the
right-hand side and everything after it is untouched (one site's right-hand side wraps to the next line; that
continuation is not touched). **Checked on `develop`:** the 43 sites are the only places the four names appear in
test bodies — the other occurrences are the four declarations (lines 25–28) and the four factory reads (lines
40–43), both in the preamble that moves. No test body *reads* any of them.

The verification reverses the rewrite (`mutationState.(publish|archive|branch|revert) =` → `<name>State =`) before
comparing body lines, so a stray edit elsewhere cannot hide behind the 43.

### R3 — The split: six files, counts read from a run

| New file | Holds (old groups, relative order kept) | Tests |
|---|---|---|
| `OverlaysPage.test.tsx` (kept; blame survives) | `OverlaysPage` (empty state, filter group name, cards, preview, publish, retry); the state filters announce which one is on (spec 427 R10) | 7 + 2 = 9 |
| `OverlaysPageArchive.test.tsx` | archive confirmation; archive and discard on one chain | 5 + 5 = 10 |
| `OverlaysPageChainActions.test.tsx` | recovering an archived overlay; every chain shape offers something | 6 + 8 = 14 |
| `OverlaysPageRefusals.test.tsx` | a refused mutation is surfaced; revocation fallback, three 403s (spec 310) | 6 + 2 = 8 |
| `OverlaysPageUnavailable.test.tsx` | keep focus while unavailable: Publish/Discard (273 O1/O2); Edit/Revert/Archive (273 O3/O4/O5); Edit draft stays native disabled (273 J1) | 4 + 3 + 1 = 8 |
| `OverlaysPageNavigation.test.tsx` | editing a draft in place (spec 152 US1); navigating instead of opening a dialog (spec 305) | 3 + 4 = 7 |
| **Total** | | **56** |

Every group keeps its `describe` title exactly (full names unchanged: R5). Describe-local helpers
(`publishedChain`, `showing`, `openConfirmation`, …) and the groups' own `beforeEach` move with their group.
**Size limit (SC-003): no new file over 400 lines** including imports; estimated 200–300.

### R4 — A hidden order dependence to check, not assume

Two groups — *archive confirmation* and *every chain shape offers something* — have **no `beforeEach` of their own**.
In the single file they inherit mock state (call counts of `archiveMock`/`publishMock`, the four states) from
whatever ran before them. In a new file they start from a fresh module. If a test silently depended on a leftover,
splitting would change its outcome. The checks: every new file is run **alone** (SC-005), the whole suite is run,
and if a group fails alone the dependence is reported and the fix is a `beforeEach` on that group **stated in the PR
as a change**, not hidden. (Placing each of the two groups first in its file, as the table does, is by luck of
ordering, not a guarantee, so the run decides.)

### R5 — Full names

Test names include the enclosing `describe` titles. Each group keeps its exact title and sits at the top level, so
every full name is unchanged and the name comparison is exact (file column excluded).

### R6 — Not in this plan

No `shard-N.filter` (Vitest). No `LayoutsPage.test.tsx` (separate issue). No production file. No change to
`OverlaysPageFocusRefetch` / `OverlaysPageRetryRecovery`.

## Project Structure

```text
apps/management-web/src/
  test/
    setup.ts                          (unchanged)
    overlaysPageHarness.tsx           (new: mocks, mutationState, refusal, vi.mock, chain, response, renderPage)
  features/overlays/
    OverlaysPage.test.tsx             (shrinks 1549 → ~250: 9 tests)
    OverlaysPageArchive.test.tsx      (new)
    OverlaysPageChainActions.test.tsx (new)
    OverlaysPageRefusals.test.tsx     (new)
    OverlaysPageUnavailable.test.tsx  (new)
    OverlaysPageNavigation.test.tsx   (new)
specs/472-split-overlayspage-tests/
```

## Delivery: one PR, commits that each stay green

1. `overlaysPageHarness.tsx` created; `OverlaysPage.test.tsx` rewritten to import from it, with the 43-line rewrite
   (R2). All 56 tests still in the one file.
2. Five commits, each moving one row of R3 out (two files touched per commit).
3. Task ticks and the verification note.

A mechanical extraction script (scratchpad, not committed, like spec 470's) cuts by top-level `describe` range,
**fails loudly** if a range does not start with the expected title or end on a `});`, applies only the R2 rewrite
and adds imports. Generated files go through `prettier --write` (the pre-commit hook rejects unformatted files);
commits `git add` exactly the files of that step (spec 470 lesson: `git checkout` restores from the index).

## Verification (what "unchanged" means, concretely)

Recorded on the unsplit tree, repeated after the last move:

- **T-A names:** sorted `full name :: status` across the six files — 56, identical, file column excluded.
- **T-A assertions:** sorted `expect(`, `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow` lines — `diff` empty.
- **T-A bodies:** the sorted multiset of every line from the first `describe(` to the end of the old file versus the
  same over the new files, **after reversing R2's rewrite**. The only differences allowed: added/removed group
  imports, `describe` wrappers are none (groups are top-level), so expected difference is empty bar import lines
  outside the compared region. Any other differing line is a failure and is listed in the PR. Also: exactly 43 lines
  differ **before** reversal, all matching the R2 pattern.
- **Each file alone** (SC-005), then the whole `management-web` suite (684 → 684), with duration before/after.
- **Counterfactual that the harness is not vacuous (SC-006):** remove the `useListOverlaysQuery` mock line from the
  harness; tests in every new file must fail; restore (`git diff` empty). And remove one `mutationState` read from
  the factory (`mutationState.archive`): the archive tests that set it must fail.
- `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `management-web`;
  `dotnet test tests/Architecture.Tests -c Release` (they scan `.tsx`).

## Risks

| Risk | Mitigation |
|---|---|
| The 43-line rewrite touches something it should not | Applied by script with an exact pattern and count assertion; reversed before the body comparison; the 43 are listed in the PR. |
| `vi.mock` from a module behaves differently here | Spec 470 proved it in `kiosk-web`; SC-006's counterfactuals prove it for this module. |
| A group relied on leftover state (R4) | Each file run alone; reported if found. |
| Slower suite (six files load the harness and the real store) | Measured and reported, not a gate (spec 470: +20 %). |
| Unused imports fail `eslint --max-warnings 0` | The script computes each file's imports from the identifiers its code uses (comments and strings stripped); lint gates. |
