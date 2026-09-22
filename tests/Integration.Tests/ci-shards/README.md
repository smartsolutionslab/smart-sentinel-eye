# Integration test CI shards

Four files, each one line: a vstest `--filter` expression selecting the
classes assigned to that shard, ANDed with the same
`Category!=Measurement&Category!=Disruptive&Category!=Maintenance`
exclusion `ci.yml`'s `integration-shards` job has always applied (some
classes mix an excluded-category method with an included one — the AND is
load-bearing, not decorative; see `specs/218-ci-shard-slow-jobs/plan.md`
§1-2).

Computed once (2026-09-22, `origin/develop` commit `2d44dd14`) via
LPT (longest-processing-time-first) bin-packing over each class's *actual
discovered* test-case count (`dotnet test --no-build --list-tests --filter
"Category!=Measurement&Category!=Disruptive&Category!=Maintenance"`, not a
source-level `[Fact]`/`[Theory]` count, which undercounts `[Theory]` row
expansion). Balance achieved: 157/157/157/157 of 628 discovered tests.

**This is a static, committed partition, not a runtime computation** — the
`integration-shard-coverage` job in `ci.yml` is what keeps it honest: it
re-runs the same discovery on every push and fails loudly if the union of
these four files stops matching the full test set exactly (a class added
without being assigned to a shard, a class renamed out of every filter,
etc.). If that job goes red for a genuine drift (not a flake), rebalance by
re-running the same discovery command above, re-partitioning by count, and
recommitting these four files — there's no script for this today because
the partition changes rarely enough that regenerating by hand, verified
against the coverage guard, was judged not worth the added indirection.
