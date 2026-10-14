# Spec 357: Guards that tie the pre-commit hook's scope and flags to CI

**Feature Branch**: `test/2922-hook-scope-guards` (cut from `origin/develop` at `37ee854e`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[JUDGEMENT]** (§6).

**Input**: Issue [#2922](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2922), a
follow-up from #2869 / spec 355. No issue comments beyond the lane pickup.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**: two new guard tests over existing hook
behaviour, no production change. Phase 4a is **CHARACTERISATION, observed green** — there is no
current drift (§1.3), so a red guard on arrival would mean the guard is wrong. Each guard is proven
by counterfactual instead (§4), because a guard that has never been seen failing proves nothing
(memory: "prove a guard by counterfactual").

**Spec number.** 357. Checked 2026-10-14 after `git fetch`: `origin/develop` highest 355; every
remote head highest 356 (`origin/fix/2913-rotate-502`). Re-check before the PR is opened.

**ADRs.** ADR-0034 (code style; the hook), ADR-0033 (CI format gates are the authority the hook
mirrors), ADR-0037 / ADR-0144 (workflow, lane), ADR-0139 (characterisation colour), ADR-0036
(smallest change). **No new ADR is needed**: this tests an existing decision.

**Latency budget.** N/A — developer tooling; nothing on the event-to-overlay path (constitution §IV).

---

## 1. Premise verified (Phase 1 investigation, `37ee854e`)

1. **Scope is hand-written.** `scripts/pre-commit.mjs:29-31` — `SCOPE_DIRECTORIES`,
   `SCOPE_EXTENSIONS`, `SCOPE_FILES` drive `inFormatScope` → `classify(...).prettier`.
   `FORMAT_CHECK_SCOPE` (`:24`) feeds only `findScopeDrift` (`:46`), which no runtime path calls
   and which asks only "does each hook glob appear in `format:check`", never the reverse. Adding
   `mjs` to `format:check`'s extension list, or a third glob, leaves the hook silently behind.
   **Confirmed.**
2. **Tautological test.** `pre-commit.test.mjs:808` compares `FORMAT_CHECK_SCOPE` with
   `FORMAT_CHECK_GLOBS`, a hand copy of the same literal. **Confirmed.**
3. **No current drift.** Expanding `format:check`'s two globs with `path.posix.matchesGlob` over
   `git ls-files` gives **424** files; `classify(git ls-files).prettier` gives **424**; both
   differences empty (ts 205 + tsx 189 + js 9 + json 13 + css 7 + `playwright.config.ts`).
   (`fs.globSync` over the working tree gives 537 — it sees untracked files and
   `node_modules`, so it is the wrong population; the guard uses `git ls-files`.)
4. **dotnet flags.** `dotnetArgs` (`pre-commit.mjs:95`) is pinned only by a literal in
   `pre-commit.test.mjs:663`; nothing reads `.github/workflows/ci.yml:176-177`
   (`dotnet format SmartSentinelEye.slnx --verify-no-changes --no-restore --exclude-diagnostics
   IDE1006`). Today they agree. **Confirmed.**
5. **Runtime.** `pnpm test` → `pnpm test:guards` (`node --test "scripts/**/*.test.mjs" …`) runs in
   CI's frontend job on `node-version: '22'` (`ci.yml:291`); `package.json` `engines` requires
   `^22.22.2 || ^24.15.0 || >=26`. `path.matchesGlob` exists from 22.5 and `fs.globSync` from 22.0;
   locally Node 24.21 returns `function` for both and matches the brace glob correctly.

## 2. User story

### US1 (P1) — Widening a CI format gate without widening the hook fails the guard suite

A maintainer who changes `format:check`'s globs, or the `Format check` step's `dotnet format`
flags, without changing the hook gets a failing `pnpm test:guards` naming the difference, instead
of a hook that silently checks less than CI.

## 3. Acceptance scenarios (Gherkin)

```gherkin
Scenario: the hook's prettier scope equals format:check's expansion (happy)
  Given package.json's format:check and the repository's tracked files
  When format:check's globs are expanded over git ls-files
  Then the set equals classify(git ls-files).prettier

Scenario: format:check gains an extension the hook lacks (conflict / counterfactual)
  Given a format:check command with ",mjs" added to the extension list
  When the expansion is compared with classify(...).prettier
  Then the difference names e2e/support/render-leg.test.mjs (the one tracked in-scope .mjs)

Scenario: format:check gains a third glob (conflict / counterfactual)
  Given a format:check command with an extra glob matching tracked files (e.g. "scripts/**/*.mjs")
  Then the difference names those files

Scenario: the hook covers a file format:check does not (reverse direction / counterfactual)
  Given a format:check command with "css" removed from the extension list
  Then the difference names the 7 tracked apps/** .css files as hook-only

Scenario: dotnet flags equal ci.yml's Format check step (happy)
  Given ci.yml's "Format check" step whose run line starts "dotnet format"
  Then its tokens after the project equal dotnetArgs(...) minus "format", the project, "--include" and the files

Scenario: ci.yml gains or loses a flag (conflict / counterfactual)
  Given that run line with "--severity warn" appended, or "--no-restore" removed
  Then the comparison fails

Scenario: the step cannot be found (bad request)
  Given ci.yml with no "Format check" step running dotnet format
  Then the guard fails saying the step was not found, rather than passing on an empty list
```

Auth: N/A (local tooling).

## 4. Independent end-to-end test procedure (Phase 5)

1. `pnpm test:guards` green on the branch.
2. **Counterfactual, live files** (not committed): add `,mjs` to `format:check` in `package.json`
   → guard (a) red, naming `e2e/support/render-leg.test.mjs`; revert → green. Append `--severity warn` to
   `ci.yml:177` → guard (b) red; revert → green. Quote both red outputs in the PR.
3. The permanent in-suite counterfactual tests (§3) stay green.

## 5. Functional requirements

- **FR-1** Guard (a) parses `format:check` as `prettier --check <operands>`, takes non-flag operands
  (quotes stripped) as globs, expands them with `path.posix.matchesGlob` over `git ls-files` at the
  repository root, and asserts set equality with `classify(files).prettier`, reporting both
  difference directions.
- **FR-2** Guard (b) locates the step named `Format check` in `ci.yml` whose `run:` starts
  `dotnet format`, tokenises it, and asserts the tokens after the project equal, in order,
  `dotnetArgs({ project, files: [] })` with `format`, `project` and `--include` removed.
- **FR-3** Both guards' comparison logic is a pure helper in the test file, exercised by permanent
  counterfactual tests on mutated inputs (§3), so the guard is seen failing on every run.
- **FR-4** No change to `scripts/pre-commit.mjs`, `package.json`, `ci.yml`.

## 6. Judgement calls

- **[JUDGEMENT] Tracked files, not `fs.globSync`.** The issue offers either API; `globSync` over
  the working tree sees untracked and ignored files (537 vs 424), which would make the guard depend
  on the machine. `git ls-files` + `path.posix.matchesGlob` is deterministic.
- **[JUDGEMENT] `.prettierignore` is not applied** on either side: both `classify` and the glob
  expansion are pre-ignore populations; the ignore file filters both identically at run time.
- **[JUDGEMENT] Dot-file glob semantics.** prettier's glob engine and `matchesGlob` may differ on
  dot-segments; no tracked in-scope path has one today, so the difference is unobservable and not
  tested.
- **[JUDGEMENT] Existing tautological test and `findScopeDrift` stay.** Removing them is a cleanup
  the issue does not ask for, and the lane may not delete tests (ADR-0144). Reported as a
  follow-up candidate.
- **[JUDGEMENT] Ordered flag comparison.** A pure reorder in `ci.yml` would fail guard (b); that is
  a cheap false positive versus missing an option/value pairing change.

## 7. Out of scope

Deriving the hook's scope from `format:check` at runtime; removing `FORMAT_CHECK_SCOPE` /
`findScopeDrift`; any CI or hook behaviour change.
