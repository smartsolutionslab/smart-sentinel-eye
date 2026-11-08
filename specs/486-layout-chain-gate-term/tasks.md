# Tasks: Is the layout Save gate's `currentChain === undefined` term pinned?

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #3232
**Phase 4a colour: characterisation.** No production file changes (FR-001). Existing tests are never edited; any new test goes in a
new file, green on today's code first, then proved able to fail (message read). One vitest at a time, foreground, batches of about
five. A break left applied by an interrupted run is checked with `git diff` and restored from git: never `git stash` or
`git checkout -- .`. The D1 throwaway file is deleted before any commit (`git status --short -- apps` checked).

## Phase 1 — Probe (US1)

- [x] **T001** (frontend) Baseline on the unmodified tree: the whole `management-web` `src/features/layouts` folder, count recorded;
      `git status` clean on `useSaveGate.ts` and `useLayoutEditorForm.ts`.
- [x] **T002** (frontend) Write `probe486.cjs` and `run486.cjs` in the scratchpad: named exactly-once substitutions T1–T6 (plan D3). The
      runner restores only `useSaveGate.ts` and `useLayoutEditorForm.ts`, from git, and refuses to start if either is dirty. Dry-run every
      pattern (each must match once) before running any test. Depends on T001.
- [x] **T003** (frontend) D1 reachability probe: a throwaway test against the real RTK store (`createApiStore`), `fetch` stubbed, recording
      `(currentData !== undefined, isFetching, isError)` at every render through S1–S8. Record every tuple; decide whether the target
      state (no data, not fetching, not failed, not skipped) occurs. Delete the throwaway file afterwards. Depends on T001.
- [x] **T004** (frontend) Run the break matrix T1–T6, twice each (the layouts folder), failure messages read; results file fresh and tree
      clean after each batch. Depends on T002.
- [x] **T005** (frontend) Decide the disposition: target state unreachable → term equivalent, no test (FR-005); reachable → say by which
      path. If a guard is dead or wrong, **stop and report** (FR-001). Depends on T003, T004.

## Phase 2 — Pin, if reachable (US1)

- [x] **T006** (frontend) NOT NEEDED (T005: unreachable). Only if T005 finds a reachable state through an operator path: `LayoutEditorDialogChainAbsent.test.tsx` (new
      file) through the real dialog and store; green on today's code; proved by T3 (message read). If reachable only through a path no
      operator takes (e.g. `resetApiState()`), **raise with the user first**. Otherwise recorded "not needed". Depends on T005.

## Phase 3 — Verify (US2)

- [x] **T007** (frontend) Write `verification.md`: the S1–S8 tuples, the T1–T6 matrix with messages, the disposition, quote the two
      overstating comments (Retention, Recovery) with the control breaks that answer them, suite counts before/after, `eslint src
    --max-warnings 0`, `tsc --noEmit`, `prettier --check`, one foreground attempt at `Architecture.Tests`. State "§IV legs touched:
      none". Tick T001–T007. Depends on T006.

## Phase 4 — QA and PR

- [ ] **T008** `/code-review medium`; dispositions in `verification.md`. **T009** open the PR to `develop` (`--base develop`) with
      `Closes #3232`, the matrix, the messages and "§IV legs touched: none"; park it; merge on green with run head SHA equal to the PR tip
      and every bucket passing, via `gh pr merge --rebase --admin --delete-branch`; post the close-out comment; confirm #3232 is closed.
      Depends on T007. (T008 and T009 are not ticked until the PR is merged.)

## Dependencies

T001 → (T002, T003) → T004 → T005 → T006 → T007 → T008 → T009. No parallel tasks (one branch, one index). If T005 finds the state
unreachable, T006 is recorded as "not needed" and the PR is documentation only.
