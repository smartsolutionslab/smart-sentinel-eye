# Spec 473 — `LayoutsPage.test.tsx` is split along the concerns its `describe` blocks already name

**Feature Branch**: `473-split-layoutspage-tests`
**Created**: 2026-11-01
**Status**: Draft
**Issue:** #3208 (feature-level; sibling of #3206 and #3197; was waiting on #3190, merged as #3205).
**ADRs:** ADR-0037 (phases), ADR-0052 (Vitest + hand-written fakes), ADR-0054 (hand-written builders),
ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** Test layout only; follows spec 470 (`cellPageHarness`) and spec 472 (`overlaysPageHarness`).
**Latency budget:** none — test-only; no production module changes.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the file being split;
every test must exist afterwards with its name and assertions unchanged.

## Problem

`apps/management-web/src/features/layouts/LayoutsPage.test.tsx` is **1228 lines**, measured on `develop`
(2026-11-01, after #3207), the largest test file in the repo now that `CellPage` and `OverlaysPage` are split.
It holds **45 tests** (45 passing) in 10 groups. Counts are read from a run, not from the source text:

| Tests | Group |
|---|---|
| 7 | `LayoutsPage` (empty state, filter group name, cards, publish click, retry on failure, tile summary, Edit on a Published chain) |
| 4 | the stale-conflict fallback (spec 231 US1) |
| 16 | archive confirmation — 5 own tests, **plus two nested groups**: *recovering an archived layout* (3) and *every chain shape offers something* (8) |
| 8 | archive and discard on one chain |
| 2 | Publish keeps focus while unavailable (spec 273 L1) |
| 2 | Edit (new draft) keeps focus while unavailable (spec 273 L2) |
| 2 | More actions trigger keeps focus while unavailable (spec 273 L3) |
| 2 | revocation fallback, three consecutive 403s (spec 310, #2725) |
| 2 | the state filters announce which one is on (spec 427 R10) |

Its siblings `LayoutsPageFocusRefetch` (203 lines) and `LayoutsPageRetryRecovery` (128) are already separate.

### What the issue and the specs 470 and 472 precedents do not cover (premise check, 2026-11-01)

1. **The shared preamble is ~120 lines** — seven `vi.fn` mocks, two `vi.mock`s (`layouts.api` and `cameras.api`),
   builders (`chain`, `response`, `renderPage`, `refusal`). Vitest mocks are per file, so each new file needs it;
   the sharing mechanism is the one spec 470 and spec 472 settled (a test-support module imported first), left to
   the plan to apply.
2. **It is not a byte-identical move, but the edit is small.** **12 test-body lines assign to the one mutable
   mock state** (`publishState = { … }`; one of them wraps over several lines). Everything else the tests share
   is read-only. An imported binding cannot be assigned from another module, so those 12 lines change to go through
   the shared module, exactly as in spec 472 but with one state variable instead of four.
3. **Two groups are nested inside *archive confirmation*** (*recovering an archived layout*, 3 tests, and
   *every chain shape offers something*, 8). A test's full name includes its ancestors, so moving a nested group
   out as a top-level group would drop `LayoutsPage — archive confirmation >` from its name and change it. The
   nested groups also sit inside the parent's scope (its local helpers). So the parent and its nested groups
   **move together**, and the nested structure is preserved.
4. **A module-level constant sits between groups.** `ACTION_LABELS` (line 738) is used only by the nested
   *every chain shape* group, so it must travel with that group's file.
5. **Two groups have no `beforeEach` of their own** (*archive confirmation* and its nested groups); in the single
   file they inherit mock state from earlier groups. As in spec 472, the check is to run each new file alone.
6. **No `shard-N.filter` entry**: Vitest discovers files itself (specs 468, 470, 472).

## Scope

**In:** replace `LayoutsPage.test.tsx` by several files in `apps/management-web/src/features/layouts/`, split
along the groups above (a parent and its nested groups together), plus the shared setup they need. Tests move;
they are not rewritten.

**Out (explicitly):**
- Any production (non-test) module, including `LayoutsPage.tsx`.
- Changing what a test asserts, renaming a test, adding coverage, or fixing a test that turns out to be wrong
  (a refactor that is also a bug fix is two issues, per CLAUDE.md).
- `LayoutsPageFocusRefetch` / `LayoutsPageRetryRecovery` and their local helpers (#3196's note): a separate
  commit after, so they are not edited while this is in flight.
- `OverlaysPage` tests (done in #3207).

## User Scenarios & Testing

### User Story 1 (P1) — Every test survives the split unchanged

As the maintainer, I want all 45 tests present afterwards with the same names and the same assertions, so the
split provably only moves code.

**Independent test:** compare test names (with status) and sorted assertion lines, before and after.

```gherkin
Scenario: no test is lost or added
  Given the 45 tests of LayoutsPage.test.tsx recorded on the unsplit tree
  When the suite is run after the split
  Then the sorted list of full test names with status is identical
  And all 45 pass

Scenario: nested tests keep their parent in their name
  Given a test in "recovering an archived layout" or "every chain shape offers something"
  When it runs from its new file
  Then its full name still begins "LayoutsPage — archive confirmation > LayoutsPage — …"

Scenario: no assertion is edited
  Given the sorted list of assertion lines (expect(, .toHaveBeen…, .toBe…, .toEqual, .toThrow) before
  When the same list is taken from the new files
  Then the two lists are identical

Scenario: the only edited non-assertion lines are the mock-state assignments
  Given the 12 lines that assign publishState
  When the body lines of the new files are compared with the old file
  Then every difference is one of those 12 lines (rewritten in the same way) or an import, and nothing else

Scenario: a test that depends on shared state behaves the same alone and in the suite
  When each new file is run on its own and then the whole management-web suite is run
  Then every file passes both ways
```

### User Story 2 (P2) — A reader finds a concern in a file named for it

As someone changing the layouts page's archive, refusal or focus behaviour, I want the tests for that concern in
one small file named after it.

```gherkin
Scenario: a concern has one home
  Given the groups in the table above
  When the split is complete
  Then each group lives in exactly one file, with its nested groups
  And that file's name says which concern it holds

Scenario: the shared setup exists once
  When two new files need the same mock or builder
  Then they take it from one place instead of each carrying a copy
```

### Edge cases

- **The 12 assignment sites**: the rewrite must preserve what each line sets and when; the mock factory must read
  the same state the tests write. One site's right-hand side wraps over several lines.
- **Nested groups use the parent's local helpers and `ACTION_LABELS`**: all of it moves as one unit.
- **The generated tests** (*every chain shape offers something*) produce 8 names from one loop; the comparison is
  on full names, so the loop moves whole.
- **Hoisted `vi.mock`**: both mocks must be registered before `store` or the page load the real slices (the
  harness is imported first).
- **Describe-level `beforeEach` and local helpers** apply only to their group today and must keep doing so.
- **Full test names** start with `LayoutsPage…`; each new file must keep every `describe` title exactly.

## Requirements

- **FR-001** All 45 tests exist after the split, each with its full name unchanged, each passing.
- **FR-002** No assertion line is edited: the sorted assertion-line list is identical before and after.
- **FR-003** Apart from assertion lines, only these lines differ: the 12 mock-state assignment sites and imports.
  The plan states the exact rewrite; the PR lists how many lines differ before and after reversing it.
- **FR-004** `LayoutsPage.test.tsx` no longer exists, or is reduced to one named concern; nothing of its content is
  left duplicated.
- **FR-005** Each new file holds one concern and is named for it, following `OverlaysPage*` / `CellPage*` naming;
  a parent group and its nested groups are never separated.
- **FR-006** Setup used by more than one new file is defined once.
- **FR-007** No production file changes; `git diff --stat` shows only
  `apps/management-web/src/features/layouts/` test files, their shared setup, and `specs/473-…` /
  `.specify/feature.json`.
- **FR-008** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `management-web`, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** Test names with status: identical set before and after (45 = 45).
- **SC-002** Sorted assertion lines: `diff` empty.
- **SC-003** No new test file exceeds the size limit set in the plan, and none is more than a fraction of the
  original 1228 lines.
- **SC-004** The whole `management-web` suite passes, with its count (684) and duration before and after.
- **SC-005** Each new file passes when run alone.
- **SC-006** A counterfactual shows the shared setup is not vacuous (breaking one mock in it fails tests in the
  new files).

## Assumptions

- The `describe` titles are the intended concerns (the table is read from the run).
- Moving a test between files does not change its outcome except through shared mock state, which the edge
  cases, SC-005 and SC-006 cover.
- Test titles include the enclosing `describe` names, so each new file must keep every group's title and nesting
  exactly.

## Dependencies

#3190 (merged as #3205); #3207 (merged), whose harness this follows. Independent of the focus-refetch and
retry-recovery helper migration.
