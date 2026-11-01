# Tasks 472 — `OverlaysPage.test.tsx` is split along the concerns its `describe` blocks already name

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3206 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation** (no red-first test; the
covering tests are the file being split and must pass with every assertion unmodified).

One PR, one commit per step; every commit leaves the suite green. Scripts, from the repo root:
`npm run -w management-web test | lint | typecheck`, `npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (spec 470 lessons):** `git add` only the files of the step (never `git checkout --` to "clean up":
it restores from the index); `prettier --write` the generated files before committing (the hook rejects them).
**Run Vitest from `apps/management-web`** (from another directory there is no jsdom and everything fails).

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the unsplit tree record into the scratchpad: (a) `vitest run … OverlaysPage.test.tsx
  --reporter=json` → sorted `full name :: status` (56, all passing); (b) the sorted assertion lines (`expect(`,
  `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow`); (c) the sorted multiset of every line from the first `describe(`
  to the end of the file; (d) the 43 lines matching `^\s*(publish|archive|branch|revert)State =` with their line
  numbers (plan §R2); (e) the whole management-web suite (684 tests) and its duration. Depends on nothing.

## Phase 2 — The shared setup

### User story 2 (P2) — the shared setup exists once (FR-006)

- [x] **T002 [US2]** (frontend) Write the extraction script in the scratchpad (`split472.cjs`): cuts by top-level
  `describe(` range (with its leading comment block), **fails loudly** if a range does not start with the expected
  title or end on `});`, applies only the plan §R2 rewrite and asserts it hit exactly 43 lines, computes each new
  file's imports from the identifiers its code uses (comments and strings stripped). Not committed. Depends on T001.
- [x] **T003 [US2]** (frontend) Create `apps/management-web/src/test/overlaysPageHarness.tsx` from old lines 1–133
  **verbatim** (the five `vi.fn` mocks, `refusal`, the `vi.mock` of `overlays.api`, `chain`, `response`,
  `renderPage`), changing only: `export` on what tests use (including `OverlaysPage`), the four `…State` lets
  replaced by the exported `mutationState` object and the factory reads (`mutationState.publish` …), and the
  dynamic import path (`'../features/overlays/OverlaysPage.js'`). Depends on T002.
- [x] **T004 [US2]** (frontend) Rewrite `OverlaysPage.test.tsx` to import from the harness **first** and apply the
  43-line rewrite; all 56 tests stay in it. Run it (56/56); `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check` on both files. Commit `test(472): OverlaysPage tests take their setup from a shared harness`.
  Depends on T003.
- [x] **T005 [US2]** (test-adversary) **Counterfactuals (SC-006), not committed:** (a) remove the
  `useListOverlaysQuery` line from the harness's mock factory; run the file; most tests must fail (quote a few);
  restore. (b) change `mutationState.archive` to `mutationState.publish` in the archive mutation's factory line;
  the archive tests that set archive state must fail (quote); restore (`git diff` empty after each). Depends on T004.

## Phase 3 — One commit per move

### User story 1 (P1) — every test survives unchanged; user story 2 — a concern has one home

Each task: run the extraction for that row, `prettier --write`, run the **new file alone** and the shrunken
`OverlaysPage.test.tsx`, check counts (new = row total; old = 56 minus moved so far), `eslint`/`tsc`/`prettier` on
both, `git add` those two files only, commit `test(472): <NewFile> holds <concern>`. Fixed order, one branch, one
index. **If a file fails alone, stop: that is plan §R4's hidden order dependence; report it, do not paper over it.**

- [x] **T006 [US1]** `OverlaysPageArchive.test.tsx` — archive confirmation; archive and discard on one chain — 10 tests
  (old file keeps 46).
- [x] **T007 [US1]** `OverlaysPageChainActions.test.tsx` — recovering an archived overlay; every chain shape offers
  something — 14 tests (keeps 32).
- [x] **T008 [US1]** `OverlaysPageRefusals.test.tsx` — a refused mutation is surfaced; revocation fallback, three
  403s — 8 tests (keeps 24).
- [x] **T009 [US1]** `OverlaysPageUnavailable.test.tsx` — keep focus while unavailable (spec 273 O1/O2, O3/O4/O5,
  J1) — 8 tests (keeps 16).
- [x] **T010 [US1]** `OverlaysPageNavigation.test.tsx` — editing a draft in place (spec 152); navigating instead of
  opening a dialog (spec 305) — 7 tests. After it, `OverlaysPage.test.tsx` holds exactly 9 tests.

## Phase 4 — Gates and PR

- [x] **T011** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty (56 = 56, file column
  excluded); (b) assertion lines `diff` empty; (c) body lines: **exactly 43** differ before reversing the rewrite,
  all matching the R2 pattern, and after reversing it the multiset difference is only import/header lines —
  list anything else and treat it as a failure; (d) each of the six files passes **alone**; (e) whole
  management-web suite 684 → 684 with duration before/after; (f) `wc -l`: no new file over 400 (SC-003).
  Depends on T010.
- [x] **T012** (frontend) `npm run -w management-web lint|typecheck|test`, `npm run format:check`, and
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). `git diff --stat origin/develop...HEAD` shows only
  `apps/management-web/src/test/overlaysPageHarness.tsx`, `apps/management-web/src/features/overlays/OverlaysPage*.test.tsx`
  and `specs/472-…` / `.specify/feature.json` (FR-007). Tick this file's tasks. Depends on T011.
- [ ] **T013** (orchestrator) PR to `develop` (`gh pr create --base develop`): body states Phase 4a characterisation
  (no red test), the T-A before/after including the 43-line list, both counterfactuals, the suite duration, any R4
  finding, and **Closes #3206**. Park; do not wait on CI. Depends on T012.
- [ ] **T014** (orchestrator) After merge: confirm #3206 closed (closing keyword, then check the state); comment that
  `LayoutsPage.test.tsx` (1228 lines) is the next-largest test file and now the obvious next split, and that the
  `OverlaysPageFocusRefetch`/`RetryRecovery` helper migration (#3196 note) is unblocked. Depends on T013 merged.

## Dependencies and parallelism

Strictly sequential: T001 → T002 → T003 → T004 → T005 → T006 … T010 → T011 → T012 → T013 → T014. The moves share
one branch and one `OverlaysPage.test.tsx`, so none can run in parallel. T005 must run before the first move so a
vacuous harness is found while the file is still whole.

## Explicitly not tasks

No `shard-N.filter` entry (Vitest). No production file, no `shared` change, no config change. No
`LayoutsPage.test.tsx`. No edit to any assertion or test name. No change to `OverlaysPageFocusRefetch` /
`OverlaysPageRetryRecovery`. No ADR.
