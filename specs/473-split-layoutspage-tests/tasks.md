# Tasks 473 — `LayoutsPage.test.tsx` is split along the concerns its `describe` blocks already name

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3208 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation** (no red-first test; the
covering tests are the file being split and must pass with every assertion unmodified).

One PR, one commit per step; every commit leaves the suite green. Scripts, from the repo root:
`npm run -w management-web test | lint | typecheck`, `npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (specs 470 and 472 lessons):** `git add` only the files of the step (never `git checkout --` to
"clean up": it restores from the index); `prettier --write` **every** generated file each step, including earlier
moved ones (the script regenerates them all; the hook rejects unformatted files); **run Vitest from
`apps/management-web`** (elsewhere there is no jsdom and everything fails); compare bodies with a script that finds
each file's last import statement, not a regex for a multi-line import.

## Phase 1 — Record the "before" (T-A)

- [ ] **T001 [US1]** (frontend) On the unsplit tree record into the scratchpad: (a) `vitest run … LayoutsPage.test.tsx
  --reporter=json` → sorted `full name :: status` (45, all passing; note the 11 nested names begin
  `LayoutsPage — archive confirmation > `); (b) the sorted assertion lines (`expect(`, `.toHaveBeen…`, `.toBe…`,
  `.toEqual`, `.toThrow`); (c) the non-blank lines from the first `describe(` to the end of the file; (d) the 12 lines
  matching `^\s*publishState = ` with their line numbers; (e) the whole management-web suite (684 tests) and its
  duration. Depends on nothing.

## Phase 2 — The shared setup

### User story 2 (P2) — the shared setup exists once (FR-006)

- [ ] **T002 [US2]** (frontend) Write the extraction script in the scratchpad (`split473.cjs`, adapted from
  `split472.cjs`): cuts by top-level `describe(` range (with its leading comment block) and treats `ACTION_LABELS`
  with its doc comment as a segment attached to the Archive file; **fails loudly** if a range does not start with the
  expected title or end on `});`, if `ACTION_LABELS` is referenced outside the Archive file's groups, or if the
  `publishState =` rewrite does not hit exactly 12 lines (comments that mention the name are left verbatim);
  computes each file's imports from the identifiers its code uses. Not committed. Depends on T001.
- [ ] **T003 [US2]** (frontend) Create `apps/management-web/src/test/layoutsPageHarness.tsx` from old lines 1–120
  **verbatim** (the seven `vi.fn` mocks, `refusal`, both `vi.mock`s, `chain`, `response`, `renderPage`), changing
  only: `export` on what tests use (including `LayoutsPage`), the `let publishState` replaced by an exported
  `mutationState = { publish: { isLoading: false } }` and the factory read (`mutationState.publish`), and the
  dynamic import path (`'../features/layouts/LayoutsPage.js'`). Depends on T002.
- [ ] **T004 [US2]** (frontend) Rewrite `LayoutsPage.test.tsx` to import from the harness **first** and apply the
  12-line rewrite; all 45 tests stay in it. Run it (45/45); `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check` on both files. Commit `test(473): LayoutsPage tests take their setup from a shared harness`.
  Depends on T003.
- [ ] **T005 [US2]** (test-adversary) **Counterfactuals (SC-006), not committed:** (a) remove the
  `useListLayoutsQuery` line from the harness's mock factory; run the file; most tests must fail (quote a few);
  restore. (b) make `usePublishRevisionMutation` read a fixed `{ isLoading: false }` instead of
  `mutationState.publish`; the stale-conflict tests that set it must fail (quote); restore (`git diff` empty after
  each — back the untracked harness up and restore from the copy). Depends on T004.

## Phase 3 — One commit per move

### User story 1 (P1) — every test survives unchanged; user story 2 — a concern has one home

Each task: run the extraction for that row, `prettier --write` the harness and **all** `LayoutsPage*.test.tsx`, run
the **new file alone** and the shrunken `LayoutsPage.test.tsx`, check counts (new = row total; old = 45 minus moved so
far), `eslint`/`tsc` on both, `git add` those two files only, commit `test(473): <NewFile> holds <concern>`. Fixed
order, one branch, one index. **If a file fails alone, stop: that is plan §R5's hidden order dependence; report it,
do not paper over it.**

- [ ] **T006 [US1]** `LayoutsPageArchive.test.tsx` — archive confirmation with its two nested groups, then
  `ACTION_LABELS` — 16 tests (old file keeps 29).
- [ ] **T007 [US1]** `LayoutsPageOneChain.test.tsx` — archive and discard on one chain — 8 tests (keeps 21).
- [ ] **T008 [US1]** `LayoutsPageRefusals.test.tsx` — the stale-conflict fallback; revocation fallback, three 403s —
  6 tests (keeps 15).
- [ ] **T009 [US1]** `LayoutsPageUnavailable.test.tsx` — keeps focus while unavailable (spec 273 L1, L2, L3) —
  6 tests. After it, `LayoutsPage.test.tsx` holds exactly 9 tests.

## Phase 4 — Gates and PR

- [ ] **T010** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty (45 = 45, file column
  excluded) and the 11 nested names still begin `LayoutsPage — archive confirmation > `; (b) assertion lines `diff`
  empty; (c) body lines, via the script that finds each file's last import: **exactly 24** differ before reversing
  the rewrite (12 + 12), all matching the R2 pattern, and **0** differ after; (d) each of the five files passes
  **alone**; (e) whole management-web suite 684 → 684 with duration before/after; (f) `wc -l`: no new file over 420
  (SC-003). Depends on T009.
- [ ] **T011** (frontend) `npm run -w management-web lint|typecheck|test`, `npm run format:check`, and
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). `git diff --stat origin/develop...HEAD` shows only
  `apps/management-web/src/test/layoutsPageHarness.tsx`, `apps/management-web/src/features/layouts/LayoutsPage*.test.tsx`
  and `specs/473-…` / `.specify/feature.json` (FR-007). Tick this file's tasks. Depends on T010.
- [ ] **T012** (orchestrator) PR to `develop` (`gh pr create --base develop`): body states Phase 4a characterisation
  (no red test), the T-A before/after including how the 12 lines changed, both counterfactuals, the suite duration, any
  R5 finding, and **Closes #3208**. Park; do not wait on CI. Depends on T011.
- [ ] **T013** (orchestrator) After merge: confirm #3208 closed (closing keyword, then check the state); comment that
  the `Layouts|Overlays` `FocusRefetch`/`RetryRecovery` helper migration (#3196 note) is now unblocked for all four
  files, and that no test file in management-web is now over 400 lines except any the survey names. Depends on T012
  merged.

## Dependencies and parallelism

Strictly sequential: T001 → T002 → T003 → T004 → T005 → T006 … T009 → T010 → T011 → T012 → T013. The moves share
one branch and one `LayoutsPage.test.tsx`, so none can run in parallel. T005 must run before the first move so a
vacuous harness is found while the file is still whole.

## Explicitly not tasks

No `shard-N.filter` entry (Vitest). No production file, no `shared` change, no config change. No edit to any
assertion or test name. No change to `LayoutsPageFocusRefetch` / `LayoutsPageRetryRecovery`. No
`resetMutationState()` helper. No ADR.
