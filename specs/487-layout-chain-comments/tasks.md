# Tasks: Correct two layout test comments that overstate the `currentChain` term

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #3237
**Phase 4a colour: characterisation (comment-only).** No assertion, test name or production line changes (FR-001). The proof is the
comment-stripped hash, shown able to fail. Never `git stash` or `git checkout -- .`.

## Phase 1 — Tool and baseline (US1)

- [x] **T001** (frontend) Baseline on `origin/develop`: the layouts suite (213 tests), `git status` clean on the two files.
- [x] **T002** (frontend) Write `strip487.cjs` in the scratchpad (D1: `ts.transpileModule`, `removeComments`, sha256; reads a file path or
      `git show` output). Run it on both files from `origin/develop`; record the two hashes. Run P, N1 and N2 (D2) on scratchpad copies:
      P equal, N1 and N2 different. Depends on T001.

## Phase 2 — Rewrite (US1)

- [x] **T003** (frontend) Rewrite the three passages per plan D3 (Retention in two places, Recovery in one), no line numbers in the new
      text, nothing else touched. `prettier --write` the two files. Depends on T002.
- [x] **T004** (frontend) Prove it: stripped hashes of both edited files equal the T002 hashes; `git diff` on the two files shows comment
      lines only; layouts suite 213 pass, unchanged; `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check src` exit 0 (read
      the exit codes before committing). Depends on T003.

## Phase 3 — Verify (US1)

- [ ] **T005** (frontend) Write `verification.md`: hashes before/after, P/N1/N2 results, the diff summary, suite counts, lint/type/format
      exit codes, one foreground attempt at `Architecture.Tests`; the correction to spec 486's note (T4 fails a different file than the
      comment is about; "alone" is the false word); "§IV legs touched: none". Tick T001–T005. Depends on T004.

## Phase 4 — QA and PR

- [ ] **T006** `/code-review medium`; dispositions in `verification.md` (read the new claims against spec 486's T1–T6 table).
      **T007** open the PR to `develop` (`--base develop`) with `Closes #3237`, the hashes and "§IV legs touched: none"; park it; merge on
      green with the run's head SHA equal to the PR tip and every bucket passing via `gh pr merge --rebase --admin --delete-branch`; post
      the close-out comment; confirm #3237 is closed. Depends on T005. (T006 and T007 are not ticked until the PR is merged.)

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → T007. No parallel tasks (one branch, one index).
