# Tasks 474 — The Overlays and Layouts FocusRefetch/RetryRecovery tests use the shared test helpers

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3210 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation** (no red-first test; the four
files are their own covering tests and must pass with every assertion unmodified).

One PR, one commit per file; every commit leaves the suite green. Scripts, from the repo root:
`npm run -w management-web test | lint | typecheck`, `npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470, 472, 473):** `git add` only the files of the step (never `git checkout --`
to "clean up": it restores from the index); `prettier --write` the migrated file before committing (the hook rejects
unformatted files); **run Vitest from `apps/management-web`** (elsewhere there is no jsdom and everything fails);
a counterfactual must be seen failing **for the right reason** (read the failure, not just the count).

## Phase 1 — Record the "before" (T-A)

- [ ] **T001 [US1]** (frontend) On the unmigrated tree record into the scratchpad, for the four files: (a) `vitest run
  <the four files> --reporter=json` → sorted `file :: full name :: status` (all passing; note the count);
  (b) their sorted assertion lines (`expect(`, `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow`); (c) per-file line
  counts (179 / 105 / 203 / 128); (d) a repo-wide `grep -rn "function jsonResponse\|function problemResponse" apps
  --include=*.test.*` (expect the four files plus `CameraTile.test.tsx`); (e) the whole management-web suite (684 tests)
  and its duration. Depends on nothing.

## Phase 2 — One commit per file

### User story 1 (P1) — the four files use the shared helpers and store, and behave as before (FR-001–FR-003)

Each task: run `node migrate.cjs helpers <file>` then `node migrate.cjs store <file> <slice>` (scratchpad, from
spec 468; it aborts on any mismatch), `prettier --write` the file, **run the file alone** (and, for a `FocusRefetch`
file, also confirm it passes in the suite at T006), `eslint --max-warnings 0` and `tsc --noEmit`, read
`git diff` for the file (only the helper and store blocks and the imports may change), `git add` that file only,
commit `test(474): <File> uses the shared test helpers`. Fixed order, one branch, one index. **If a file fails alone,
read the failure: if it is the plan §R3 env-stub timing, move that file's `createApiStore` import to a dynamic
import beside its slice imports (as `kiosk-web`'s `CameraTile` does) and say so in the PR; anything else, stop and
report.**

- [ ] **T002 [US1]** `overlays/OverlaysPageFocusRefetch.test.tsx` — `jsonResponse` + `problemResponse` + store
  (`overlaysApi`).
- [ ] **T003 [US1]** `overlays/OverlaysPageRetryRecovery.test.tsx` — `jsonResponse` + store (`overlaysApi`).
- [ ] **T004 [US1]** `layouts/LayoutsPageFocusRefetch.test.tsx` — `jsonResponse` + `problemResponse` + store
  (`layoutsApi`).
- [ ] **T005 [US1]** `layouts/LayoutsPageRetryRecovery.test.tsx` — `jsonResponse` + store (`layoutsApi`).

## Phase 3 — Gates and PR

- [ ] **T006** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty; (b) assertion lines
  `diff` empty; (c) the two `FocusRefetch` files pass **alone** and the whole management-web suite passes
  (684 → 684) with duration before/after; (d) the SC-003 grep finds only `CameraTile.test.tsx`; (e) `git diff --stat
  origin/develop...HEAD` shows only the four test files and `specs/474-…` / `.specify/feature.json` (FR-004).
  Depends on T005.
- [ ] **T007** (test-adversary) **Counterfactual (plan §Verification), not committed:** in
  `apps/shared/src/test/responses.ts` make `problemResponse` drop `status` from its body; run
  `OverlaysPageFocusRefetch` (and `LayoutsPageFocusRefetch`); the tests that use `problemResponse` must fail
  **because of that** (quote the failure message); restore (`git diff` empty). Depends on T006.
- [ ] **T008** (frontend) `npm run -w management-web lint|typecheck|test`, `npm run format:check`, and
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). Tick this file's tasks. Depends on T007.
- [ ] **T009** (orchestrator) PR to `develop` (`gh pr create --base develop`): body states Phase 4a characterisation
  (no red test), the T-A before/after, the counterfactual, the suite duration, any R3 finding, and
  **Closes #3210**. Park; do not wait on CI. Depends on T008.
- [ ] **T010** (orchestrator) After merge: confirm #3210 closed (closing keyword, then check the state); comment that
  #3196's remaining item is done and `CameraTile` is the only intentional local `jsonResponse`. Depends on T009
  merged.

## Dependencies and parallelism

Strictly sequential: T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010. The four files share one
branch and one index, so none can run in parallel.

## Explicitly not tasks

No `CameraTile.test.tsx`. No production file, no `shared` change (T007 breaks `responses.ts` temporarily and
restores it, uncommitted). No `resetHarness()`. No edit to any assertion or test name. No `shard-N.filter` entry
(Vitest). No ADR.
