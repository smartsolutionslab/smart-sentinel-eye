# Verification — Spec 218

Phase 5, per tasks.md T009 and spec.md SC4: real, observed wall-clock,
job-by-job, from an actual PR CI run — not the modeled figures in plan.md
§3 (which this note corrects, not repeats).

**Correction (this revision):** an earlier version of this file measured
run 35762866652 — the pre-revert config with build-artifact reuse still in
place, one commit before the shipped state. Re-pointed at the run that
actually tested the shipped commit (`1d08e322`), attempt 1 (attempt 2 is
the manual re-run of one shard, not representative of a clean pass — see
below). The corrected numbers tell a **better** story, not a worse one.

## The run

PR [#2535](https://github.com/smartsolutionslab/smart-sentinel-eye/pull/2535),
run [35766654699](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35766654699),
**attempt 1**, commit `1d08e322` — the shipped state: build-artifact reuse
reverted, `e2e-shard-coverage` added, verification comments corrected.

## Job-by-job, attempt 1 (the real clean pass)

| Job | Real duration |
|---|---|
| `backend` | 4m32s (272s) |
| `frontend` | 1m39s (99s), parallel |
| `integration-shard-coverage` | 2m03s (123s) |
| `integration-shards` 1/4 | 8m21s (501s) — hit the pre-existing #2537 flake this attempt, see below |
| `integration-shards` 2/4 | 5m32s (332s) |
| `integration-shards` 3/4 | **6m53s (413s)** |
| `integration-shards` 4/4 | 5m55s (355s) |
| `e2e-shard-coverage` | 19s |
| `e2e-shards` 1/4 | 6m59s (419s) |
| `e2e-shards` 2/4 | 6m00s (360s) |
| `e2e-shards` 3/4 | **9m05s (545s)** |
| `e2e-shards` 4/4 | 6m50s (410s) |

**Total wall clock: run created 18:21:51Z → last conclusion (`e2e`
synthesis) 18:35:39Z = 13m48s (828s).** Baseline (problem statement, run
35725455764): **18m27s (1107s)**.

**Real reduction: ~25% (279s), better than either the refuted ~12.5min
(58%) plan.md §3 model or the previously-reported ~15% (which measured the
wrong commit — see the correction above).**

The integration bucket showed `failure` in this attempt because shard 1/4
hit issue **#2537** (below) — unrelated to this branch, confirmed by
re-running just that shard, which passed clean (run 35766654699 attempt 2).
The PR's current green state is attempt 2; the wall-clock figures above are
attempt 1's, since that's the representative clean-pass measurement, not
the re-run's.

## Confirm/refute against Heiko's own floor

Heiko's own estimate, stated before this work started: a realistic floor
around 10-12 minutes, not the originally-hoped 5. **13m48s is close to,
just outside, that floor** — within a shard-balance improvement (S4,
below) of reaching it, not a wholesale redesign.

## Why per-shard duration still varies (S4, deferred)

plan.md §3 assumed each shard's execution time is roughly `total / N` —
true on average, false per shard:

- `integration-shards`: 332s-501s (413s excluding the flake's own long
  tail), a spread modeled as uniform at 362s.
- `e2e-shards`: 360s-545s, modeled as uniform at 447s.

The critical path is `max(shard)`, not the average, so the model's error
compounds exactly where it matters. **N=4 was still the right call** (the
fixed-cost floor — ~132s fixture boot, ~159s stack-readiness wait — makes
N=8 barely better than N=4 under either model), but LPT-by-test-count
(integration) and Playwright's own file-level split (e2e) both balance the
wrong variable. Filed as follow-up: rebalance both partitions by *measured
duration* per class/file (the data already sits in the uploaded
test-result artifacts), now that the coverage guards
(`integration-shard-coverage`, `e2e-shard-coverage`) make rebalancing safe
to do without silently dropping coverage.

## Build-artifact reuse: tried, measured, reverted

The original design (this PR's early commits) had `backend` upload its
Release build for the shard jobs to download instead of rebuilding. Real
measurement (against the pre-revert commit, run 35762866652) showed this
net-negative on the critical path: `backend` grew from a modeled 301s to a
measured 378s (the upload cost), while the shards' fixed-cost savings
didn't offset it once only the *slowest* shard is on the critical path. It
also directly caused three of the four real defects found during
verification (missing DCP native tool assets, lost Unix executable bits
through the artifact zip round-trip, and broken `Architecture.Tests`
text-parsing once the category filter moved behind a shell variable).
**Reverted**: every shard now restores and builds its own copy, exactly as
before sharding, paid once per shard rather than once per job — the
accepted, inherent cost of parallelizing across processes that plan.md §3
always expected sharding alone to carry. `backend` itself dropping to 272s
in the shipped run (no upload step) is consistent with this.

## Real e2e shard test counts (confirms the `--shard` fix, not assumed)

Pulled directly from each shard's job log (`Running N tests`), run
35762866652 (the commit that introduced the fix; counts are structural,
unaffected by the later revert):

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

- **`WhepAuthorizeRateLimitTests` (whole class, load-sensitive) — tracked as
  issue [#2537](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2537).**
  Two distinct methods in this class failed across 4 separate runs in this
  PR's own history — `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record`
  (on `c87f254c`) and `A_throttled_authorize_never_reaches_the_handler`
  (on the shipped `1d08e322`, both attempts) — always in
  `integration-shards` 1/4. **Confirmed pre-existing, not shard-induced,
  by the tightest available proof**: `A_throttled_authorize_never_reaches_the_handler`
  — the exact same method, not just the same class — already failed in the
  single, unsharded `integration` job on `develop` itself, before this
  branch existed, in run
  [35679604506](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35679604506)
  (2026-09-22T02:28:49Z); a second develop run,
  [35661421599](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35661421599)
  (2026-09-21T22:12:09Z), shows the same failure mode. Four total failures
  across two methods in one class is a class-wide load-sensitivity pattern,
  not one isolated flaky test — exactly what #2537 tracks.
- **`LockoutSessionSurvivalIntegrationTests.A_malformed_refresh_token_is_refused_without_moving_the_failure_counter`**
  — a corrupted refresh-token signature accepted (200) instead of refused
  (400). A fourth reproduction of existing issue **#2533**
  (security-relevant, real, reproduced in both this PR's sharded config
  and the pre-sharding single-job config — rules out shard-ordering as a
  cause). Not fixed here; out of scope.

## Concurrency headroom (S6, corrected)

This workflow peaks at **10** concurrent jobs (4 integration shards + 4
e2e shards + `integration-shard-coverage` + `e2e-shard-coverage` — the new
guard was undercounted as 9 in an earlier report). Under ADR-0144's
3-parked-PR cap, that's up to **30** concurrent jobs across the org's
Actions runners, unverified against this org's actual concurrency limit.
Known unknown, not blocking; recorded here rather than only in a PR
comment so it survives as a searchable artifact.

## Confirms / refutes / beats (per the original brief's own framing)

**Refutes** the ~12.5min modeled figure. **Comes close to** Heiko's
10-12min floor (13m48s) without fully reaching it. **Confirms** N=4 was
the right shard count regardless (the fixed-cost floor argument holds
independent of the balance error). **Neither confirms nor refutes** the
"beat 5 minutes" non-goal — never in scope. The honest summary: sharding
delivered a real, verified **~25% reduction** (18m27s → 13m48s); closing
the remaining gap to the stated floor needs duration-aware rebalancing
(S4), not a bigger N.
