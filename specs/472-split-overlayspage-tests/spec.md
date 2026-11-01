# Spec 472 — `OverlaysPage.test.tsx` is split along the concerns its `describe` blocks already name

**Feature Branch**: `472-split-overlayspage-tests`
**Created**: 2026-11-01
**Status**: Draft
**Issue:** #3206 (feature-level; sibling of #3197, which split `CellPage.test.tsx`; was waiting on #3190,
merged as #3205).
**ADRs:** ADR-0037 (phases), ADR-0052 (Vitest + hand-written fakes), ADR-0054 (hand-written builders),
ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** Test layout only; follows spec 470 (`apps/kiosk-web/src/test/cellPageHarness.tsx`).
**Latency budget:** none — test-only; no production module changes.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the file being split;
every test must exist afterwards with its name and assertions unchanged.

## Problem

`apps/management-web/src/features/overlays/OverlaysPage.test.tsx` is **1549 lines**, measured on `develop`
(2026-11-01, after #3205), the largest test file in the repo since #3204 split `CellPage.test.tsx`. It holds
**56 tests** (56 passing) in 13 groups: one top-level `describe('OverlaysPage')` with 7 tests and 12 further
top-level `describe('OverlaysPage — …')` blocks, each already named for one concern:

| Tests | Group |
|---|---|
| 7 | `OverlaysPage` (empty state, filter group name, cards, label preview, publish click, retry on failure) |
| 5 | archive confirmation |
| 6 | recovering an archived overlay |
| 8 | every chain shape offers something (one generated test per chain shape) |
| 5 | archive and discard on one chain |
| 6 | a refused mutation is surfaced |
| 3 | editing a draft in place (spec 152 US1) |
| 4 | Publish and Discard draft keep focus while unavailable (spec 273 O1/O2) |
| 3 | Edit (new draft), Revert and Archive keep focus while unavailable (spec 273 O3/O4/O5) |
| 1 | Edit draft (:209) stays native disabled, out of scope (spec 273 [J1]) |
| 4 | navigating instead of opening a dialog (spec 305, #2350) |
| 2 | revocation fallback, three consecutive 403s (spec 310, #2725) |
| 2 | the state filters announce which one is on (spec 427 R10) |

Its siblings `OverlaysPageFocusRefetch` (179 lines) and `OverlaysPageRetryRecovery` (105) are already separate.
The counts are read from a run, not from the source text.

### What the issue and the spec 470 precedent do not cover (premise check, 2026-11-01)

1. **The shared preamble is small (~133 lines)** — five `vi.fn` mocks, one `vi.mock` of
   `@smart-sentinel-eye/shared/api/overlays.api`, builders (`chain`, `response`, `renderPage`, `refusal`) — but
   Vitest mocks are per file, so each new file needs it; the sharing mechanism is a plan decision (spec 470
   settled it with a test-support module imported first).
2. **It is not a byte-identical move.** **43 test-body lines assign to the mutable mock state** the page reads
   back (`publishState = { … }`, `archiveState`, `branchState`, `revertState`), including the reset lines in the
   groups' `beforeEach`. In spec 470 the shared bindings were only *read* by tests; here an imported binding
   cannot be assigned from another module, so those 43 lines must change to go through the shared module.
   This is the one place where non-assertion test lines are edited, and the plan must define the allowed edit
   precisely and the verification must prove nothing else moved.
3. **Describe-local helpers exist** (e.g. `publishedChain`, `showing`, `openConfirmation` inside the archive
   confirmation group) and several groups have their own `beforeEach`; they move with their group.
4. **No `shard-N.filter` entry**: Vitest discovers files itself (spec 468/470).

## Scope

**In:** replace `OverlaysPage.test.tsx` by several files in `apps/management-web/src/features/overlays/`, split
along the groups above, plus the shared setup they need. Tests move; they are not rewritten.

**Out (explicitly):**
- `LayoutsPage.test.tsx` (1228 lines): the same treatment, a separate issue and spec.
- Any production (non-test) module, including `OverlaysPage.tsx`.
- Changing what a test asserts, renaming a test, adding coverage, or fixing a test that turns out to be wrong
  (a refactor that is also a bug fix is two issues, per CLAUDE.md).
- `OverlaysPageFocusRefetch` / `OverlaysPageRetryRecovery` and their local helpers (#3196's note): a separate
  commit after, so they are not edited while this is in flight.

## User Scenarios & Testing

### User Story 1 (P1) — Every test survives the split unchanged

As the maintainer, I want all 56 tests present afterwards with the same names and the same assertions, so the
split provably only moves code.

**Independent test:** compare test names (with status) and sorted assertion lines, before and after.

```gherkin
Scenario: no test is lost or added
  Given the 56 tests of OverlaysPage.test.tsx recorded on the unsplit tree
  When the suite is run after the split
  Then the sorted list of full test names with status is identical
  And all 56 pass

Scenario: no assertion is edited
  Given the sorted list of assertion lines (expect(, .toHaveBeen…, .toBe…, .toEqual, .toThrow) before
  When the same list is taken from the new files
  Then the two lists are identical

Scenario: the only edited non-assertion lines are the mock-state assignments
  Given the 43 lines that assign publishState, archiveState, branchState or revertState
  When the body lines of the new files are compared with the old file
  Then every difference is one of those 43 lines (rewritten in the same way), a wrapper, an import or a
       reset-hook line, and nothing else

Scenario: a test that depends on shared state behaves the same alone and in the suite
  When each new file is run on its own and then the whole management-web suite is run
  Then every file passes both ways
```

### User Story 2 (P2) — A reader finds a concern in a file named for it

As someone changing archive, navigation or focus behaviour on the overlays page, I want the tests for that
concern in one small file named after it.

```gherkin
Scenario: a concern has one home
  Given the groups in the table above
  When the split is complete
  Then each group lives in exactly one file
  And that file's name says which concern it holds

Scenario: the shared setup exists once
  When two new files need the same mock or builder
  Then they take it from one place instead of each carrying a copy
```

### Edge cases

- **The 43 assignment sites** (above): the rewrite must preserve what each line sets and when; the mock factory
  must read the same state the tests write.
- **A mock factory that closes over module state.** `vi.mock` is hoisted; the state it reads must be reachable
  from the module that registers it (spec 470 spiked this pattern).
- **The generated tests** (`every chain shape offers something`) produce 8 names from one loop; the comparison
  is on full names, so the loop moves whole.
- **Describe-level `beforeEach` and local helpers** apply only to their group today and must keep doing so.
- **Full test names** start with `OverlaysPage…`; each new file must keep the same top-level `describe` titles.

## Requirements

- **FR-001** All 56 tests exist after the split, each with its full name unchanged, each passing.
- **FR-002** No assertion line is edited: the sorted assertion-line list is identical before and after.
- **FR-003** Apart from assertion lines, only these lines differ: the 43 mock-state assignment sites, group
  wrappers, imports, and lines of the reset hook that moves into the shared module. The plan states the exact
  rewrite; the PR lists the differing lines.
- **FR-004** `OverlaysPage.test.tsx` no longer exists, or is reduced to one named concern; nothing of its content
  is left duplicated.
- **FR-005** Each new file holds one concern and is named for it, following `OverlayDraftForm*` /
  `CellPage*` naming.
- **FR-006** Setup used by more than one new file is defined once.
- **FR-007** No production file changes; `git diff --stat` shows only
  `apps/management-web/src/features/overlays/` test files, their shared setup, and `specs/472-…` /
  `.specify/feature.json`.
- **FR-008** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `management-web`, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** Test names with status: identical set before and after (56 = 56).
- **SC-002** Sorted assertion lines: `diff` empty.
- **SC-003** No new test file exceeds the size limit set in the plan, and none is more than a fraction of the
  original 1549 lines.
- **SC-004** The whole `management-web` suite passes, with its count (684) and duration before and after.
- **SC-005** Each new file passes when run alone.
- **SC-006** A counterfactual shows the shared setup is not vacuous (breaking one mock in it fails tests in the
  new files).

## Assumptions

- The top-level `describe` titles are the intended concerns (the table is read from the run).
- Moving a test between files does not change its outcome except through shared mock state, which the edge
  cases, SC-005 and SC-006 cover.
- Test titles include the enclosing `describe` names, so the new files must keep each group's `describe` title
  exactly.

## Dependencies

#3190 (merged as #3205), which changed the page under test. Independent of `LayoutsPage.test.tsx`.
