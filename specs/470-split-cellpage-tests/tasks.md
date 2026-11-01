# Tasks 470 — `CellPage.test.tsx` is split along the concerns its `describe` blocks already name

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3197 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation** (no red-first test; the
covering tests are the file being split and must pass with every assertion unmodified).

One PR, one commit per move; every commit leaves the suite green. Scripts (from the repo root):
`npm run -w kiosk-web test | lint | typecheck`, `npm run format:check`. Scratchpad files are not committed.

## Phase 1 — Record the "before" (T-A)

- [ ] **T001 [US1]** (frontend) On the unsplit tree (`develop` + the spec commits), record into the scratchpad:
  (a) `vitest run … CellPage.test.tsx --reporter=json` → sorted `full name :: status` (must be 66, all passing);
  (b) sorted assertion lines (`expect(`, `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow`) of the file;
  (c) sorted multiset of every line from `describe('CellPage'` to end of file;
  (d) whole `kiosk-web` suite: 25 files / 329 tests, and its duration (plan §R4).
  Depends on nothing.

## Phase 2 — The shared setup

### User story 2 (P2) — the shared setup exists once (FR-005)

- [ ] **T002 [US2]** (frontend) Write the extraction script in the scratchpad
  (`split470.cjs`): cuts by `describe(` line range, fails loudly if a range does not start with the expected
  title or ends off a `});`, computes each new file's import list from the identifiers its body uses, and adds
  only imports + the `describe('CellPage', () => { beforeEach(resetCellPageMocks); … })` wrapper. Not committed.
  Depends on T001.
- [ ] **T003 [US2]** (frontend) Create `apps/kiosk-web/src/test/cellPageHarness.tsx` from old lines 1–514
  **verbatim** (the `vi.mock` calls, mock state, `MockOverlayElement`, builders, `pushText`,
  `textFrameWithoutFab`, `layoutWithFab`), adding only `export`s, the `await import('../features/cell/CellPage.js')`
  inside it, `renderPage` closing over it, `export { store }`, and `resetCellPageMocks()` holding the body of the
  old top-level `beforeEach` (old 518–527). Relative imports adjusted for the new directory. Depends on T002.
- [ ] **T004 [US2]** (frontend) Rewrite `CellPage.test.tsx` to import its preamble from the harness
  (harness first) and call `beforeEach(resetCellPageMocks)`; all 66 tests stay in it, bodies untouched. Run it:
  66/66; `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` on both files. Commit
  `test(470): CellPage tests take their setup from a shared harness`. Depends on T003.
- [ ] **T005 [US2]** (frontend) **Counterfactual (plan §Verification).** In the harness, remove the
  `layoutHub` `vi.mock`; run `CellPage.test.tsx`; the push/hub tests must fail (quote the failures); restore
  (`git diff` empty). Not committed. Depends on T004.

## Phase 3 — One commit per move

### User story 1 (P1) — every test survives unchanged; user story 2 — a concern has one home

Each task: run the extraction for that row, then **run the new file alone** and the old file (counts: new file
= row total, old file = 66 minus moved so far), `eslint`/`tsc`/`prettier` on both, commit
`test(470): <NewFile> holds <concern>`. Order is fixed (one branch, one index) and each depends on the previous.

- [ ] **T006 [US1]** `CellPageWallFab.test.tsx` — Applying only the wall's own fab (#2069) — 5 tests.
- [ ] **T007 [US1]** `CellPageFabSkew.test.tsx` — A frame carrying no fab at all is a version skew (#2084),
  incl. #2320 — 12 tests.
- [ ] **T008 [US1]** `CellPageOmittedInputs.test.tsx` — spec 141 site 1 + site 2 — 9 tests.
- [ ] **T009 [US1]** `CellPageResolvedText.test.tsx` — mixed `[Box, Text]` (spec 301), pushed resolved text
  (#2012), spec 141 site 3 — 8 tests.
- [ ] **T010 [US1]** `CellPageLabelAge.test.tsx` — Holding a label back; label held for the age most
  recently reported (spec 204 SC-3/4/5) — 5 tests.
- [ ] **T011 [US1]** `CellPageLiveUpdates.test.tsx` — live-update resilience (spec 011), per-tile highlight
  (US3), wall does no work when nothing changed (spec 204 SC-1), pushed archive/publish frames (spec 229)
  — 14 tests.
- [ ] **T012 [US1]** `CellPageLabelSet.test.tsx` — The label set as a unit (spec 150) — 6 tests. After it,
  `CellPage.test.tsx` holds exactly the 7 top-level tests.

## Phase 4 — Gates and PR

- [ ] **T013** (frontend) **T-A after:** repeat T001's four records. Names+status `diff` empty (66 = 66, file
  column excluded); assertion lines `diff` empty; body-line multiset differs only by wrapper/hook/import lines
  (list the differing lines in the PR); each of the eight files passes **alone**; whole suite 329 → 329 with the
  new duration (flag > +25 %). Then `npm run -w kiosk-web lint|typecheck|test`, `npm run format:check`, and
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). Also `wc -l`: no new file over 600 lines
  (SC-003). Depends on T012.
- [ ] **T014** (frontend) `git diff --stat origin/develop...HEAD` shows only `apps/kiosk-web/src/test/`,
  `apps/kiosk-web/src/features/cell/*.test.tsx` and `specs/470-…` / `.specify/feature.json` (FR-006). Tick this
  file's tasks. Depends on T013.
- [ ] **T015** (orchestrator) PR to `develop` (`gh pr create --base develop`): body states Phase 4a
  characterisation (no red test), the T-A before/after, the counterfactual's failures, the suite duration
  before/after, and **Closes #3197**. Park; do not wait on CI. Depends on T014.
- [ ] **T016** (orchestrator) After merge: confirm #3197 closed (a closing keyword, then check the state);
  add a note on #3190 that `OverlaysPage`/`LayoutsPage` tests are the next-largest files and still wait on it.
  Depends on T015 merged.

## Dependencies and parallelism

Strictly sequential: T001 → T002 → T003 → T004 → T005 → T006…T012 → T013 → T014 → T015 → T016. The moves
share one branch and one `CellPage.test.tsx`, so none can run in parallel. T005 (counterfactual) must run
before the first move so a vacuous harness is found while the file is still whole.

## Explicitly not tasks

No `shard-N.filter` entry (Vitest). No production file, no `shared` change, no config change. No
`OverlaysPage`/`LayoutsPage` split (waits on #3190). No edit to any assertion or test name. No ADR.
