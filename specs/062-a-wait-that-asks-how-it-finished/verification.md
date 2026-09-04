# Verification: 062 — a wait that asks how it finished

**Feature**: 062 | **Issue**: #2064 | **Date**: 2026-09-04
**Branch**: `fix/2064-a-wait-that-asks-how-it-finished` (HEAD `fb69446` at the
time of observation)

Phase 5 (ADR-0037). Phase 4 answered "do the tests pass" at a Docker-free seam
over two pure functions, 5/5. This phase answers the question those tests
structurally cannot: **does the real fixture, on a real boot where the migration
runner dies, stop at the migrations wait instead of burning the whole
budget?** `plan.md` §"The honest limits" item 1 says why nothing committed can
answer it — deleting the call site leaves every one of those tests green. That
claim is not repeated here on trust; it was re-observed (§5).

Everything below was observed on **Windows 11 Pro 26100, .NET SDK 10.0.400,
Aspire.Hosting 13.5.3**, `-c Release`, against a real Docker-backed fixture boot
in `isE2ETests` mode. The pre-existing persistent run-mode containers (suffix
`-18bcf406`, 7 of them) were left running and untouched throughout; all four
provoked/healthy runs used ephemeral containers and left none behind
(`docker ps -a` after the last run listed nothing but the persistent set and the
tunnel proxy).

## How the failure was provoked

One scratch line in `src/MigrationRunner/Program.cs`, immediately after
`await host.StartAsync();` — the same seam #2061's phase 5 used, in the spelling
`tasks.md` T001 corrected to:

```csharp
// SCRATCH (#2064 phase 5) — provoke a non-zero exit from `migrations`. REVERT.
if (Environment.GetEnvironmentVariable("PATH") is not null)
{
    throw new InvalidOperationException("SCRATCH: deliberate migration failure.");
}
```

**Not #2061's `if (args.Length >= 0)`.** Confirmed again here: this run is
`-c Release`, `MigrationRunner` is a production project, and the build completed
with `0 Warning(s), 0 Error(s)` with the `PATH` form — the analyzer cannot fold
an environment read, and `S3981` never fires.

The run, twice, back to back on the same machine:

```sh
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release \
  --filter "FullyQualifiedName~A_camera_registration_reaches_the_camera_catalog_log_tail"
```

## 1. What the fixture said — the observation this phase exists for

Verbatim from **post-fix run 2** (`post-fix-2.log:50–119`, the migrations log
elided in the middle, `…` marking the elision):

```
[xUnit.net 00:01:38.51]     SmartSentinelEye.Integration.Tests.Fixtures.LogTailDeliversIntegrationTests.A_camera_registration_reaches_the_camera_catalog_log_tail [FAIL]
  Failed SmartSentinelEye.Integration.Tests.Fixtures.LogTailDeliversIntegrationTests.A_camera_registration_reaches_the_camera_catalog_log_tail [1 ms]
  Error Message:
   System.InvalidOperationException : migrations exited with code -532462766 — a non-zero exit is a failure, not a clean finish.
The startup wait stopped here rather than spending the remaining budget on services that wait for it.
migrations log:
2026-09-04T12:13:17.3189682Z Waiting for resource 'postgres' to become healthy.
…
2026-09-04T12:14:38.2320000Z       Hosting started
2026-09-04T12:14:38.2880000Z Unhandled exception. System.InvalidOperationException: SCRATCH: deliberate migration failure.
2026-09-04T12:14:38.2880000Z    at Program.<Main>$(String[] args) in D:\Github\smart-sentinel-eye\src\MigrationRunner\Program.cs:line 75
2026-09-04T12:14:38.2880000Z    at Program.<Main>(String[] args)
  Stack Trace:
     at SmartSentinelEye.Integration.Tests.Fixtures.AspireFixture.InitializeAsync() in D:\Github\smart-sentinel-eye\tests\Integration.Tests\Fixtures\AspireFixture.cs:line 156

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 7 ms - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

Post-fix run 1 produced the identical message, the identical exit code and the
identical throw site, at `[xUnit.net 00:02:28.11]`.

All four things the phase was asked to check are present:

- **The exception type is `InvalidOperationException`** — not `TimeoutException`
  and not an `OperationCanceledException` subtype, so the
  `catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)`
  at `AspireFixture.cs:214` (FR-006 cites it by its pre-fix line, `:182`) did not
  reclassify a 98-second failure as "did not start within 8 minutes".
- **The throw site is the migrations wait** — `AspireFixture.cs:156`, the throw
  inside the new `ExitedNonZero` branch. Pre-fix the stack pointed at the
  `camera-catalog` wait (FR-001).
- **The message names the resource and the exit code** — `-532462766`, the
  Windows unhandled-exception code, the same one #2061 saw and the case
  `A_negative_exit_code_is_a_failure` exists for. A `> 0` rule would have let
  this boot through (FR-002).
- **The message carries `migrations`' own log**, ending on the unhandled
  exception and its `Program.cs:line 75` frame — the actual cause is inside the
  window, not cut off by the 60-line tail (FR-003, Windows).

The log arrived **non-empty and full**: exactly 60 lines, i.e. capped by
`CaptureOneResourceLogAsync`'s `TakeLast(60)` rather than by anything being
missing. Same as #2061's Windows observation, and still says nothing about
Linux (§6).

## 2. The measurement — pre vs post, second run reported

**Both halves were run twice**, per the repository's own rule that the first run
after machine churn looks like a regression. The pre-fix pair is phase 4a's, on
this same machine; the post-fix pair is this phase's. The figure is the
**`[xUnit.net HH:MM:SS.ss]` marker on the `[FAIL]` line**, never the runner's
`Duration:` line — on a run where `InitializeAsync` throws, xUnit attributes the
fixture's time to neither the test nor the assembly, and `Duration:` here read
`8 ms` and `7 ms` for boots of two and a half minutes and a minute and a half.

| | Run 1 | Run 2 (reported) |
|---|---|---|
| **Pre-fix** (phase 4a) | `00:08:53.82` | `00:08:55.10` |
| **Post-fix** (this phase) | `00:02:28.11` | `00:01:38.51` |
| **Saving** | 6 m 25.71 s (385.7 s) | **7 m 16.59 s (436.6 s)** |
| Exception, pre-fix | `TimeoutException`, `camera-catalog` wait | same |
| Exception, post-fix | `InvalidOperationException`, migrations wait | same |

**The reported saving is 7 m 16.6 s per occurrence**, ≈ 82 % of the failing
run's wall clock. Run 1's smaller saving is the churn effect, not a different
outcome: both post-fix runs threw at the same wait with the same message, and
run 1's extra 50 s is Keycloak taking 87 s to become healthy on the cold pull
(`12:10:04 → 12:11:31` in `post-fix-1.log`) — time spent *before* the migrations
wait, which the fix does not touch and does not claim.

**The CI saving is not measured and is not claimed.** CI's time-to-death is
unrecoverable (`spec.md` §"Verification of the premise" — the migrations log is
one of #2061's empty placeholders), so the CI figure remains an upper bound of
8 minutes. What is measured is this machine, twice.

## 3. The healthy path is unaffected — the #1918 regression check

After reverting the scratch line, `git status --short` empty, and **rebuilding
`MigrationRunner` in Release** (a restored file keeps its old timestamp and
MSBuild will happily skip it — the Release binary was then confirmed to contain
no `SCRATCH: deliberate migration failure` string):

```sh
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release \
  --filter "FullyQualifiedName~LogTailDeliversIntegrationTests"
```

Twice, both green:

```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 31 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 37 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

A clean `migrations` reaches `Finished` with exit code 0, `ExitedNonZero`
returns false, the fixture proceeds through the remaining eleven waits and the
suite runs exactly as before (FR-004). No extra wait, and the bounded five-second
log read is not paid — the passing run's `Duration:` is fixture time included,
and 31–37 s is the same neighbourhood as the 45 s phase 4b recorded.

## 4. The Docker-free tests, for the record

```sh
dotnet test … -c Release --filter "FullyQualifiedName~AspireFixtureMigrationGateTests|FullyQualifiedName~AspireFixtureReportSelectionTests"
Passed!  - Failed:     0, Passed:    23, Skipped:     0, Total:    23, Duration: 77 ms
```

23 = the 5 new gate tests plus the 18 report-selection tests of #2061, unmodified
and still green.

## 5. The gap, re-observed rather than asserted

`plan.md` limit 1 says no committed test observes the wiring. That was checked,
not taken on faith: the call site at `AspireFixture.cs:136–158` was temporarily
replaced with the pre-fix two-line wait and the same 23 tests re-run.

```
Passed!  - Failed:     0, Passed:    23, Skipped:     0, Total:    23, Duration: 95 ms
```

**All 23 green with the fix removed.** The mutation was reverted immediately
(`git checkout --`; `git status --short` empty). So the only thing in existence
that exercises this change is the run in §1, and it is manual. Recorded here
because a limit that is stated but never tested is exactly the kind of claim
this repository has had to correct.

## 6. Latency

`N/A — test-harness startup diagnostic.` The change ships no `src/` code (the
scratch line of §"How the failure was provoked" is reverted and the tree is
clean), executes only inside `AspireFixture.InitializeAsync`, and touches no
constitution §IV leg. Same ruling as #2061, for the same reason. The 7-minute
figure above is CI/developer wall clock, not an event-to-overlay measurement,
and must not be read as one.

## 7. What was not covered

1. **Linux, and therefore CI.** Everything in §1 is Windows. #2061's phase 5
   left open that the same `CaptureOneResourceLogAsync` returned logs locally and
   nothing in CI's `integration` job, and that is still unexplained. The exit
   code — not the log — is what this change depends on, which is why FR-003
   requires the message to stand without it; but **no claim is made here about
   what CI's message will contain.**
2. **The empty-log case is unobserved.** FR-003's `(no logs captured)` branch was
   not produced: Windows served the log fully both times, and forcing the empty
   branch would need either the unexplained Linux behaviour or a mutation of the
   capture itself. The committed test
   `The_failure_message_names_the_code_before_it_shows_the_log` holds the
   ordering claim (code before log) that makes the message readable when the log
   is a placeholder, but that is a test, not an observation. **Unobserved, said
   plainly.**
3. **§5 is a one-time observation, not a regression test.** Tier 2 needs a
   production-source scratch edit that must be reverted before commit, so nothing
   in CI will notice if this regresses. The honest mitigation is that the symptom
   — a nine-minute `integration` job — is loud.
4. **The eleven `Running` waits still burn the whole budget.** Out of scope by
   `spec.md` §"Scope ruling 2"; a service that dies during startup lands in
   `Finished`/`FailedToStart` and its `Running` wait never matches. Same family
   as #1918/#2061/#2064, and the recommended follow-up issue is not yet filed.
5. **A real migration failure was never used.** The provocation is a synthetic
   throw immediately after `StartAsync`, so `migrations` dies before any migrator
   runs. A genuine EF failure mid-migration would exit non-zero the same way and
   take the same branch, but that path was not exercised.

## Verdict

**The behaviour holds.** The fixture stops at the migrations wait, names the
resource and the exit code, carries the runner's own log, throws a type the
existing catch does not reclassify, and does none of it on a healthy boot. The
measured saving is **7 m 16.6 s** on the second of two runs, against a pre-fix
`00:08:55.10` from the same machine.
