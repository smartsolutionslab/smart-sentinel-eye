# Tasks 355: Pre-commit format hook

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2869 (feature-level;
no per-task issues).

**Phase 4a colour: RED** (behaviour-changing). T002's tests must be observed **failing** before
T003 exists, and pass **unmodified** after T003-T005. A test arriving green is a phase-4 failure.
For the e2e cases the red must be the assertion on the file name in the output, not merely a
non-zero exit (plan §5).

**Engineer:** infra-engineer. **Test-writer:** T002.

**Commits.** One per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). The red commit
(T002) leaves `pnpm test:guards` failing until T003 lands (accepted per CLAUDE.md "Builds is not
passes"); every other commit builds and passes on its own.

**Forbidden:** `.github/workflows/*`, `.editorconfig`, `.prettierrc.json`, `.prettierignore`,
`Directory.Build.props`, any ADR, the constitution, `tests/**` (no xUnit test, so no shard-filter
entry), `lint-staged` or any dependency beyond `husky`, auto-fix/restage behaviour.

Legend: `[P]` = disjoint files. Single user-story slice (US1-US3 together); all tasks are `[US1]`.

## Phase 1: Baseline (orchestrator)

- [ ] **T001** [US1] Re-check spec number 355 against `origin/develop` and every remote head.
  Record `pnpm test:guards` green on the untouched branch.

## Phase 2: Red tests (test-writer; blocks Phase 3)

- [ ] **T002** [US1] Write `scripts/pre-commit.test.mjs` with plan §5 cases 1-14. Run `pnpm
  test:guards`; return the verbatim failing output. Commit `test(scripts): pre-commit hook checks
  staged files against CI's format gates`.

## Phase 3: Implementation (infra-engineer; sequential — T003 blocks T004)

- [ ] **T003** [US1] `scripts/pre-commit.mjs` per plan §3 (classification, C# leg with injected
  runner, frontend leg on index content, output/exit). T002 passes unmodified. Commit
  `build(scripts): add the pre-commit format check`.
- [ ] **T004** [US1] `pnpm add -D -w husky@9.1.7` (exact), `"prepare": "husky"`, `.husky/pre-commit`
  = `node scripts/pre-commit.mjs`. Verify `git config core.hooksPath` = `.husky/_`, `HUSKY=0 pnpm
  install --frozen-lockfile` exits 0, `scripts/node-types-alignment.test.mjs` and
  `scripts/lint-scope.test.mjs` still green. Re-measure and justify
  `SOLUTION_FALLBACK_PROJECT_COUNT` (plan §3.3) — if changed, the change is in T003's file and
  committed here with the measurement in the message. Commit `build: install the pre-commit hook
  with husky`.
- [ ] **T005** [P] [US1] `CONTRIBUTING.md` per FR-011 (disjoint from T004's files; may run alongside
  T004). Commit `docs(contributing): document the pre-commit format hook`.

## Phase 4: Verify (Phase 5 of ADR-0037)

- [ ] **T006** [US1] Spec §4 steps 1-8 in `D:\Github\sse-2869` from Git Bash and PowerShell, plus
  one fresh `git worktree add` in the scratch directory (`pnpm install` there, mis-formatted commit
  rejected, then `git worktree remove`). Quote outputs and wall times in the verification note. The
  C# counterfactual (step 3) is the only evidence of the dotnet leg against a real `dotnet format`.

## Phase 5: Report (orchestrator)

- [ ] **T007** PR body: the ADR-0034 divergence table (spec §6) verbatim as "ADR text that would
  need to change — not edited by the lane"; note ADR-0030's unbuilt `commitlint` hook as a candidate
  follow-up issue; note the per-commit lane cost (spec §7).

## Dependencies

T001 → T002 → T003 → T004 → T006 → T007; T005 depends only on T002 (can run alongside T003/T004).
No foundational Shared/AppHost work; nothing to fan out beyond T005.
