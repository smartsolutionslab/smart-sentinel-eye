# Plan — Spec 218

## 1. Measurement before mechanism: what actually runs in `integration` today

The brief's premise ("599 Facts/Theory across 146 classes, all under one
`[Collection]`") doesn't survive contact with the source. Verified directly
(this worktree, commit `2d44dd14`, `origin/develop` tip):

- `grep -rc "\[Collection(AspireCollection.Name)\]" tests/Integration.Tests
  --include=*.cs`, summed over nonzero matches: **132** classes carry that
  attribute directly (xUnit's `[Collection]` is not inherited — a subclass
  without its own attribute is not in the collection).
- A further **17 files** have `[Fact]`/`[Theory]` but no `[Collection]`
  attribute: 15 are `Category=FixtureLogic` (Docker-free logic tests, already
  run once in `backend`'s "Docker-free fixture logic tests" step, and run
  *again*, redundantly, in `integration` today — see §5), 1 is
  `Category=Measurement`, 1 is `Category=Maintenance`.
- **Several classes mix categories at the method level.** `AcceptToDecideLatencyTests`,
  `LogTailDeliversIntegrationTests`, and others have some `[Fact]` methods
  tagged `Measurement`/`Disruptive` and other methods in the *same class*
  untagged. A shard filter that matches by class name alone
  (`FullyQualifiedName~ClassName`, no category clause) would silently
  re-include an excluded method — this is exactly the kind of drift the
  brief warned about, and it's real, not hypothetical (found by grep, then
  confirmed by discovery — see §2).
- Ground truth, from `dotnet test ... --no-build --list-tests --filter
  "Category!=Measurement&Category!=Disruptive&Category!=Maintenance"` (the
  exact filter `ci.yml` already applies): **133 classes, 628 discovered test
  cases.** The gap between 599 source attributes and 628 discovered cases is
  `[Theory(InlineData...)]` rows expanding at discovery time (e.g.
  `AppHostMigrationGateTests` has 3 `[Theory]` methods in source but 27
  discovered cases). The gap between 147 (132+15 by naive file-arithmetic)
  and 133 classes is the mixed-category classes above, where every method in
  a handful of otherwise-collection-tagged classes happens to carry an
  excluded category, removing the whole class from filtered discovery.

**This changes the partitioning mechanism.** A shard filter must AND the
class-membership clause with the *same* category exclusion the job already
applies — never assume class name alone is a safe unit.

## 2. Filter syntax — verified, not assumed

Built and ran `tests/Integration.Tests` locally against the pinned SDK
(`global.json`: `10.0.401`). Confirmed by actually invoking `dotnet test
--list-tests --filter "..."` (not read off docs):

```
(FullyQualifiedName~ClassA|FullyQualifiedName~ClassB|...)&Category!=Measurement&Category!=Disruptive&Category!=Maintenance
```

- **Parens for grouping work** in vstest's filter grammar under this pinned
  SDK — confirmed by running a 2-class OR grouped with the category AND, and
  getting the intersection, not the wrong-precedence result.
- **The AND-with-category-exclusion correctly drops mixed-category methods**:
  tested against `LogTailDeliversIntegrationTests` (3 `[Fact]`s, 1 tagged
  `Disruptive`) and `AcceptToDecideLatencyTests` (2 `[Fact]`s, 1 tagged
  `Measurement`) together — the combined filter returned exactly the 3
  expected surviving tests (2 + 1), not 5.
- **No class-name substring collisions** among the 133 discovered classes
  (checked programmatically: no class's full name is a substring of
  another's) — `FullyQualifiedName~X` (Contains) is safe today. The
  committed filter files anchor with a trailing `.` after the class name
  (`FullyQualifiedName~Namespace.ClassName.`) anyway, so a *future* class
  added with a name that happens to prefix another's can't create a silent
  cross-match — the `.` requires the next character to be the method
  separator.
- **Full round-trip verified**: partitioned all 133 classes into 4 shards
  (see §3), ran `--list-tests` per shard, and diffed the sorted union of all
  four shards' discovered names against the sorted full-filter discovery.
  **Zero diff. 628 in, 628 out, no duplicates.** This is the actual
  mechanism this plan proposes to commit, already proven correct against
  the real assembly — not a design that still needs to be tried.

## 3. Shard counts — fixed-cost math, not a round number

### Integration

Per shard: `103s` (checkout/dotnet-setup/cache/docker-verify/restore/build,
current — see §5 for the artifact-reuse alternative) + `132s` (AspireFixture
boot, paid again per process — inherent, not a bug) + `(501s / N)` (633s
measured `dotnet test` step minus the 132s boot) + `~2s` upload.

| N | Fixed | Variable | Total | vs N=1 |
|---|---|---|---|---|
| 1 (today) | 237s | 501s | 738s (12.3m) | — |
| 2 | 237s | 251s | 488s (8.1m) | −34% |
| 3 | 237s | 167s | 404s (6.7m) | −45% |
| **4** | **237s** | **125s** | **362s (6.0m)** | **−51%** |
| 5 | 237s | 100s | 337s (5.6m) | −54% |
| 6 | 237s | 84s | 321s (5.3m) | −57% |
| 8 | 237s | 63s | 300s (5.0m) | −59% |

N=4→5 saves 25s for one more concurrent runner; N=5→6 saves 16s; N=6→8 saves
21s across two more shards. Past N=4 the curve is flat — the 237s fixed
floor (dominated by the 132s fixture boot, which sharding cannot reduce)
increasingly dwarfs the shrinking variable term. **N=4** is the chosen
count: it captures the large first-order win (51%) while keeping runner
concurrency modest and the shard→bucket math clean (628 discovered tests /
4 = exactly 157 per shard, see §4 — a coincidence worth having, not
engineered).

### E2E

Per shard: `331s` fixed (55s setup + 108s "Build the stack" + 159s wait-for-
stack-ready + 9s teardown, current) + `(463s / N)` variable.

| N | Fixed | Variable | Total | vs N=1 |
|---|---|---|---|---|
| 1 (today) | 331s | 463s | 794s (13.2m) | — |
| 2 | 331s | 232s | 563s (9.4m) | −29% |
| 3 | 331s | 154s | 485s (8.1m) | −39% |
| **4** | **331s** | **116s** | **447s (7.4m)** | **−44%** |
| 5 | 331s | 93s | 424s (7.1m) | −47% |
| 6 | 331s | 77s | 408s (6.8m) | −49% |

Same shape, steeper floor: the 159s stack-readiness wait is not reducible by
sharding at all (each shard boots its own full Aspire run-mode stack —
Keycloak realm import, migrations, gateway, web apps). **N=4**, matching
integration for a legible, consistent matrix shape across both jobs.

### Combined critical path (modeled, to be confirmed by the real run)

`backend` (301s) → `max(integration N=4, e2e N=4)` = 301 + 447 ≈ **748s
(12.5min)**, inside Heiko's own stated 10-12 minute realistic floor. `frontend`
stays off the critical path. §6 of the report will carry the *observed*
figure, not this modeled one.

## 4. Integration shard partition — committed, guarded

**Mechanism: option (a) from the brief — a committed, explicit per-shard
class list — not a runtime hash.** Reasoning: a hash-of-name mod N would
still need to render as `FullyQualifiedName~X|...` OR-lists per shard (vstest
has no native modulo-hash filter), so it buys nothing over explicit lists
except opacity; and this codebase's own convention (`PrimitiveBoundaryTests`,
`HandlerDeconstructionTests`, the Phase-3 board-gate correction in
CLAUDE.md itself) is consistently: **prefer an explicit, checkable list over
implicit magic, and enforce it doesn't drift with a guard, not with faith.**

- Computed via LPT (longest-processing-time-first) bin-packing over the 133
  classes' *real discovered* per-class test-case counts (not source-grep
  counts, which undercount `Theory` row expansion — see §1). Balance
  achieved: **157/157/157/157** (628 total ÷ 4, exactly).
- Committed as four plain files,
  `tests/Integration.Tests/ci-shards/shard-{1..4}.filter`, each one line:
  the exact `(FullyQualifiedName~...)&Category!=...` string, so `ci.yml`
  just does `--filter "$(cat tests/Integration.Tests/ci-shards/shard-N.filter)"`
  — no script, no build step, maximally inspectable.
- **New CI job `integration-shard-coverage`** (needs `backend`, no Docker,
  ~20-30s): runs `--list-tests` once with the *full* existing filter and
  once per shard file, diffs the sorted union of the four shard results
  against the sorted full-filter result. Fails loudly — non-zero exit, names
  the missing or duplicated tests — on any mismatch. This is what actually
  protects "no gaps, no overlaps" *going forward*: a new test class added
  later without being added to a shard file makes this job red, not a
  silent hole. Static partition, enforced invariant — not "static partition,
  hope nobody adds a class."

## 5. Build-artifact reuse

`backend` already runs `dotnet restore SmartSentinelEye.slnx -p:Configuration=Release`
(8s, measured) and `dotnet build SmartSentinelEye.slnx -c Release --no-restore`
(90s, measured) — the **whole solution**, confirmed by the fact that
`backend`'s own next step, "Docker-free fixture logic tests", runs
`dotnet test tests/Integration.Tests/....csproj -c Release --no-build
--filter "Category=FixtureLogic"` straight off that build, with no
intervening restore/build of its own. `Integration.Tests` is part of
`SmartSentinelEye.slnx`.

`integration`'s own "Restore" (17s) + "Build integration tests" (69s) = 86s,
and `e2e`'s "Build the stack (Release)" (108s, restore+build combined) are
therefore **rebuilding the identical commit's code that `backend` already
built**, once per job today and — without reuse — once *per shard* after
sharding (4×86s = 344s and 4×108s = 432s of purely redundant compute added
by sharding alone, on top of what exists today).

**Decision: attempt it for both jobs, land it in this same change, verify
for real.**

- `backend` adds an upload step after "Build (Release)": tar `bin/` and
  `obj/` for every project under `src/` and `tests/` in `Release`
  configuration, upload as artifact `release-build-output`. Downstream
  `--no-build` steps never re-check timestamps (that's the entire point of
  `--no-build`), so the well-known cross-runner-timestamp problem with reused
  build outputs (checkout writing sources with a *later* mtime than the
  artifact's compiled outputs, which would normally trigger MSBuild's
  incremental-rebuild path) **does not apply here** — nothing downstream
  ever asks MSBuild to decide freshness; it's handed a binary and told to
  run it.
  - `obj/project.assets.json` embeds restore results that reference
    `~/.nuget/packages` — safe to reuse because all three jobs restore
    against the *same* path on the *same* `ubuntu-latest` image, keyed by
    the *same* `hashFiles(...)` cache key.
  - `integration` and `e2e` (each of the 4 shards) download this one
    artifact instead of running their own restore+build.
- **Risk, stated rather than hidden**: this is the change most likely to
  need a revert if the real PR run disproves it. The `e2e` half in
  particular reuses the build for a *running, orchestrating* process
  (`dotnet run --project src/AppHost/...`) rather than a static test
  assembly — a subtle staleness there would show up as a runtime failure
  inside the stack, not a build error, which is a worse failure mode to
  debug than "the build step failed." If the real run shows this breaking
  `e2e` (AppHost manifest resolution, container image references, etc.) in
  a way that isn't a quick fix, the fallback is: keep the sharding, drop
  the `e2e` half of artifact reuse, keep it for `integration` only (smaller
  blast radius, a static test assembly, easier to reason about), and file
  the `e2e` half as a follow-up. This will be decided from the real PR's
  logs, not guessed here.
- Artifact size/transfer time is also unmeasured until the real run —
  the whole solution's `bin/`+`obj/` tree could be large enough that
  upload+download eats into or exceeds the ~86-108s it's meant to save.
  Reported with real numbers in §6 of the final report, not estimated.

**Noted, not done**: the 15 `FixtureLogic` classes that run once in `backend`
and *again*, fully redundantly, inside `integration` today (§1) are a
legitimate separate win (roughly 106 of the 628 discovered cases, all
Docker-free, currently paying for a from-scratch Docker/Aspire-capable
process to run logic that needs neither) — filed as a follow-up rather than
folded in here, because it changes `integration`'s filter semantics and this
change should read as "sharding plus a build-reuse plumbing change," not
"sharding plus a filter-scope change," in one diff a reviewer has to hold in
their head at once.

## 6. E2E shard mechanism

Playwright's native sharding — `playwright.config.ts` gets nothing new;
the CLI already supports `--shard=<current>/<total>`. `pnpm test:e2e`'s
underlying command gets `-- --shard=${{ matrix.shard }}/4` appended (or the
`shard` config field, whichever the existing `test:e2e` package.json script
composes more cleanly with — checked at implementation time). `workers: isCI
? 1 : undefined` is orthogonal (in-shard worker count) and stays as-is.

Each of the 24 spec files is assigned to exactly one shard by Playwright's
own algorithm — no custom partition, no guard job needed (unlike
integration, there's no hand-rolled filter to drift out of sync with
reality; Playwright's `--shard` is a first-class, self-verifying mechanism
against whatever spec files exist at run time).

## 7. Job naming and check legibility

- Matrix `strategy.matrix.include` with a `shard` value 1-4; job `name:`
  templated as `` integration tests (Docker) (${{ matrix.shard }}/4) `` and
  `` e2e (Playwright, full stack) (${{ matrix.shard }}/4) `` so the PR
  checks list reads as four distinctly-named rows per job, not four
  identically-named ones.
- `develop` has **no required status checks** configured (confirmed:
  `gh api repos/.../branches/develop/protection` — no
  `required_status_checks` key at all), and this is the *only* workflow
  file in `.github/workflows/` (confirmed: `ls .github/workflows/` →
  `ci.yml` alone) — so nothing currently depends on the literal check names
  `integration tests (Docker)` or `e2e (Playwright, full stack)`. The
  "four-bucket manual read" (this repo's own documented gotcha, since
  `develop` has no required checks) is the only thing that reads these
  names today, and it's a human, not a required-check config.
- **Still adding two thin synthesis jobs** — `integration` and `e2e` —
  each `needs: [the matrix job]`, `if: always()`, failing if any shard
  failed. Two reasons: (1) legibility — one human-readable green/red row
  per slow job beats mentally ANDing four, which is exactly the risk the
  brief raised about the manual four-bucket read; (2) forward-compatibility
  — if `develop` ever gets required status checks (plausible, given how
  much of this repo's process is manual today), a stable check name to
  point them at already exists, rather than someone having to hunt down
  which of four shard-numbered names to require.

## 8. Artifacts

- `integration-test-results` → per shard, `integration-test-results-N-of-4`
  (trx + blame diagnostics), matching the existing `path:` globs — no
  merging; a reviewer chasing a red shard downloads that shard's artifact.
- `playwright-report` → per shard, `playwright-report-N-of-4`. Not merged —
  Playwright's own HTML report viewer works per-shard-run; a
  `merge-reports` step would be a legitimate follow-up if anyone wants one
  combined view, not required for this spec's success criterion.
- `coverage-report` (from `backend`) — untouched, `backend` isn't sharded.

## 9. Verification (Phase 5)

1. Push this branch, open a real PR to `develop` (title states it's for
   observing sharded CI timing; will be closed or merged based on Heiko's
   call once `infra-reviewer` has looked at it — not self-merged regardless).
2. Read the real run's job timings via `gh api repos/.../actions/runs/<id>/jobs`,
   the same query used to produce the problem-statement numbers.
3. Report job-by-job: `backend`, `frontend`, each `integration` shard, each
   `e2e` shard, the two synthesis jobs, `integration-shard-coverage`, and the
   total wall clock — compared against the modeled figures in §3.
4. Confirm `integration-shard-coverage` passed (proving no gap/overlap on a
   real GitHub Actions runner, not just this local worktree).
