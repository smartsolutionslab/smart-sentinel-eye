# Tasks: Double-break `OverlayLayerList`'s add/remove/move guards

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #3226
**Phase 4a colour: characterisation.** No production file changes (FR-005). Existing tests are never edited; any new test goes in a
new file, green on today's code first, then proved able to fail (message read). One vitest at a time, foreground, batches of about
five. A break left applied by an interrupted run is checked with `git diff` and restored from git: never `git stash` or
`git checkout -- .`.

## Phase 1 — Probe (US1)

- [x] **T001** (frontend) Baseline on the unmodified tree: the `management-web` overlays folder, the shared `OverlayLayerList*` tests and
      the stub-seam `OverlayDraftFormProps` test, counts recorded; `git status` clean on the three files to be broken.
- [x] **T002** (frontend) Write `probe485.cjs` and `run485.cjs` in the scratchpad: named exactly-once substitutions for A1, A2, R1, R2, M1
      (each as `L`, `H`, `LH`; A2 and R2 also `CLH`) and M2 (`L` only). The runner restores only `OverlayLayerList.tsx`,
      `useElementSelection.ts` and `OverlayDraftForm.tsx`, from git, and refuses to start if any is dirty. Dry-run every pattern
      (each must match once) before running any test.
- [x] **T003** (frontend) Run the matrix twice per break (real-tree suite only; then the full covering set), failure messages read. Check
      the results file is fresh and the tree clean after each batch. Depends on T001, T002.
- [x] **T004** (frontend) Decide each break's disposition: noticed end to end; noticed only by seam tests (gap); equivalent or
      unreachable (with the DOM evidence); jointly defended with the third layer. If a guard is dead or wrong, **stop and report**
      (FR-005). Depends on T003.

## Phase 2 — Pin the gaps (US1)

- [x] **T005** (frontend) For each gap, a test in `OverlayDraftFormGuards.test.tsx` (new file): real form, real list, clicks on the
      `aria-disabled` buttons, asserting the layer set and the live region unchanged. Run it green on today's code first. Depends on T004.
- [x] **T006** (frontend) Prove each new test: re-apply its `LH` break, read the failure message, restore from git. Then re-run T001's
      suites: counts equal baseline plus the new tests, `git diff` on existing tests empty. Depends on T005.

## Phase 3 — Verify (US2)

- [x] **T007** (frontend) Write `verification.md`: the matrix (every guard × L / H / LH, both runs) with the failure messages, dispositions
      and evidence, the tests added, suite counts before/after for `management-web` and `shared`, `eslint src --max-warnings 0`,
      `tsc --noEmit` and `prettier --check` for both apps, one foreground attempt at `Architecture.Tests` (if reaped for memory, say so
      and name the CI `backend` job). State "§IV legs touched: none". Close spec 477's "Caveat on E5–E9". Tick T001–T007. Depends on T006.

## Phase 4 — QA and PR

- [ ] **T008** `/code-review medium`; dispositions in `verification.md`. **T009** open the PR to `develop` (`--base develop`) with
      `Closes #3226`, the matrix, the failure messages and "§IV legs touched: none"; park it (do not wait on CI); merge on green with the
      run's head SHA equal to the PR tip and every bucket passing, via `gh pr merge --rebase --admin --delete-branch`; post the
      close-out comment; confirm #3226 is closed. Depends on T007.

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009. No parallel tasks (one branch, one index). If T004 finds no gap, T005
and T006 are recorded as "not needed: every guard defended or equivalent" and the PR is documentation only.
