# Tasks: Make the kiosk grid-template assertion independent of jsdom's serialiser

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #3243
**Phase 4a colour: a test-only change whose subject did not move.** One test file changes (FR-001). The edited assertions must pass on
jsdom 30.1.3 and 31.0.0 and fail on four counterfactuals. No production file is committed. `@microsoft/signalr` and the jsdom bump itself
(#3240) are out of scope. Never `git stash` or `git checkout -- .`. One vitest at a time. No `Co-Authored-By`.

## Phase 1 — Baseline (US1)

- [x] **T001** (frontend) On the branch with a lockfile-true install (`pnpm install --frozen-lockfile`), read the installed `jsdom` from
      `apps/kiosk-web/node_modules` (must be `30.1.3`). Run `apps/kiosk-web` `vitest run`: record files and tests (expected 39 files, 392
      tests, all passing) and run `LayoutGridPage.test.tsx` alone (12 tests). `git status` clean on the file. Record.

## Phase 2 — The edit (US1)

- [x] **T002** (frontend) Edit `apps/kiosk-web/src/features/cell/LayoutGridPage.test.tsx` per plan D1/D2: a local helper that rewrites only
      `minmax(0px,` to `minmax(0,`, applied to both read-backs, comparing with the same literals as before; a why-comment of three lines
      at most. Nothing else in the file changes. Depends on T001.
- [x] **T003** (frontend) On 30.1.3: run the file (12 pass), the full `kiosk-web` suite (39 files, 392 tests, unchanged),
      `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check` on the file; read the exit codes. Depends on T002.
- [x] **T004** (frontend) Commit `test(kiosk-web): read the layout grid template back independent of the CSS engine's zero-length
      spelling` (the one test file; stage by path). The probes need this clean tree. Depends on T003.

## Phase 3 — Does it still pin the layout? (US1, FR-004)

- [ ] **T005** (frontend) Write `probe490.cjs` and `run490.cjs` in the scratchpad: four exactly-once substitutions (plan D4 a, b, c1, c2),
      each run against `LayoutGridPage.test.tsx`; the runner restores only the file it broke from git and refuses to start on a dirty
      tree. Depends on T004.
- [ ] **T006** (frontend) Run the four probes on 30.1.3, one vitest at a time; each must fail at least one test. A break that leaves the
      file green means the normaliser weakened the assertion: fix the helper and redo T003-T006, do not edit the probe. Confirm the tree is
      clean afterwards. Depends on T005.

## Phase 4 — jsdom 31 (US1, FR-003)

- [ ] **T007** (frontend) Re-make the bump temporarily (plan D3): `"jsdom": "31.0.0"` in the five `apps/*/package.json`,
      `pnpm install --lockfile-only`, `pnpm install --frozen-lockfile`; read `31.0.0` from `node_modules`. Run the file (12 pass, the
      rows assertion observed too) and the full `kiosk-web` suite (392 passed, 0 failed). Then restore only those six paths from git,
      `pnpm install --frozen-lockfile`, and read `30.1.3` back from disk; confirm `git status` is clean. Depends on T006.

## Phase 5 — Verify (US1)

- [ ] **T008** (frontend) Write `verification.md`: baseline and after counts on 30.1.3, the file on 31.0.0, the four probe results with
      the failing messages, gate exit codes, one foreground attempt at `Architecture.Tests`, `git diff origin/develop --stat` (one test file
      plus specs), "§IV legs touched: none", "no production file changed". Tick T001-T008. Depends on T007.

## Phase 6 — QA and PR

- [ ] **T009** `/code-review medium` with the branch as the target; dispositions in `verification.md`.
      **T010** open the PR to `develop` (`--base develop`) with `Closes #3243`, the two-version evidence and "§IV legs touched: none"; park
      it; merge on green with the run's head SHA equal to the PR tip and every bucket passing via
      `gh pr merge --rebase --admin --delete-branch`; post the close-out comment; confirm #3243 is closed; **then** note on #3240 that it
      is unblocked. Depends on T008. (T009 and T010 are not ticked until the PR is merged.)

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010. No parallel tasks (one branch, one index, one vitest at a time).
