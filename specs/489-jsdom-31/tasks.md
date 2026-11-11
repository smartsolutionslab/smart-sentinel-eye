# Tasks: Bump `jsdom` 30.1.3 → 31.0.0

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #3240
**Phase 4a colour: characterisation.** The five suites are observed green, twice, on the lockfile-true unmodified tree first, and must pass
**unmodified** after, with identical counts. No source, test or snapshot file changes (FR-001). A failure that reproduces on jsdom 31 is
reported, not edited, and no red PR is opened (plan D9). `@microsoft/signalr`, `@hookform/resolvers` (#3239) and any canvas package are
out of scope. Never `git stash` or `git checkout -- .`. One vitest at a time. No `Co-Authored-By`.

## Phase 1 — Baseline on the lockfile-true tree (US1, US2)

- [x] **T001** (frontend) `pnpm install --frozen-lockfile` on the unmodified branch. Read the installed `jsdom` version from
      `node_modules` in **each of the five apps** (all must read `30.1.3`), list the `jsdom@*` directories under `node_modules/.pnpm`
      (exactly one), and record `node --version` against the engines range. Record it.
- [x] **T002** (frontend) Write `run489.cjs` in the scratchpad (plan D2): one app per call, `vitest run --reporter=json --outputFile`,
      keeps combined stdout/stderr, prints files, passed, failed, skipped/todo and the count of `stderr |` / `stdout |` blocks; writes
      after every app. Depends on T001.
- [x] **T003** (frontend) Baseline run 1: all five apps in the fixed order (`shared`, `kiosk-web`, `management-event-ingestion`,
      `management-cameras`, `management-web`), foreground, one at a time. Save the JSON and the logs. Depends on T002.
- [x] **T004** (frontend) Baseline run 2: the same, to learn the normal variance (#2641). Any count that differs between runs is
      recorded as a baseline flake, with its test name. Depends on T003.
- [x] **T005** (frontend) Baseline gates: `eslint src --max-warnings 0` and `tsc --noEmit` in all five apps, exit codes read.
      Depends on T001.

## Phase 2 — The bump (US1)

- [x] **T006** (frontend) Set `jsdom` to `31.0.0` in the five `apps/*/package.json` files (exact pin, same value; FR-002). Run
      `pnpm install --lockfile-only`, then `pnpm install --frozen-lockfile`; read the installed version back from `node_modules` in each
      app (all `31.0.0`) and confirm a single `jsdom@*` directory. Depends on T003, T004, T005.
- [x] **T007** (frontend) Check the diff (FR-001, FR-008, SC-002): `git diff --stat` is exactly the five manifests and `pnpm-lock.yaml`.
      List the added and removed lockfile package keys; for each, `pnpm why <pkg>` must show it is reached through `jsdom`.
      `@napi-rs/canvas` must not be installed. If anything else moved, **stop and report** — do not hand-edit the lockfile.
      Depends on T006.
- [x] **T008** (frontend) After-bump run: the same runner, same order. Counts (files, passed, skipped) equal the baseline's, no test file
      in the diff (FR-004, SC-001). A failure: save it verbatim, re-run that file alone once, classify it (plan D5/D9); if it reproduces
      on 31, **stop** and follow D9 rather than continuing. Depends on T007.
- [x] **T009** (frontend) Diff the noise (FR-005, SC-003): normalise the `stderr |` / `stdout |` blocks of the baseline and the
      after-run (strip paths, timings, ids), and list every message present after and absent before. Read each one. Depends on T008.
- [x] **T010** (frontend) After-bump gates: `eslint src --max-warnings 0` and `tsc --noEmit` in all five apps, `prettier --check` on the
      five manifests, exit codes read (FR-007). Depends on T008.
- [x] **T011** (frontend) Commit `build(deps): bump jsdom to 31.0.0` (the six files only; stage by path, never `-a`).
      Depends on T009, T010.

## Phase 3 — Are the visibility and style matchers live on 31? (US2, FR-006)

- [x] **T012** (frontend) Choose the sample: three of the 13 `toBeVisible` / `toHaveStyle` files, one from each of `management-web`,
      `kiosk-web` and `shared`, mixing both matchers. Read each file and write the exact-once substitution and the expected failing test
      into `verification.md` **before** running anything. Depends on T011.
- [x] **T013** (frontend) Write `probe489.cjs` in the scratchpad: the three named substitutions in the component under test, a runner
      that restores only the file it broke from git and refuses to start on a dirty tree, and the file's own tests as the check.
      Depends on T012.
- [x] **T014** (frontend) Run the three probes, one vitest at a time; each should fail at least one test. Record the failing test names,
      or "the matcher did not notice" (a finding, filed, not fixed here). Confirm the tree is clean afterwards. Depends on T013.

## Phase 4 — Verify (US1, US2)

- [x] **T015** (frontend) Write `verification.md`: installed versions before/after and the single-copy check, Node version, both baseline
      runs and the after-run (files, passed, failed, skipped, noise-block counts per app), any baseline flake, the T007 diff extract, the
      T009 message list, the T014 table, gate exit codes, one foreground attempt at `Architecture.Tests`, "§IV legs touched: none", "jsdom
      ships in no bundle" and "`@microsoft/signalr` deliberately untouched". Tick T001–T015. Depends on T014.

## Phase 5 — QA and PR

- [x] **T016** `/code-review medium` with the branch as the target; dispositions in `verification.md`.
      **T017** open the PR to `develop` (`--base develop`) with `Closes #3240`, the before/after counts and "§IV legs touched: none";
      park it; merge on green with the run's head SHA equal to the PR tip and every bucket passing via
      `gh pr merge --rebase --admin --delete-branch`; post the close-out comment; confirm #3240 is closed. Depends on T015.
      (T016 and T017 are not ticked until the PR is merged.) If PR #3242 has merged first, rebase and re-resolve with
      `pnpm install --lockfile-only` before opening.

## Dependencies

T001 → T002 → T003 → T004; T005 after T001. T006 after T003, T004, T005 → T007 → T008 → T009/T010 → T011 → T012 → T013 → T014 → T015 →
T016 → T017. No parallel tasks (one branch, one index, one vitest at a time).
