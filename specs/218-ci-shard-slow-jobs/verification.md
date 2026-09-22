# Verification — Spec 218

Phase 5, per tasks.md T009 and spec.md SC4: real, observed wall-clock,
job-by-job, from an actual PR CI run — not the modeled figures in plan.md
§3 (which this note corrects, not repeats).

## The run

PR [#2535](https://github.com/smartsolutionslab/smart-sentinel-eye/pull/2535),
run [35762866652](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35762866652),
commit `c87f254c` (the first commit where every genuine defect found during
verification — DCP package assets, lost executable bits, `Architecture.Tests`
text-parsing, the `pnpm`/`--` shard no-op — was already fixed). Two of the
job attempts needed a manual re-run for reasons unrelated to this change
(documented below); this note reports the clean, single-pass numbers as the
comparable figure, and separately notes what the re-runs cost.

## Job-by-job, single clean pass (attempt 1 + the one legitimate rerun
folded in only where noted)

| Job | Real duration |
|---|---|
| `backend` | 6m18s (378s) |
| `frontend` | 2m11s (131s), parallel |
| `integration-shard-coverage` | 47s |
| `integration-shards` 1/4 | 7m29s (449s) — this attempt hit a pre-existing flake, see below |
| `integration-shards` 2/4 | 4m36s (276s) |
| `integration-shards` 3/4 | 6m25s (385s) — this attempt hit a pre-existing bug, see below |
| `integration-shards` 4/4 | 4m53s (293s) |
| `e2e-shards` 1/4 | 6m00s (360s) |
| `e2e-shards` 2/4 | 6m25s (385s) |
| `e2e-shards` 3/4 | **9m17s (557s)** — the run's tail |
| `e2e-shards` 4/4 | 6m21s (381s) |

**Total single-pass wall clock: 15m47s (947s)**, run-created (17:46:45) to
the last originally-passing check's conclusion (`e2e` synthesis, 18:02:32).
Baseline (problem statement, run 35725455764): **18m27s (1107s)**.

**Real reduction: ~15% (160s), not the ~12.5min (58%) plan.md §3 modeled.**

## Why the model was wrong: confirm/refute against Heiko's own floor

Heiko's own estimate, stated before this work started: a realistic floor
around 10-12 minutes, not the originally-hoped 5. **This run's 15m47s does
not reach that floor.** The gap traces to one thing: plan.md §3 assumed each
shard's execution time is roughly `total / N` — true on average, false per
shard. Real spread:

- `integration-shards`: 276s-449s (excluding the flake's own long tail),
  a 1.6x spread. Modeled uniform: 362s.
- `e2e-shards`: 360s-557s, a 1.5x spread within this job, and the
  Playwright execution portion alone (subtracting each shard's ~fixed cost)
  swings further. Modeled uniform: 447s.

The critical path is `max(shard)`, not the average — so the model's error
compounds exactly where it matters most. **N=4 was still the right call**
(the fixed-cost floor — ~132s fixture boot, ~159s stack-readiness wait —
makes N=8 barely better than N=4 under either model), but the *achieved*
number is smaller than promised because LPT-by-test-count (integration) and
Playwright's own file-level split (e2e) both balance the wrong variable.
Filed as follow-up S4 (phase 6 review): rebalance both partitions by
*measured duration* per class/file, now that the coverage guards
(`integration-shard-coverage`, `e2e-shard-coverage`) make rebalancing safe
to do without silently dropping coverage.

## Build-artifact reuse: tried, measured, reverted

The original design (this PR's early commits) had `backend` upload its
Release build for the shard jobs to download instead of rebuilding. Real
measurement showed this net-negative on the critical path: `backend` itself
grew from a modeled 301s to a measured 378s (the ~64s upload cost is a
direct component of that), while the shards' fixed-cost savings (~48s for
integration, ~72s for e2e) don't offset it once only the *slowest* shard is
on the critical path — the other three shards' savings are free but
irrelevant to wall clock. It also directly caused three of the four real
defects found during verification (missing DCP native tool assets, lost
Unix executable bits through the artifact zip round-trip, and broken
`Architecture.Tests` text-parsing once the category filter moved behind a
shell variable). **Reverted**: every shard now restores and builds its own
copy, exactly as before sharding, paid once per shard rather than once per
job — the accepted, inherent cost of parallelizing across processes that
plan.md §3 always expected sharding alone to carry.

## Real e2e shard test counts (confirms the `--shard` fix, not assumed)

Pulled directly from each shard's job log (`Running N tests`), run
35762866652:

| Shard | Tests |
|---|---|
| 1/4 | 19 |
| 2/4 | 20 |
| 3/4 | 19 |
| 4/4 | 21 |

Full unsharded count: 67. Sum of shards (79) exceeds 67 because Playwright's
dependency-aware sharding runs the `seed`/`cleanup` setup-teardown projects
once per shard that needs them — documented Playwright behaviour, verified
by `e2e-shard-coverage`'s deduplicated-union check, not a defect.

## Two unrelated findings, not fixed here

- `WhepAuthorizeRateLimitTests.Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record`
  — recurred in `integration-shards` 1/4 across two separate runs (same
  shard, same class set, both by construction — the recurrence says nothing
  about which shard, only that the test itself is flaky). Not touched by
  this branch. Worth a follow-up issue.
- `LockoutSessionSurvivalIntegrationTests.A_malformed_refresh_token_is_refused_without_moving_the_failure_counter`
  — a corrupted refresh-token signature accepted (200) instead of refused
  (400). Independently identified as a fourth reproduction of existing
  issue **#2533** (security-relevant, real, reproduced in both this PR's
  sharded config and the pre-sharding single-job config — rules out
  shard-ordering as a cause). Not fixed here; out of scope.

## Confirms / refutes / beats (per the original brief's own framing)

**Refutes** the ~12.5min modeled figure and Heiko's 10-12min floor — real
result is 15m47s. **Confirms** N=4 was the right shard count regardless
(the fixed-cost floor argument holds independent of the balance error).
**Neither confirms nor refutes** the "beat 5 minutes" non-goal — never in
scope. The honest summary: sharding delivered a real, verified, ~15%
reduction; closing the gap to the stated floor needs duration-aware
rebalancing (S4), not a bigger N.
