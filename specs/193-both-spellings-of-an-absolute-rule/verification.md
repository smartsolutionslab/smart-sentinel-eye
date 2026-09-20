# Verification 193 — Both spellings of an absolute rule

Per `spec.md` §5 and `tasks.md` T004/T009/T010. All output quoted verbatim
below, captured 2026-09-20 against worktree `D:/Github/sse-2292`.

## T004 — phase 4a red, on the unmodified comparison

`dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`
against the tree at `ba7a94cc` (probe + companion fact added, comparison not
yet widened):

```
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.EventIngestion.Infrastructure") [971 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.LayoutComposition.Infrastructure") [421 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.AuditObservability.Infrastructure") [659 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Identity.Infrastructure") [375 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.OverlayDesigner.Infrastructure") [459 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.CameraCatalog.Infrastructure") [376 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Automation.Infrastructure") [349 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.StreamDistribution.Infrastructure") [501 ms]
[xUnit.net]     SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [FAIL]
  Shouldly.ShouldAssertException : offenders
      should be
  ["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository"]
      but was (case sensitive comparison)
  ["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository"]
      difference
  ["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", *]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.SystemVariables.Infrastructure") [491 ms]
Failed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [65 ms]

Test Run Failed.
Total tests: 10
     Passed: 9
     Failed: 1
```

`SyncOffenderRepository` — the synchronous-commit spelling — was not reported.
This is the red T005/T006 fix.

## T009 — corpus plant, on the real `CameraCatalog.Infrastructure` assembly

Steps below use `git stash` to isolate the comparison change (not the probe,
which was already committed at `ba7a94cc`) rather than the theory's own filter
list, and a plant in
`src/CameraCatalog/Infrastructure/Persistence/CameraRepository.cs`:

```csharp
// TEMPORARY — spec 193 T009 plant. Never merged; reverted before commit.
public void PlantedSyncCommit() => dbContext.SaveChanges();
```

**Step 2 — before the fix, plant present.** `git stash` set aside the
uncommitted comparison widening, leaving the pre-fix `==
nameof(DbContext.SaveChangesAsync)` comparison in place, with the plant added:

```
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.EventIngestion.Infrastructure") [32 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.LayoutComposition.Infrastructure") [8 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.AuditObservability.Infrastructure") [8 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Identity.Infrastructure") [7 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.OverlayDesigner.Infrastructure") [3 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.CameraCatalog.Infrastructure") [212 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Automation.Infrastructure") [4 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.StreamDistribution.Infrastructure") [9 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.SystemVariables.Infrastructure") [7 ms]

Test Run Successful.
Total tests: 9
     Passed: 9
```

`CameraCatalog.Infrastructure` passed with the plant in place — the guard did
not see the synchronous commit. This is the defect, observed on the real
corpus rather than on the probe.

**Step 3 — after the fix, plant still present.** `git stash pop` restored the
comparison widening; the plant was untouched:

```
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.EventIngestion.Infrastructure") [26 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.LayoutComposition.Infrastructure") [7 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.AuditObservability.Infrastructure") [7 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Identity.Infrastructure") [4 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.OverlayDesigner.Infrastructure") [3 ms]
Failed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.CameraCatalog.Infrastructure") [51 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.CameraRepository"]

Additional Info:
    SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.CameraRepository calls SaveChanges or SaveChangesAsync directly. Commit through ITransactionalCommit instead, so the rows and the integration events they announce land in one transaction (spec 021 FR-001). Committing directly is silent: the write succeeds, the caller is told the truth, and the announcement is never made.
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Automation.Infrastructure") [3 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.StreamDistribution.Infrastructure") [7 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.SystemVariables.Infrastructure") [4 ms]

Test Run Failed.
Total tests: 9
     Passed: 8
     Failed: 1
```

After the widening, the guard reports `CameraRepository` by name, and the
failure message names both spellings. This is the guard change, proven on a
real candidate assembly rather than the probe.

**Step 4 — revert, confirm green.** `git checkout -- CameraRepository.cs`,
then `touch` the file (a restored file keeps its original mtime and MSBuild
can skip the rebuild):

```
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.EventIngestion.Infrastructure") [23 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.LayoutComposition.Infrastructure") [6 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.AuditObservability.Infrastructure") [4 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Identity.Infrastructure") [4 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.OverlayDesigner.Infrastructure") [3 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.CameraCatalog.Infrastructure") [141 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.Automation.Infrastructure") [3 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.StreamDistribution.Infrastructure") [6 ms]
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.SystemVariables.Infrastructure") [3 ms]

Test Run Successful.
Total tests: 9
     Passed: 9
```

`git status --short` after the revert showed only `tests/Architecture.Tests/OutboxCommitTests.cs`
modified — the plant left no trace in `src/`.

## T010 — whole `Architecture.Tests` project, and Release build

`dotnet test tests/Architecture.Tests` (no filter), on the fixed tree:

```
Passed!  - Failed:     0, Passed:   433, Skipped:     0, Total:   433, Duration: 4 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

`dotnet build tests/Architecture.Tests -c Release`:

```
    195 Warning(s)
    0 Error(s)

Time Elapsed 00:01:04.49
```

All 195 warnings are pre-existing SonarAnalyzer advisories (`S107`, `S138`,
`S104`) in unrelated `src/*/Api` files, carved out of
`TreatWarningsAsErrors` per ADR-0084 — none in `tests/Architecture.Tests/`,
none naming `OutboxCommit*`, and no `error CS`/`warning CS` at all. No `!`
null-forgiving operator appears in `OutboxCommitTests.cs`.

## Companion fact — final run, on the fixed tree

```
Passed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [10 ms]
```

Exact two-element offender list (`AsyncOffenderRepository`,
`SyncOffenderRepository`) confirms `SeamCommitRepository` and
`FailureSubscriptionRepository` are correctly excluded — the false-positive
check phase 4a built for the substring-conflict design that was not chosen.
