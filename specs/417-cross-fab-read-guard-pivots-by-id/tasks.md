# Tasks 417: The cross-fab read-guard tests find their seeded fab-neutral row by identifier

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3035 (feature-level, already on Project
#13 and in the autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, `test/3035-pivot-null-fab-assertions`. Phase 4a colour: characterisation, observed green (plan §5), plus a
required counterfactual observed red on a throwaway branch.** No production code, so no `backend-engineer` task.

**Roles.** Tests: `test-writer` (T010-T012). Counterfactual: `test-writer` (T020). Review: `backend-reviewer` (T040).

Format: `[ID] [P?] [Story] description, files`. No `[P]` markers: every task edits or reads the same single test file,
so nothing fans out (ADR-0109). No foundational Shared.Kernel, Contracts or AppHost task.

## Phase 0: Setup

- [ ] **T001 [US1]** On the branch tip, confirm the two null-presence assertions are still at
  `CrossFabReadGuardIntegrationTests.cs:110` and `:162-164`, and that `NeutralRow()` returns the seeded `AuditEvent`.
  Re-check that 417 is still free (`origin/develop`, open PRs). No file change.

## Phase 1: US1 (the change)

- [ ] **T010 [US1]** In `Search_without_a_fab_filter_returns_the_callers_fabs_and_cross_fab_rows`: keep the seeded
  `NeutralRow()` in a local, replace `:110` with the identifier lookup + `fab` null assertion (plan §3). `:109` stays
  verbatim. File: `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`.
- [ ] **T011 [US1]** In `A_variable_archived_in_munich_is_not_returned_to_a_berlin_only_operator`: same, for the row
  seeded at `:138` and read at `:159-164`. Every other assertion in the test stays verbatim. Same file. Depends on T010
  (same file).
- [ ] **T012 [US1]** `dotnet build tests/Integration.Tests -c Release`: zero errors, no new warnings. Quote the summary.
  Write the plan §4 implication into the PR body draft. Depends on T011.

## Phase 2: Evidence (plan §5)

- [ ] **T020 [US1]** Counterfactual on a **throwaway branch** cut from the T012 commit (never the delivery branch):
  reseed `NeutralRow()` itself (used only by these two tests) as `FabScope.Owned` with no fab. Run locally if Docker is up, otherwise open a **draft
  PR** from it and read the integration job's trx. Expected: both tests red on the identifier lookup. Quote the
  failure verbatim, then close the draft PR and delete the branch. Depends on T012.
- [ ] **T021 [US1]** On the delivery PR, after CI concludes: download the integration job's trx and confirm both test
  names are `Passed`. Quote the two lines. Depends on T012. Not satisfied by a build, a skipped job or a cancelled run.

## Phase 3: Review and PR

- [ ] **T040 [US1]** `backend-reviewer` on the diff: checks plan §4 (new implies old; nothing deleted, no message
  weakened), that only the spec directory and the one test file changed (SC-4), and that the counterfactual quote is
  real. Depends on T012.
- [ ] **T050 [US1]** PR to `develop` (`--base develop`), Conventional Commit `test(3035): ...`, body quotes T012,
  T020 and T021, references `Closes` **nothing**: #3035 stays open for items 1-3 (owner). Merge only after T020 and
  T021 are both quoted.

## Dependencies

T001 -> T010 -> T011 -> T012 -> {T020, T021, T040} -> T050.
