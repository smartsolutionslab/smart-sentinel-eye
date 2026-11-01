# Implementation Plan: `LayoutsPage.test.tsx` is split along the concerns its `describe` blocks already name

**Branch**: `473-split-layoutspage-tests` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/473-split-layoutspage-tests/spec.md`

## Summary

Move the 45 tests of `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` (1228 lines) into five files of
roughly 150–400 lines, one per concern, by moving the ~120-line preamble (mocks, mock state, builders) into one
test-support module each file imports first — the pattern of specs 470 and 472. **12 test-body lines assign to the one
mutable mock state** (`publishState`), so those lines get one mechanical rewrite (R2); nothing else in a test body
changes. A parent group and its nested groups move together (R3). Behaviour-preserving, so **characterisation**, not
red-first (ADR-0139/0144 phase 4a).

## Technical Context

- **Stack**: TypeScript, React 19, Vitest + Testing Library, `apps/management-web` (jsdom,
  `setupFiles: ./src/test/setup.ts`).
- **Baseline (2026-11-01, `develop` at 0a4ab03c):** `LayoutsPage.test.tsx` 1228 lines, 45 tests passing; whole
  `management-web` suite 86 files / 684 tests / ~92 s.
- **Touches**: test files and one test-support module under `apps/management-web/src/`. No production file, no
  `shared`, no config.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | The design: characterisation, assertions unmodified (SC-001/002). |
| ADR-0036 — smallest change | One support module; the only body edit is the 12-line state rewrite, forced by ES-module semantics. |
| Lane may not weaken a gate / delete a test | 45 = 45 is a success criterion, checked by diff. |
| No new ADR | Test layout, follows specs 470 and 472. |

No violation.

## Research (decisions that shaped the plan)

### R1 — Sharing the preamble: a support module that registers the mocks

`apps/management-web/src/test/layoutsPageHarness.tsx` (beside `overlaysPageHarness.tsx` and `setup.ts`) holds the
seven `vi.fn` mocks, `refusal`, both `vi.mock`s (`layouts.api`, `cameras.api`), the dynamic import of `LayoutsPage`,
`chain`, `response` and `renderPage`, all exported. Every test file imports it **first**. **`LayoutsPage` is exported
too: several test bodies render `<LayoutsPage />` directly** (the revocation-fallback group builds its own router
wrapper), so a harness that only exported `renderPage` would leave them with no component. SC-006 re-proves the
pattern by counterfactual.

Rejected: copying the ~60-line mock block into each of five files (leaves bodies byte-identical but duplicates the
setup five times, FR-006).

### R2 — The 12 assignment sites: one mechanical rewrite, nothing else

The tests assign `publishState = { … }` to a module variable the `vi.mock` factory reads
(`usePublishRevisionMutation: () => [publishMock, publishState]`). The other mutation hooks in the factory return
fixed `{ isLoading: false }` literals, so **there is exactly one state variable**. The harness exports
`mutationState: { publish: { isLoading: boolean; error?: unknown } }` and the factory reads
`mutationState.publish`.

**The allowed edit, exactly:** a line whose first token is `publishState =` has that identifier replaced by
`mutationState.publish`; the right-hand side and everything after it is untouched (the site at line 336 wraps its
object over several lines; the continuation is not touched). **Checked on `develop`:** the 12 sites are the only
places the name appears in test bodies besides prose in comments; the declaration (line 25) and the factory read
(line 41) are in the preamble that moves; no test body *reads* `publishState`.

Verification reverses the rewrite (`mutationState.publish =` → `publishState =`) before comparing body lines, so a
stray edit elsewhere cannot hide behind the 12. Comments that mention `publishState` stay verbatim (the extraction
script's check looks at code only, as in spec 472).

### R3 — Nested groups move with their parent

*Archive confirmation* contains two nested `describe`s (*recovering an archived layout*, 3 tests; *every chain shape
offers something*, 8) at indent 2, closing at lines 640 and 734, before the parent closes at 735. A test's full name
includes its ancestors, so lifting a nested group out would change `LayoutsPage — archive confirmation > LayoutsPage —
… > test` to `LayoutsPage — … > test`. They also use the parent's local helpers. So the **whole parent
(16 tests) is one unit** and the nested structure is preserved byte for byte.

`const ACTION_LABELS` (line 738, with its one-line doc comment) sits at module level **after** the parent group and is
used only inside the nested chain-shape group. It travels with the parent: in the new file it stays right after the
group, in the same relative position. The extraction script treats it as a segment attached to the Archive file and
**fails if any other group references `ACTION_LABELS`**.

### R4 — The split: five files, counts read from a run

| New file | Holds (relative order kept) | Tests |
|---|---|---|
| `LayoutsPage.test.tsx` (kept; blame survives) | `LayoutsPage` (empty state, filter group name, cards, publish, retry, tile summary, Edit on a Published chain); the state filters announce which one is on (spec 427 R10) | 7 + 2 = 9 |
| `LayoutsPageArchive.test.tsx` | archive confirmation **with its two nested groups** (5 + 3 + 8), then `ACTION_LABELS` | 16 |
| `LayoutsPageOneChain.test.tsx` | archive and discard on one chain | 8 |
| `LayoutsPageRefusals.test.tsx` | the stale-conflict fallback (spec 231 US1); revocation fallback, three 403s (spec 310) | 4 + 2 = 6 |
| `LayoutsPageUnavailable.test.tsx` | keeps focus while unavailable: Publish (273 L1); Edit (new draft) (273 L2); More actions trigger (273 L3) | 2 + 2 + 2 = 6 |
| **Total** | | **45** |

Every group keeps its exact `describe` title and nesting (full names unchanged: R6). Describe-local helpers and each
group's own `beforeEach` move with their group. **Size limit (SC-003): no new file over 420 lines** including
imports; the Archive file is the largest (parent ≈ 375 lines + `ACTION_LABELS` + imports ≈ 395).

### R5 — A hidden order dependence to check, not assume

*Archive confirmation* (and its nested groups) has no `beforeEach` of its own, so in the single file it inherits mock
state (`archiveMock` call counts, `publishState`) from earlier groups. In a fresh file it starts clean. The checks:
every new file is run **alone** (SC-005); if a group fails alone, the dependence is reported and any `beforeEach`
added is **stated in the PR as a change**, not hidden. (Spec 472's two analogous groups passed fresh; this is
checked again, not assumed.)

### R6 — Full names

Test names include the enclosing `describe` titles. Each group keeps its exact title and nesting, so every full name
is unchanged and the name comparison is exact (file column excluded).

### R7 — Not in this plan

No `shard-N.filter` (Vitest). No production file. No change to `LayoutsPageFocusRefetch` /
`LayoutsPageRetryRecovery`. No `resetMutationState()` helper (it would edit bodies beyond the 12-line rule).

## Project Structure

```text
apps/management-web/src/
  test/
    setup.ts                          (unchanged)
    overlaysPageHarness.tsx           (unchanged)
    layoutsPageHarness.tsx            (new: mocks, mutationState, refusal, both vi.mocks, chain, response, renderPage, LayoutsPage)
  features/layouts/
    LayoutsPage.test.tsx              (shrinks 1228 → ~240: 9 tests)
    LayoutsPageArchive.test.tsx       (new)
    LayoutsPageOneChain.test.tsx      (new)
    LayoutsPageRefusals.test.tsx      (new)
    LayoutsPageUnavailable.test.tsx   (new)
specs/473-split-layoutspage-tests/
```

## Delivery: one PR, commits that each stay green

1. `layoutsPageHarness.tsx` created; `LayoutsPage.test.tsx` rewritten to import from it, with the 12-line rewrite
   (R2). All 45 tests still in the one file.
2. Four commits, each moving one row of R4 out (two files touched per commit).
3. Task ticks and the verification note.

A mechanical extraction script (scratchpad, not committed, adapted from spec 472's) cuts by top-level segment
(`describe` blocks with their leading comments, plus the `ACTION_LABELS` segment), **fails loudly** if a range does not
start with the expected title or end on `});`, applies only the R2 rewrite and asserts it hit exactly 12 lines, and
computes imports from the identifiers each file's code uses. Lessons carried over from specs 470 and 472:
`prettier --write` **every** generated file, including earlier moved ones, each step (the hook rejects unformatted
files and the script regenerates them all); `git add` exactly the files of that step; run Vitest from
`apps/management-web`; compare bodies with a script that finds the last import statement (not a regex on a
multi-line import).

## Verification (what "unchanged" means, concretely)

Recorded on the unsplit tree, repeated after the last move:

- **T-A names:** sorted `full name :: status` across the five files — 45, identical, file column excluded; the 11
  nested names must still begin `LayoutsPage — archive confirmation > `.
- **T-A assertions:** sorted `expect(`, `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow` lines — `diff` empty.
- **T-A bodies:** the multiset of non-blank lines from the first `describe(` to the end of the old file versus the
  same over the new files, with each file's imports excluded. **Exactly 24 lines differ before reversal** (12
  removed + 12 added), all matching the R2 pattern, and **0 differ after reversing it**.
- **Each file alone** (SC-005), then the whole `management-web` suite (684 → 684), with duration before/after.
- **Counterfactuals that the harness is not vacuous (SC-006):** remove the `useListLayoutsQuery` mock line from the
  harness — tests in every new file must fail; and make `usePublishRevisionMutation` ignore `mutationState.publish`
  — the stale-conflict tests that set it must fail. Both restored (`git diff` empty).
- `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `management-web`;
  `dotnet test tests/Architecture.Tests -c Release` (they scan `.tsx`).

## Risks

| Risk | Mitigation |
|---|---|
| The 12-line rewrite touches something it should not | Applied by script with an exact pattern and count assertion; reversed before the body comparison. |
| Nested names change if a group is lifted out | They are never separated from their parent (R3); the name diff and the 11-name prefix check catch it. |
| `ACTION_LABELS` ends up in a file that does not use it, or is missing from one that does | The script asserts which groups reference it; lint (`no-unused-vars`) and `tsc` catch both. |
| A group relied on leftover state (R5) | Each file run alone; reported if found. |
| Slower suite (five files load the harness and the real store) | Measured and reported, not a gate (spec 472: +4.8 %). |
