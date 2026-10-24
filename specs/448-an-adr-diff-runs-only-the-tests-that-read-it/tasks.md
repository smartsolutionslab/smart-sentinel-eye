# Tasks 448: An ADR diff runs only the tests that read it

Spec: [spec.md](./spec.md) · Plan: [plan.md](./plan.md) · Issue #3122 (feature-level; no
per-task issues). **Phase 4a: red** (behaviour-changing).

Agents: phase 4a `test-writer`; phase 4b `infra-engineer`; phase 6 `infra-reviewer`.

**Parallelism:** none. T001 blocks T002. T002–T004 share `ci.yml` and the briefs that
`AgentBriefClaimTests` couples to it, so they are one commit by one agent. The `[P]`
markers below only mean "independent within the T002 commit".

## Phase 4a: red (test-writer)

- [ ] **T001 [US1]** Create `scripts/classify-ci-diff.test.mjs` per plan §4. It covers every
  spec §2 scenario: ADR-only, each of the six backend roots alone, specs+ADR,
  specs-only, ADR+src, the seven near-misses, the two renames, the empty diff, the zero base,
  the empty base, the unreachable base, exit 0 with one summary line naming the bucket, and
  the `ci.yml` wiring assertion. Run `node --test scripts/classify-ci-diff.test.mjs` and
  return the **verbatim** output. Expected: every case red, because the script is missing
  and `docs_only` is still present. Commit: `test(3122): pin the three-bucket diff
  classification`.

## Phase 4b: green (infra-engineer; may not edit T001's file)

- [ ] **T002 [US1]** Create `scripts/classify-ci-diff.sh` per plan §2: fail-safe first
  write, `conclude` naming the bucket, the path list in one `case`, a header restating the
  reader inventory, and executable mode in git (`git update-index --chmod=+x`).
- [ ] **T003 [P] [US1]** `.github/workflows/ci.yml`: `changes` exposes `bucket` and runs the
  script with `${{ github.event_name }}`, the base and `${{ github.sha }}`. Retarget every
  gate per plan §3, including `changes` in `integration-shards`, `integration-shard-coverage`
  and `e2e-shards` needs. Rewrite the header comment: three buckets, five reader guards
  (add `KioskRecoveryRecordTests`), and point to the script for the list. Update the stale
  `docs_only` comments on `integration`, `e2e` and `e2e-shard-coverage`.
- [ ] **T004 [P] [US1]** Briefs. In `.claude/agents/infra-engineer.md` and
  `infra-reviewer.md`, update the `changes` description (three buckets) and the `needs`
  clauses for the three jobs. In infra-reviewer, replace "a skipped bucket is acceptable
  ONLY when … specs/-only" with the backend rule. In `.claude/commands/next-issue.md`
  (§ When the watcher reports), add a second named exception: on `bucket=backend`,
  `frontend`, `integration-shards`, `integration-shard-coverage`, `integration`,
  `e2e-shards`, `e2e-shard-coverage` and `e2e` read skipped. That is green only if `changes`
  succeeded, its summary names `bucket=backend`, **and** `backend` concluded success.
- [ ] **T005 [US1]** Locally: `pnpm test:guards` (all guard tests green, including
  `lint-scope`, `pre-commit` and `summarise-e2e-retries`, which read `ci.yml`), and
  `dotnet test tests/Architecture.Tests` (AgentBriefClaimTests green against the new
  needs). Commit T002–T004 together: `ci(3122): run only backend on an ADR, brief or
  constitution diff`.

## Phase 5: verify

- [ ] **T006** Counterfactuals: (a) remove the `docs/adr/*` arm, and the ADR cases go red;
  (b) drop `== 'full'` from `integration-shards`, and the wiring test goes red; (c) put
  `.specify/*` in place of `.specify/memory/*`, and the `.specify/scripts` near-miss goes
  red. Restore each one and quote all three outputs.
- [ ] **T007** After the push: the feature PR itself is `full` (all jobs run, green). Open
  a throwaway draft PR stacked on the branch that touches only `docs/adr/README.md`.
  Observe `bucket=backend` in the summary, `backend` green, and the seven jobs skipped.
  Record the run URL, then close it unmerged and delete its branch. Write the
  verification note with obligation-1's inventory and these observations.

## Phase 6–7

- [ ] **T008** `infra-reviewer` on the diff. The PR body lists plan §6 (owner edits:
  optional CLAUDE.md line, ADR-0033 amendment note, memory exception) and quotes the T001
  red output.
