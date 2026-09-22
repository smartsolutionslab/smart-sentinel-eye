# Spec 218 — Shard the two slow CI jobs

**Issue:** none — direct request from Heiko (live session), supervised lane.
Not a Project #13 board pickup.
**Branch:** `218-ci-shard-slow-jobs`
**Status:** Phase 1 complete — awaiting review before Phase 4
**ADRs touched:** none amended. Respects ADR-0037 (phased workflow),
ADR-0139 §Testing-adjacent discipline applied by analogy (no dropped
coverage even though this isn't a behaviour change), ADR-0087 (rebase-only
merge — every shard commit must build standalone at the workflow-file
level, though this is one file so that's automatic).

---

## Problem, measured

CI run [35725455764](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35725455764)
(green, `develop`, 2026-09-22): **18m27s** wall clock. Job breakdown (from
`gh api repos/.../actions/runs/35725455764/jobs`, step-level timestamps):

| Job | Wall | Notes |
|---|---|---|
| `frontend` | 2m04s | parallel with `backend`, off critical path |
| `backend` | 5m01s (301s) | build+unit+coverage; gates `integration` and `e2e` |
| `integration` | 12m21s (741s) | `needs: [backend]`. Fixed 103s (checkout/dotnet-setup/cache/docker-verify/restore/build) + **633s** `dotnet test` step, of which ~132s is `AspireFixture`'s own boot (per the existing ci.yml comment) |
| `e2e` | 13m14s (794s) | `needs: [backend, frontend]`. Fixed 331s (55s setup + 108s "Build the stack" + 159s "Wait for the stack to be ready" + 9s teardown) + **463s** `pnpm test:e2e` step |

Critical path: `backend` (301s) → `e2e` (794s from backend's finish) = 1095s ≈
18m15s, matching the observed 18m27s.

**Both slow jobs already run everything sequentially in one process**:
- `integration`: 599 `[Fact]`/`[Theory]` **source attributes** across (measured
  directly, not assumed — see plan.md §1) **133 classes / 628 discovered test
  cases** (Theory `InlineData` rows expand at discovery time, which is why the
  case count exceeds the attribute count) all under
  `Category!=Measurement&Category!=Disruptive&Category!=Maintenance`, no
  parallelization directive anywhere in the project (`xunit.runner.json`
  absent, no `[CollectionBehavior]`), one shared `dotnet test` invocation.
- `e2e`: 24 Playwright spec files, `playwright.config.ts` already sets
  `workers: isCI ? 1 : undefined` — single-worker in CI today.

## What this delivers

Shard `integration` and `e2e` via a GitHub Actions matrix so each pays its
fixed cost once per shard but its variable (test-execution) cost divided by
N, in parallel. Full coverage runs on every push — this is not permission to
drop, skip, or narrow any test. Investigate (and land if safe) reusing
`backend`'s already-built Release output in both jobs instead of each shard
re-restoring and re-building the same commit's code from scratch.

## Out of scope

- `backend`'s own 5m01s — not what was asked; noted in the report if a
  trivial win is spotted, not acted on here (Karpathy: smallest possible
  change, don't mix a refactor of `backend` into this).
- Any reduction in *what* runs — the FixtureLogic-tagged classes that already
  run redundantly in both `backend` and `integration` today stay exactly as
  redundant as they are now. Removing that duplication is a legitimate,
  separate, low-risk follow-up (noted in plan.md), not folded into this
  change, because it changes the `integration` job's filter semantics and
  this change should be reviewable as "sharding, nothing else."
- k3s/Helm deploy — unrelated.

## Success criterion (stated up front, per Karpathy guidelines)

1. A real PR against `origin/develop`, opened from this branch, shows the
   sharded `integration` and `e2e` jobs actually running on GitHub's
   infrastructure (not a local model of them).
2. The union of all `integration` shards' discovered test list is byte-for-
   byte identical to today's unsharded discovered test list under the same
   filter — verified by a new CI step, not asserted by hand. Same test
   *count* in, same test count out, every push (an ongoing invariant, not a
   one-time claim about today's partition).
3. Every Playwright spec file still runs exactly once across the `e2e`
   shards (Playwright's own `--shard` guarantees this; verified against the
   real job summary's test count).
4. The real, observed total wall-clock from the PR's actual CI run is
   reported job-by-job, the same way the problem statement above was
   measured — not predicted.
5. `infra-reviewer` reviews before merge. This agent does not merge its own
   infra change to the CI gate every PR depends on.

## Non-goals

- A guaranteed 5-minute total. Heiko's own fixed-cost modeling puts a
  realistic floor around 10-12 minutes once AspireFixture's ~132s boot and
  the e2e stack's ~159s wait are paid per shard; this spec confirms, refutes,
  or beats that with real numbers, it doesn't promise beating it.
- Touching `deploy/helm/` or the Aspire k8s publisher.
