# Verification 255 — #2586

**Phase**: 5 (Verify) · **Tree**: `b40acd42` (phase 4b) · **Latency budget: N/A** — every changed file is under `tests/`.

## 1. Full suite, branch tip

```
dotnet test tests/Architecture.Tests -c Debug
Passed!  - Failed:     0, Passed:   484, Skipped:     0, Total:   484, Duration: 7 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

484 = the 464 the phase-4a red run reported (463 passing + 1 red) plus the 20 new
theory cases the phase-4b widening added. All green.

## 2. Counterfactual A (US1) — a direct commit in an `*.Api` assembly is caught

Added a throwaway `src/CameraCatalog/Api/CounterfactualDirectCommitProbe.cs`:

```csharp
public sealed class CounterfactualDirectCommitProbe(CameraCatalogDbContext dbContext)
{
    public Task CommitDirectlyAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
```

`dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`:

```
SmartSentinelEye.Architecture.Tests.OutboxCommitTests.Nothing_commits_without_its_announcements(assemblyName: "SmartSentinelEye.CameraCatalog.Api") [FAIL]
Error Message:
 Shouldly.ShouldAssertException : offenders
  should be empty but had
1
  item and was
["SmartSentinelEye.CameraCatalog.Api.CounterfactualDirectCommitProbe"]
Failed!  - Failed:     1, Passed:    31, Skipped:     0, Total:    32, Duration: 7 s
```

Exactly the `CameraCatalog.Api` case failed, naming the planted type. This is the
shape #2586 named as invisible before this widening — an `*.Api` type resolving
its sibling `*.Infrastructure`'s concrete `DbContext` and committing directly.
Reverted (file deleted); `git status` clean, `git diff` empty.

## 3. Counterfactual B (US2) — an unscanned assembly is caught by the completeness fact

Removed `"SmartSentinelEye.CameraCatalog.Api"` from `ScannedAssemblies` (no
duplicate, no reorder):

```
SmartSentinelEye.Architecture.Tests.OutboxCommitTests.Every_assembly_that_can_reach_a_DbContext_is_scanned [FAIL]
Error Message:
 Shouldly.ShouldAssertException : unscanned
  should be empty but had
1
  item and was
["SmartSentinelEye.CameraCatalog.Api"]
Failed!  - Failed:     1, Passed:    30, Skipped:     0, Total:    31, Duration: 2 s
```

Only `CameraCatalog.Api` named — no other case turned red. Restored the entry;
`git diff` empty, confirmed with `dotnet test` returning to 484/484 green.

## 4. Finding

**No live offender.** Both the phase-1 measurement (architect) and the full
build/test cycle across phases 4a/4b/5 agree: the only direct-commit call site in
`src/` is `StreamFabAttributionService`, already exempt via `PermittedDirectCommits`
(spec 247 / #2469). This PR widens what the guard *looks at* — it does not change
what it *finds*. No `PermittedDirectCommits` entry added, no `src/` production
code changed (beyond the two reverted counterfactual probes above, never
committed).

## 5. Phase 6 review findings and resolution

`backend-reviewer` found one blocker and two should-fixes; both should-fixes
addressed in a follow-up commit, `c6c5cd26`.

- **Blocker — `Co-Authored-By` footer on both commits.** ADR-0030/0086 and
  CLAUDE.md ban that footer; only `Claude-Session` belongs. **Fixed**: both
  commits recreated with `git cherry-pick --no-commit` + corrected message
  (file content confirmed byte-identical via diff against the originals before
  they were replaced), commit hashes now `a830e416` (was `cb093608`) and
  `6432867a` (was `b40acd42`).
- **Should-fix — stale `PersistenceAssemblies` naming.** Correcting the record
  from this doc's earlier §5: the phase-4b widening commit *did* touch the
  completeness fact's body once, `reaching.Except(PersistenceAssemblies)` →
  `reaching.Except(ScannedAssemblies)`, because the rename would not otherwise
  compile — "untouched" was inaccurate. What phase 4b did not touch was the
  fact's *doc comments and message text*, which is what stayed stale (4
  occurrences). **Fixed** in `c6c5cd26`: renamed all four; no assertion or
  criterion changed.
- **Should-fix — vacuity guard covered only one of two criterion clauses.** The
  original guard anchored `CameraCatalog.Infrastructure`, reachable only via the
  EF Core-reference clause; a silently broken `*.Infrastructure`-reference clause
  would drop every `*.Api` assembly from `reaching` without the fact noticing.
  **Fixed** in `c6c5cd26`: added a second anchor, `CameraCatalog.Api`, reachable
  only via the Infrastructure-reference clause.
- **Nits** (informational, not fixed): the new fact's method length exceeds
  SonarAnalyzer's advisory 30-line guide (S138, suppressed for test projects,
  ADR-0084); the "Known limitation" doc paragraph now names `ScenarioSimulator`
  and `AppHost` as the two `src/` projects outside the test project's reference
  set (neither references EF Core today, so not a live gap) — applied in
  `c6c5cd26` alongside the should-fixes since it was a one-line addition to the
  same paragraph already being edited.

Full suite re-confirmed green after the follow-up commit: 484/484.
