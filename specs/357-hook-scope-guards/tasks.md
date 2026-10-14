# Tasks 357: Hook scope and flag guards

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2922 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green.** No current drift (spec §1.3). The guards
must pass on arrival; the counterfactual tests must pass too (they assert the helper reports the
mutation). Red is required only in T003's live counterfactual, which is not committed.

**Engineer:** none beyond test-writer (no production change). **Test-writer:** T002-T003.

**Commits.** Conventional Commits, no `Co-Authored-By` (ADR-0086), never `--no-verify`. Each
commit builds and passes on its own.

**Forbidden:** `scripts/pre-commit.mjs`, `package.json`, `.github/workflows/*`, any ADR, the
constitution, deleting or editing existing tests, new dependencies.

Single slice; all tasks `[US1]`. No `[P]`: one file.

- [ ] **T001** [US1] Re-check spec number 357 against `origin/develop` and every remote head;
  record `pnpm test:guards` green on the untouched branch.
- [ ] **T002** [US1] Add plan §2 helpers and plan §3 tests 1-7 to `scripts/pre-commit.test.mjs`.
  Run `pnpm test:guards`; return verbatim output (all green). Commit `test(hooks): …`.
- [ ] **T003** [US1] Live counterfactual (uncommitted): `,mjs` in `package.json` `format:check` →
  test 1 red naming `e2e/support/render-leg.test.mjs`; `--severity warn` on `ci.yml:177` → test 2
  red. Revert both (`git diff --exit-code` clean), re-run green. Quote both red outputs verbatim
  for the PR body.
- [ ] **T004** [US1] `pnpm exec prettier --check scripts/pre-commit.test.mjs` and `pnpm lint`
  clean (memory: eslint clean is not prettier clean).

Dependencies: T001 → T002 → T003 → T004.
