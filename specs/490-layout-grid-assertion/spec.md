# Spec 490 — Make the kiosk grid-template assertion independent of jsdom's serialiser

**Feature Branch**: `490-layout-grid-assertion`
**Created**: 2026-11-10
**Status**: Draft
**Issue:** #3243 (feature-level). Unblocks #3240 (jsdom 30.1.3 → 31.0.0, spec 489).
**ADRs:** ADR-0036 (smallest change), ADR-0139 / ADR-0144 (phase 4a), ADR-0037 (phases), ADR-0053 (test naming).
**No new ADR.**
**Latency budget (constitution §IV): NOT on the path.** One test file in `kiosk-web`; no source file changes, so no leg between event
arrival and overlay render is touched. The PR says so.
**Phase 4a colour: a test-only change to an assertion whose subject did not move.** This is neither red/green (no behaviour changes) nor
a pure characterisation (an assertion is edited). The usual rule is that an assertion that has to be edited is evidence the behaviour
moved. Here it did not: the component's output is unchanged (`LayoutGrid.tsx:149–150`, same bytes); the **engine that reads it back**
changed. The spec therefore replaces "unmodified" with a stronger, checkable claim: the edited assertion passes on **both** jsdom
30.1.3 and 31.0.0, and **fails** on three counterfactuals that change what the component really does.

## Problem

`apps/kiosk-web/src/features/cell/LayoutGridPage.test.tsx`, the test "lays the grid out with the published revision columns and rows",
reads the style back through `grid.style.gridTemplateColumns` and `grid.style.gridTemplateRows` and compares each with the literal
`'repeat(N, minmax(0, 1fr))'`. That literal is jsdom 30's echo of the string the component wrote.

With jsdom 31.0.0 (new integrated CSS engine; "CSS serialization and computed values can differ from previous releases") the same
string reads back as `repeat(3, minmax(0px, 1fr))`, and the test fails (spec 489 `verification.md`, reproduced alone, the only failure in
2,705 tests). The test is pinned to an engine's spelling of a zero length, not to what the component does.

**What the assertion is for, and must keep.** It pins that the grid has the published revision's column count and row count, and that
each track is `minmax(0, 1fr)`: the `0` minimum is what stops a wide tile from forcing its track wider than its share. A weakened
assertion that only checked the counts would silently drop that second guarantee, so the track shape stays pinned.

## Scope

**In:** the two assertions in that one test, plus, if needed, a small helper local to the test file.

**Out (explicitly):** any production file (`LayoutGrid.tsx` and everything else); every other test; the jsdom bump itself (#3240);
`@microsoft/signalr`.

## User Scenarios & Testing

### User Story 1 (P1) — the test pins the layout, not the engine's spelling

```gherkin
Scenario: the same component on either engine
  Given LayoutGrid renders a 3-column, 2-row published revision
  When the grid's template columns and rows are read back on jsdom 30.1.3 and on jsdom 31.0.0
  Then the test passes on both

Scenario: a wrong column or row count
  Given the same test and a component that lays out the wrong number of columns or rows
  When the test runs
  Then it fails

Scenario: a changed track shape
  Given a component whose tracks are no longer minmax(0, 1fr)
  When the test runs
  Then it fails
```

## Edge Cases

- **jsdom 31 spells other lengths differently too** (for example `0` as `0px`, possibly `0fr`, case, spacing). The edit normalises the
  **zero length only**, and states why; it does not blanket-normalise, which would let a real change through.
- **The rows assertion** was not reached on jsdom 31 because the first `expect` throws. It is expected to differ the same way; the
  after-change run on 31 observes it rather than assuming.
- **Observing jsdom 31 without committing the bump.** The bump is applied temporarily (the saved patch of spec 489), the file is run, and
  the tree is restored from git; the committed change stays valid on jsdom 30.1.3.

## Requirements

- **FR-001** Only `apps/kiosk-web/src/features/cell/LayoutGridPage.test.tsx` changes: the two assertions and, if needed, one helper
  local to that file. No production file, no other test, no manifest.
- **FR-002** The assertions still pin the column count, the row count and the `minmax(0, 1fr)` track shape.
- **FR-003** They pass on jsdom **30.1.3** (the version on `develop`) and on jsdom **31.0.0** (observed with the bump temporarily
  applied, then restored from git). The file's other 11 tests pass on both.
- **FR-004** They fail on three counterfactuals, each an exactly-once substitution, restored from git afterwards, observed on 30.1.3:
  (a) the test data's `cols: 3` changed to a different count against the unchanged assertion; (b) the test data's `rows: 2` likewise;
  (c) the track shape in `LayoutGrid.tsx` changed from `minmax(0, 1fr)` to `1fr` (a production-file substitution used only as a
  probe, never committed).
- **FR-005** `eslint --max-warnings 0`, `tsc --noEmit` and `prettier --check` are clean in `kiosk-web`; the `kiosk-web` suite passes
  with the same file and test counts as before (39 files, 392 tests).
- **FR-006** The edit states, in a comment, **why** the zero length is normalised (a CSS engine may serialise `0` as `0px`), so it is not
  later "simplified" back to a literal. Comments say why, not what (ADR-0036).
- **FR-007** The PR states that no §IV leg is touched, that no production file changed, and that this unblocks #3240.

## Success Criteria

- **SC-001** The test passes on 30.1.3 and 31.0.0 with the file's other tests unchanged in outcome.
- **SC-002** All three counterfactuals make the test fail on 30.1.3.
- **SC-003** The diff against `origin/develop` is `LayoutGridPage.test.tsx` plus the spec documents.

## Assumptions

- The saved bump patch from spec 489 (five `jsdom` pins and the lockfile) reproduces the jsdom 31.0.0 environment; if `develop` has moved
  the lockfile since, the bump is re-made with `pnpm install --lockfile-only`.
- No other test in the repository reads a zero length back through jsdom's style serialiser; spec 489's after-bump run found this the only
  failure across all five apps.

## Dependencies

Unblocks #3240. Independent of every other open spec. #3240's PR rebases onto this one, and the local branch `489-jsdom-31` carries its
spec, plan, tasks and verification documents.
