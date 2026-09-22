# Tasks — Spec 218

Gate: Heiko reviews this plan before T002 onward proceeds (explicit,
supervised — not the autonomous lane). T001 is already done (this plan).

- **T001** — Phase 1-3: measure, verify filter syntax against the pinned SDK,
  compute the LPT partition, write spec.md/plan.md/tasks.md. **Done.**
- **T002** — Commit the four `tests/Integration.Tests/ci-shards/shard-{1..4}.filter`
  files (verified content from plan.md §4).
- **T003** — `ci.yml`: shard `integration` into a 4-way matrix
  (`strategy.matrix.include`, job `name:` templated `(N/4)`), each leg
  reading its `.filter` file instead of the current single `--filter`.
  Update `integration-test-results` artifact name per shard
  (`integration-test-results-N-of-4`).
- **T004** — `ci.yml`: add `integration-shard-coverage` job (needs `backend`,
  no Docker) — full-filter `--list-tests` vs. union of the four shard
  `--list-tests`, fails loudly on any diff.
- **T005** — `ci.yml`: shard `e2e` into a 4-way matrix via Playwright's
  native `--shard=N/4`, job `name:` templated `(N/4)`. Update
  `playwright-report` artifact name per shard.
- **T006** — `ci.yml`: add the two synthesis jobs `integration` and `e2e`
  (`needs:` the respective matrix job, `if: always()`, fail if any shard
  failed) for check-name legibility/forward-compat (plan.md §7).
- **T007** — Build-artifact reuse: attempted (`backend` uploaded
  `bin/`+`obj/` as `release-build-output`; the shard/guard jobs downloaded
  it instead of rebuilding), measured net-negative on the critical path
  (the upload cost outweighed the shard-side savings once only
  `max(shard)` sits on the critical path — verification.md), and traced
  to 3 of 4 real defects found during verification. **Fully reverted** —
  every shard restores and builds its own copy again; see
  verification.md's "Build-artifact reuse" section for the numbers.
- **T008** — Push branch, open PR to `develop` (throwaway/observation PR —
  stated in the PR body; disposition decided after real numbers are in).
  Do not self-merge.
- **T009** — Phase 5: read real job timings via `gh api`, write
  `verification.md` with the job-by-job table (modeled vs. observed),
  confirm `integration-shard-coverage` passed on the real runner.
- **T010** — Request `infra-reviewer`. Do not merge — report branch/PR
  number and findings back for Heiko.

## Explicit non-tasks (in scope of the brief, deliberately not done here)

- Removing the `FixtureLogic`-class duplication between `backend` and
  `integration` (plan.md §5, "noted, not done") — filed as a follow-up
  issue in T010's report, not implemented.
- Any change to `backend` itself, beyond noting an obvious win if one
  surfaces (spec.md "Out of scope").
