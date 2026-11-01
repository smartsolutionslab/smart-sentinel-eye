# Implementation Plan: `CellPage.test.tsx` is split along the concerns its `describe` blocks already name

**Branch**: `470-split-cellpage-tests` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/470-split-cellpage-tests/spec.md`

## Summary

Move the 66 tests of `apps/kiosk-web/src/features/cell/CellPage.test.tsx` (3027 lines) into eight files of
roughly 150–600 lines, one per concern, by **moving the ~515-line preamble (mocks, mock state, builders) into one
test-support module** that each file imports first. Test bodies, doc comments and `describe` titles move
byte-for-byte; the only new lines are imports, a `describe('CellPage')` wrapper, and one reset call per file.
Behaviour-preserving, so **characterisation**, not red-first (ADR-0139/0144 phase 4a): the covering tests are the
file being split.

## Technical Context

- **Language/stack**: TypeScript, React 18, Vitest + Testing Library, `apps/kiosk-web` (jsdom, `globals: true`,
  `setupFiles: ./src/test/setup.ts`, `testTimeout: 30_000`).
- **Baseline (2026-11-01, `develop` at 405ad00e):** `CellPage.test.tsx` 66 passed (14.6 s); whole `kiosk-web`
  suite 25 files / 329 tests / ~20 s.
- **Touches**: test files and one test-support module in `apps/kiosk-web/src/` only. No production file, no
  `shared` change, no config change.
- **Latency budget**: none (test-only).

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | Applies and is the design: characterisation, assertions unmodified (SC-001/002). |
| No new framework / fixture library (ADR-0052/0054) | Holds: hand-written helpers, one module. |
| No ADR needed | Layout of tests, follows existing families. |
| Lane may not weaken a gate / delete a test | Holds: 66 = 66 is a success criterion, checked by diff. |

No violation; nothing to justify.

## Research (decisions that shaped the plan)

### R1 — Sharing the preamble: one test-support module, mocks registered in it

**Options.** (a) Copy the preamble into each of eight files: ~4000 duplicated lines, fails FR-005. (b) A
Vitest `setupFiles` entry: runs for all 25 files, not just these. (c) One module that **registers the
`vi.mock` calls and exports the state and builders**; each test file imports it first.

**Chosen: (c)** — `apps/kiosk-web/src/test/cellPageHarness.tsx` (beside the existing `setup.ts`; `.tsx` because
the `CameraViewer` double returns JSX).

**Evidence this works (spike, 2026-11-01, throwaway files, since removed):** a helper module that calls
`vi.mock('…/layouts.api', factory)` and re-exports the real `store` applies the mock to a later
`await import('@smart-sentinel-eye/shared/api/layouts.api')`, the factory's closure assignment is visible
through an exported `let` live binding, and the mocked hook is the one the module graph sees. No other file in
the repo registers `vi.mock` from a non-test module, so this is a new pattern; the spike is why it is chosen and
SC-005 (each file passes alone) is why it stays honest.

**Consequences.**
- The harness exports exactly what the old preamble defined (mock functions, `capturedCallbacks`, `reportLag`,
  the two maps, `MockOverlayElement`, `useSnapshotFromTheRealCache`, `tile`, `chain`, `publishedRevision`,
  `mockLayout`, `renderPage`, the `published*` overlay builders, `overlayDoubleDefault`, `resilienceLines`,
  `spyOnConsoleInfo`, `pushText`, `textFrameWithoutFab`, `layoutWithFab`) **and** a
  `resetCellPageMocks()` holding the body of today's top-level `beforeEach`. It also exports `store`
  (re-exported from `../app/store.js`).
- The harness does the `await import('../features/cell/CellPage.js')` itself (top-level await, after the
  mocks), and `renderPage` closes over it. A test file therefore never imports `CellPage` directly.
- Test bodies are **not edited**. The shared `let`s (`capturedCallbacks`, `reportLag`) are read in test bodies and
  assigned only in the old top-level `beforeEach` (checked: the only assignments are at old lines 524–525), so
  a live binding is enough and no body needs `harness.capturedCallbacks`.
- Each file declares `describe('CellPage', () => { beforeEach(resetCellPageMocks); … })`, so every full test name
  keeps its `CellPage > …` prefix and **full-name comparison stays exact** (the spec's open question: full
  names, not leaf names).
- Import order matters: the harness is the first import of every test file, so its `vi.mock` calls are
  registered before `store` or any API module is loaded.

### R2 — The split: eight files, counts read from a run

| New file | Holds (old groups) | Tests |
|---|---|---|
| `CellPage.test.tsx` (kept; blame survives) | the 7 top-level tests: grid rendering, spanning placement, bound label per tile, picker-prompt fallback | 7 |
| `CellPageWallFab.test.tsx` | Applying only the wall's own fab (#2069) | 5 |
| `CellPageFabSkew.test.tsx` | A frame carrying no fab at all is a version skew (#2084) incl. #2320 | 12 |
| `CellPageOmittedInputs.test.tsx` | spec 141 site 1 (omitted `overlayIdentifier`) + site 2 (layout with no fab) | 9 |
| `CellPageResolvedText.test.tsx` | mixed `[Box, Text]` (spec 301), pushed resolved text (#2012), spec 141 site 3 | 8 |
| `CellPageLabelAge.test.tsx` | Holding a label back to the age of its picture; label held for the age most recently reported (spec 204 SC-3/4/5) | 5 |
| `CellPageLiveUpdates.test.tsx` | live-update resilience (spec 011), per-tile highlight (US3), wall does no work when nothing changed (spec 204 SC-1), pushed archive/publish frames (spec 229) | 14 |
| `CellPageLabelSet.test.tsx` | The label set as a unit (spec 150) | 6 |
| **Total** | | **66** |

The two fab files stay apart because #2084 alone is ~400 lines. Groups keep their original relative order inside a
file. The text between groups is comment-only (checked), so no helper is left stranded; group-local helpers
(`highlightedTiles`, `aMunichWall`, `cycle`, …) sit inside their group and move with it.

**Size limit (SC-003):** no new file over **600 lines** including imports; estimated 150–450.

### R3 — The first-draft claims that are withdrawn

- `shard-N.filter`: not applicable to Vitest (spec §premise 2). No `tests/**/shard-*.filter` change.
- "Moving a test multiplies local `jsonResponse`": #3196 left this file with none.
- A new `OverlaysPage`/`LayoutsPage` split: out of scope, waits on #3190.

### R4 — Cost, stated

Eight files each load the harness and the real `store`, so the **suite will do the preamble's import work eight
times**. Vitest isolates per file, and the old file paid it once. Measured before: whole suite ~20 s. After is
recorded in the PR; an increase is expected and acceptable, a large one (> +25 %) is reported rather than hidden.

## Project Structure

```text
apps/kiosk-web/src/
  test/
    setup.ts                       (unchanged)
    cellPageHarness.tsx            (new: vi.mock calls, state, builders, resetCellPageMocks, renderPage)
  features/cell/
    CellPage.test.tsx              (shrinks from 3027 to ~200 lines: the 7 top-level tests)
    CellPageWallFab.test.tsx       (new)
    CellPageFabSkew.test.tsx       (new)
    CellPageOmittedInputs.test.tsx (new)
    CellPageResolvedText.test.tsx  (new)
    CellPageLabelAge.test.tsx      (new)
    CellPageLiveUpdates.test.tsx   (new)
    CellPageLabelSet.test.tsx      (new)
specs/470-split-cellpage-tests/
```

## Delivery: one PR, one commit per move

Every commit leaves the suite green (66 = 66), so `git bisect` stays honest:

1. `cellPageHarness.tsx` created; `CellPage.test.tsx` rewritten to import its preamble from it and call
   `resetCellPageMocks()`. All 66 tests still in the one file.
2. One commit per new file, each moving one row of the table out of `CellPage.test.tsx` (7 commits).
3. Task ticks.

A **mechanical extraction script** (scratchpad, like spec 468's `migrate.cjs`) cuts by `describe` line range and
**fails loudly** when a range does not start with the expected `describe(` or the moved text is not found; it
adds only the import block (computed from the identifiers each body uses) and the wrapper. It is not committed.

## Verification (what "unchanged" means, concretely)

Recorded on the unsplit tree, repeated after the last move:

- **T-A names:** sorted `file :: full name :: status` for the `CellPage*.test.tsx` files — must be the same 66
  names, differing only in the file column (compared on the name and status columns: `diff` empty).
- **T-A assertions:** sorted `expect(`, `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow` lines across
  the files — `diff` empty (SC-002).
- **T-A bodies (stronger than 468):** the sorted multiset of every line between the old file's
  `describe('CellPage'` and its end, versus the same over the new files minus the harness. The expected
  difference is only wrapper/hook/import lines; any other line is a failure. This catches a dropped comment or an
  edited non-assertion line that the first two lists would miss.
- **Each file alone** (SC-005) and then the whole `kiosk-web` suite (329 → 329), with duration noted (R4).
- **Counterfactual that the harness is not vacuous:** remove one `vi.mock` from the harness (the `layoutHub`
  one, which feeds `capturedCallbacks`); the push tests must fail, then restore (`git diff` empty). Without this,
  "all green" could mean the mocks silently stopped applying to the new files.
- `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `kiosk-web`; `dotnet test
  tests/Architecture.Tests -c Release` (they scan `.tsx`).

## Risks

| Risk | Mitigation |
|---|---|
| `vi.mock` from a module is new in this repo and could behave differently under another Vitest version or `isolate` setting | Spike passed; SC-005 and the counterfactual run in CI too; fallback if it ever breaks is per-file `vi.mock` calls delegating to harness factories. |
| A group relied on state left by an earlier group in the old file | None found (every group is covered by the top-level `beforeEach` reset); SC-005 runs each file alone to prove it. |
| An unused import after the cut fails `eslint --max-warnings 0` | The script computes each file's imports from the identifiers its body uses; lint and `tsc` gate it. |
| Slower suite (R4) | Measured and reported; not a gate. |
| Full test names change if a wrapper title differs | Wrapper is exactly `describe('CellPage'`; T-A names diff proves it. |
