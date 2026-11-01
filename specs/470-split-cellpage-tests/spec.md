# Spec 470 — `CellPage.test.tsx` is split along the concerns its `describe` blocks already name

**Feature Branch**: `470-split-cellpage-tests`
**Created**: 2026-11-01
**Status**: Draft
**Issue:** #3197 (feature-level; found by the frontend refactoring survey; was blocked on #3196, which closed
2026-11-01 via #3201–#3203).
**ADRs:** ADR-0037 (phases), ADR-0052 (Vitest + hand-written fakes), ADR-0054 (hand-written builders),
ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** Test layout only; it follows the split families that already exist (`OverlayDraftForm*`,
`LayoutEditorDialog*`, `CameraViewer*`).
**Latency budget:** none — test-only; no production module changes (§Scope).
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the file being
split; every test must exist afterwards with its name and assertions unchanged.

## Problem

`apps/kiosk-web/src/features/cell/CellPage.test.tsx` is **3027 lines**, measured on `develop` (2026-11-01),
about twice the next-largest test file (`OverlaysPage.test.tsx`, 1549). #2321 split the hub handlers out of
`CellPage.tsx`; the tests were not split to match. It holds **66 tests** (66 passing, 14.6 s) in one
`describe('CellPage')`: 7 at its top level and 59 in 15 nested groups, each already named for one concern:

| Tests | Group (nested `describe`) |
|---|---|
| 7 | *(top level of `CellPage`)* |
| 1 | A mixed `[Box, Text]` overlay resolves by template, not position (spec 301) |
| 4 | live-update resilience (spec 011 US2) |
| 6 | per-tile overlay highlight (US3) |
| 2 | Holding a label back to the age of its own picture |
| 1 | A pushed resolved text reaching the tile that binds the overlay (#2012) |
| 5 | Applying only the wall's own fab (#2069) |
| 12 | A frame carrying no fab at all is a version skew (#2084), of which 7 under *Two absent fabs must not compare equal (#2320)* |
| 5 | An omitted `overlayIdentifier` says so instead of asking for the overlay collection (spec 141 site 1) |
| 4 | A layout with no fab says so before querying across fabs (spec 141 site 2) |
| 6 | A resolved-text push for a label the kiosk treats as static says so (spec 141 site 3) |
| 1 | A wall does no work when nothing changed (spec 204 SC-1) |
| 3 | A label is held for the age most recently reported (spec 204 SC-3/4/5) |
| 3 | Pushed overlay archive and publish frames (spec 229 US1, #2321) |
| 6 | The label set as a unit (spec 150) |

### What the issue did not say (premise check, 2026-11-01)

1. **The first ~515 lines are a preamble every test depends on**: about a dozen `vi.mock` calls
   (layouts/overlays/systemVariables/streams APIs, `kioskLatency`, `layoutHub`, `react-router-dom`,
   `react-oidc-context`, `CameraViewer`), module-level mock state (`getLayoutMock`, `capturedCallbacks`,
   `reportLagByCamera`, …) and builders (`tile`, `chain`, `mockLayout`, `renderPage`, `publishedOverlay*`).
   Vitest mocks are per file, so **a pure cut-and-paste is not possible**: each new file needs this setup, and
   how it is shared is the one real design question. It is settled in the plan, not here.
2. **The issue's `shard-N.filter` requirement does not apply.** Those entries are for xUnit test classes; Vitest
   discovers files itself (spec 468 plan recorded the same). The done criterion drops it.
3. **#3196 did not shrink this file.** It defines no local `jsonResponse`/`problemResponse` and builds no store
   by hand, so the "moving a test multiplies duplication" worry from the issue does not arise here.

## Scope

**In:** replace `CellPage.test.tsx` by several files in `apps/kiosk-web/src/features/cell/`, split along the
groups above, plus whatever shared setup the plan decides they need. Tests move; they are not rewritten.

**Out (explicitly):**
- `OverlaysPage.test.tsx` and `LayoutsPage.test.tsx` — they wait on #3190, which changes the pages under test.
- Any production (non-test) module, including `CellPage.tsx` and its hooks.
- Changing what a test asserts, renaming a test, adding coverage, or fixing a test that turns out to be wrong
  (a refactor that is also a bug fix is two issues, per CLAUDE.md).
- Other large test files (`LayoutsPage` 1228, `OverlayDraftForm` 959, `RuleDialog` 927, `WhepClient` 925).

## User Scenarios & Testing

### User Story 1 (P1) — Every test survives the split unchanged

As the maintainer, I want all 66 tests present after the split with the same names and the same assertions,
so the split provably only moves code.

**Why P1:** a split that silently drops or edits a test is worse than the big file; this is the whole
safety argument.

**Independent test:** compare test names (with status) and sorted assertion lines, before and after.

```gherkin
Scenario: no test is lost or added
  Given the 66 tests of CellPage.test.tsx recorded on the unsplit tree
  When the suite is run after the split
  Then the sorted list of full test names with status is identical
  And all 66 pass

Scenario: no assertion is edited
  Given the sorted list of assertion lines (expect(, .toHaveBeen…, .toBe…, .toEqual, .toThrow) before
  When the same list is taken from the new files
  Then the two lists are identical

Scenario: a test that depends on shared state still behaves the same alone and in the suite
  When each new file is run on its own and then the whole kiosk-web suite is run
  Then every file passes both ways
```

### User Story 2 (P2) — A reader finds a concern in a file named for it

As someone changing fab handling, label ageing or overlay frames, I want the tests for that concern in one
small file named after it, so I open one file instead of searching 3027 lines.

**Independent test:** each new file's name and its `describe` title agree, and no new file exceeds a size
stated in the plan.

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

- **Order and shared module state.** The mocks hold state across tests (`capturedCallbacks`,
  `reportLagByCamera`, `renderCountByCamera`). Per-file isolation is Vitest's default, but the setup must
  reset exactly what the big file reset, in the same hooks.
- **A mock factory that closes over a module-level `vi.fn()`.** `vi.mock` is hoisted above imports; moving the
  `vi.fn()` out of the file changes that. The plan must say how each factory keeps working.
- **Top-level tests (7)** belong to no named group; the plan assigns them by reading them.
- **`afterEach`/`beforeEach` at `CellPage` level** apply to every group today and must apply to each new file.

## Requirements

- **FR-001** All 66 tests exist after the split, each with its full name unchanged, each passing.
- **FR-002** No assertion line is edited: the sorted assertion-line list is identical before and after.
- **FR-003** `CellPage.test.tsx` no longer exists, or is reduced to one named concern; nothing of its content is
  left duplicated.
- **FR-004** Each new file holds one concern and is named for it, following `OverlayDraftForm*` /
  `LayoutGrid*` naming.
- **FR-005** Setup shared by more than one new file is defined once.
- **FR-006** No production file changes; `git diff --stat` shows only `apps/kiosk-web/src/features/cell/`
  test files, their shared setup, and `specs/470-…`.
- **FR-007** `eslint --max-warnings 0`, `tsc --noEmit` and `prettier --check` are clean for `kiosk-web`, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** Test names with status: identical set before and after (66 = 66).
- **SC-002** Sorted assertion lines: `diff` empty.
- **SC-003** No new test file exceeds the size limit set in the plan, and none is more than a small fraction of
  the original 3027 lines.
- **SC-004** The whole `kiosk-web` suite passes: 329 before this change (the count after #3203) and the same
  329 after.
- **SC-005** Each new file passes when run alone.

## Assumptions

- The nested `describe` titles are the intended concerns (the issue says so; the table above is read from the
  run, not from the source text).
- Moving a test between files does not change its outcome except through shared mock state, which the edge
  cases and SC-005 cover.
- Test titles include the enclosing `describe` names, so a renamed outer `describe` would change every full name;
  the plan must keep the title identical or state that the comparison uses the leaf names plus the group.

## Dependencies

#3196 (closed). Independent of #3190 as long as `OverlaysPage`/`LayoutsPage` tests are not touched.
