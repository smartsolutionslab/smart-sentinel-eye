# Tasks: Bump `@hookform/resolvers` 6.0.0 → 7.0.5

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #3239
**Phase 4a colour: characterisation.** The suites are observed green on the lockfile-true unmodified tree first, and must pass
**unmodified** after. No source, test or snapshot file changes (FR-001). A test that fails after the bump is reported, not edited.
`@microsoft/signalr` and `jsdom` are out of scope. Never `git stash` or `git checkout -- .`. One vitest at a time. No `Co-Authored-By`.

## Phase 1 — Baseline on the lockfile-true tree (US2)

- [x] **T001** (frontend) `pnpm install --frozen-lockfile` on the unmodified branch, then read the installed
      `@hookform/resolvers` version from `node_modules` in both apps: it must be `6.0.0` (the local install was 5.9.1). Record it.
- [x] **T002** (frontend) Baseline: run the full vitest suite of `apps/management-web`, then of `apps/management-cameras`, foreground,
      one at a time. Record files and tests passed per app, and save the runner's summary lines. Depends on T001.
- [x] **T003** (frontend) Baseline gates: `eslint src --max-warnings 0`, `tsc --noEmit` in both apps, exit codes read. Depends on T001.

## Phase 2 — The bump (US1)

- [x] **T004** (frontend) Edit the `@hookform/resolvers` pin to `7.0.5` in `apps/management-web/package.json` and
      `apps/management-cameras/package.json` (exact pin, same value in both; FR-002). Run `pnpm install --lockfile-only`, then
      `pnpm install --frozen-lockfile`; read the installed version back from `node_modules` (must be `7.0.5`). Depends on T002, T003.
- [x] **T005** (frontend) Check the diff (FR-001, FR-006, SC-002): `git diff --stat` is exactly the two manifests and `pnpm-lock.yaml`;
      every changed lockfile key contains `@hookform/resolvers`; no second `zod@` or `react-hook-form@` snapshot. If anything else moved,
      **stop and report** — do not hand-edit the lockfile. Depends on T004.
- [x] **T006** (frontend) After-bump run: the same two suites, same order, counts equal to T002 and no test file in the diff (FR-004,
      SC-001); `eslint --max-warnings 0` and `tsc --noEmit` exit 0 in both apps; `prettier --check` on the two manifests exit 0
      (FR-005). Depends on T005.
- [x] **T007** (frontend) Commit `build(deps): bump @hookform/resolvers to 7.0.5` (the three files only; stage by path, never `-a`).
      Depends on T006.

## Phase 3 — Do the suites reach the resolver? (US2, FR-007)

- [x] **T008** (frontend) Write `probe488.cjs` and `run488.cjs` in the scratchpad: seven named, exactly-once substitutions of
      `zodResolver(<schema>)` with `async (values) => ({ values, errors: {} })` (`useOverlayDraftFormState.ts` included), each paired
      with its test file (`OverlayDraftForm.test.tsx` for the overlay one). The runner restores only the file it breaks, from git, and
      refuses to start on a dirty tree. Depends on T007.
- [x] **T009** (frontend) Run the seven probes, one vitest at a time. Each should fail at least one test. Record per form: failing test
      names, or "no test reaches the resolver". A form with no failure is **recorded and filed as its own issue**, not fixed here.
      Confirm the tree is clean afterwards. Depends on T008.

## Phase 4 — Verify (US1, US2)

- [x] **T010** (frontend) Write `verification.md`: installed versions before/after, baseline and after counts per app, the T005 diff
      extract, the T009 table (seven forms), gate exit codes, one foreground attempt at `Architecture.Tests` (a frontend change still
      meets it in CI), "§IV legs touched: none", and "`@microsoft/signalr` deliberately untouched". Tick T001–T010. Depends on T009.

## Phase 5 — QA and PR

- [ ] **T011** `/code-review medium` with the branch as the target; dispositions in `verification.md`.
      **T012** open the PR to `develop` (`--base develop`) with `Closes #3239`, the before/after counts and "§IV legs touched: none";
      park it; merge on green with the run's head SHA equal to the PR tip and every bucket passing via
      `gh pr merge --rebase --admin --delete-branch`; post the close-out comment; confirm #3239 is closed. Depends on T010.
      (T011 and T012 are not ticked until the PR is merged.)

## Dependencies

T001 → T002/T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011 → T012.
T002 and T003 are independent of each other but run serially (one vitest at a time, one branch, one index). No parallel tasks.
