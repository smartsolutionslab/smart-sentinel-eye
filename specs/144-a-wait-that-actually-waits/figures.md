# Figures — Spec 144, a wait that actually waits

Per `plan.md` § *Evidence artefacts this spec commits to producing* and the phase-6
finding (B1): a `develop` baseline and a post-fix figure, both with run id, SHA, median,
worst and five samples, plus a plain verdict on whether the figure moved. The blocker
review found none of this had been captured against the fully-folded code — this file
replaces that gap.

All figures are `[NFR spec 014 T031]` from
`NFR_VariableResolutionLatencyTests.Value_change_reaches_the_resolved_overlay_text_within_the_leg_budget`,
against the **global-keyed** implementation (as spec 014 T031 always was). The test
asserts only `median < 800 ms`; every figure below is one to two orders of magnitude
under both that assertion and constitution §IV's 200 ms budget for this leg.

## `develop` baseline (pre-fix, CI)

| Run id | SHA | Median | Worst | Samples |
|---|---|---|---|---|
| [34769639334](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/34769639334) | `3fe616740b99b6ad20e22a14106aec8f11a7d444` | 4 ms | 4 ms | `[3, 4, 4, 4, 4]` ms |

This is the commit this branch was cut from (`3fe61674`), carrying the readiness-wait
defect #2201 describes — `WaitUntilResolvableAsync` returns on the first 404. Retention
on the artifact is 14 days; this is the newest green `develop` run, downloaded and grepped
directly rather than linked, per spec 136's rule that the run id **and** the figure both
go in the tree.

## Post-fix, local (fully folded and reviewed code)

Aspire was not booted fresh for this — the stack's containers were already warm from
earlier work this session, so each run below is a genuine cold-JIT-once, warm-container
measurement, not a cold-stack one. Three runs, not two, because the first two were taken
immediately before the phase-6 fixes were committed and the third is the exact commit
this PR ends on; all three ran against byte-identical source (the review fixes touched no
code these two methods execute).

| Run | SHA | Median | Worst | Samples | Warmup samples |
|---|---|---|---|---|---|
| local-1 | `b3db0005` (working tree, pre-commit — content identical) | 11 ms | 33 ms | `[7, 8, 11, 13, 33]` ms | `[12, 14, 13]` ms |
| local-2 | `b3db0005` (working tree, pre-commit — content identical) | 17 ms | 29 ms | `[13, 16, 17, 20, 29]` ms | `[12, 23, 22]` ms |
| local-3 | `b3db00051d272017f7cbbf0b825943a92f4e7fa9` (exact, post-commit) | 16 ms | 27 ms | `[10, 14, 16, 21, 27]` ms | `[15, 10, 15]` ms |

Medians cluster tightly: 11, 16, 17 ms. This matches the one pre-existing local sample
the B1 review found (`chartests-run1.trx`, median 16 ms, taken after the fix but before
the fold) — three independent runs now corroborate that figure instead of leaving it as
a single unverified sample.

## Isolating the fix from the CI-vs-local confound

11-17 ms local looks like roughly 3-4x the 4 ms CI baseline, which would read as a moved
figure. But that comparison changes two things at once — the code **and** the
environment (GitHub Actions runner vs. this local dev machine, which also has Rider,
several idle MSBuild nodes and long-running Docker containers from earlier sessions). To
isolate the fix's own effect, `3fe616740b99b6ad20e22a14106aec8f11a7d444` — the exact SHA
the CI baseline above was built from, still carrying the pre-fix defect — was checked
out, rebuilt, and measured **locally**, twice, then the branch was restored:

| Run | SHA | Median | Worst | Samples |
|---|---|---|---|---|
| local-baseline-1 | `3fe61674` (pre-fix, local) | 26 ms | 54 ms | `[16, 19, 26, 39, 54]` ms |
| local-baseline-2 | `3fe61674` (pre-fix, local) | 13 ms | 17 ms | `[11, 13, 13, 14, 17]` ms |

Local pre-fix medians (13, 26 ms) and local post-fix medians (11, 16, 17 ms) overlap
directly — the pre-fix local figures are, if anything, the higher of the two sets. The
same commit that measured 4 ms on the CI runner measured 13-26 ms on this machine, a
gap of the same order as the local pre-fix-to-post-fix "move" that prompted this check.

## Verdict

**The figure did not move because of the fix.** The 4 ms → 11-17 ms gap between the CI
baseline and the post-fix local runs is explained by environment (CI runner vs. local
dev machine with other processes running), not by the readiness-wait correction: a
same-machine, same-environment comparison (pre-fix 13/26 ms vs. post-fix 11/16/17 ms)
shows the two ranges overlapping, with the pre-fix local figures running slightly
*higher* than the post-fix ones. This is the expected outcome US-3 predicted — the three
warmup rounds were already absorbing what the readiness wait failed to, so correcting the
wait was not expected to move the measured figure, and the same-environment comparison
confirms it did not, within the noise this machine produces run to run.

No constitution edit accompanies this file. §IV carries no cell for this test's number in
any case (its `Event → overlay state` row is the production metric, #1707) and ADR-0144
forbids the autonomous lane amending the constitution regardless. Had the figure moved,
this section would say so as a finding, unchanged in every other respect.
