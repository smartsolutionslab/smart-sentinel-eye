# Verification: 061 — a boot failure names its own cause

**Feature**: 061 | **Issue**: #2061 | **Date**: 2026-09-04
**Branch**: `fix/2061-a-boot-failure-names-its-cause` (HEAD `6d56305` at the
time of observation)

Phase 5 (ADR-0037). The question is not "do the tests pass" — phase 4 answered
that at a Docker-free seam over pure functions, 13/13. The question is whether
**the real fixture, on a real boot failure, now names its cause**, because
every test on this branch feeds hand-built dictionaries to
`FormatFailedResourceReport` / `FormatLikelyCause` / `SelectResourcesToReport`
and none of them shows those functions receiving data from an actual boot.

`spec.md` said this could not be checked — *"A boot failure cannot be provoked
deterministically, so nobody can watch this happen on demand"* — and marked the
log-delivery half **tier 2, expected but not proven**. **Both were provoked and
both were watched.** The spec's sentence is wrong and this note supersedes it.

Everything below was observed on **Windows 11, .NET 10.0.400, Aspire 13.5.3**,
against a real Docker-backed fixture boot in `isE2ETests` mode. The
pre-existing persistent run-mode containers (suffix `-18bcf406`) were left
running and untouched; the two provoked runs used ephemeral containers and left
none behind.

## How the failure was provoked

One scratch line in `src/MigrationRunner/Program.cs`, immediately after
`await host.StartAsync();`, making `migrations` die:

```csharp
// SCRATCH (#2061 phase 5 verification) — provoke a non-zero exit from
// `migrations` so the real fixture produces a real failure report. REVERT.
if (args.Length >= 0)
{
    throw new InvalidOperationException("SCRATCH: deliberate migration failure for #2061 phase 5 verification.");
}
```

Nine services `WaitForCompletion(migrations)` under `isE2ETests`, so a non-zero
exit puts all nine into `FailedToStart` — the exact shape of CI run
33623647778. The run:

```sh
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
  --filter "FullyQualifiedName~A_camera_registration_reaches_the_camera_catalog_log_tail"
```

It timed out after the full 8-minute budget at
`WaitForResourceAsync("camera-catalog", Running)` — the same wait that timed
out in CI. The scratch line was reverted afterwards (see §5).

## 1. What the report said — the observation this phase exists for

Verbatim, from the `TimeoutException` message (`provoked-run.log:98`,
`:305–365`, elided where noted):

```
   System.TimeoutException : Aspire AppHost did not start within 8 minutes.
Likely cause: migrations exited with code -532462766 — a non-zero exit is a failure, not a clean finish.
Resource states:
  ...
  migrations: Finished (exit code -532462766)
  migrations-rebuilder: NotStarted
  ...
Failed-resource logs:
---- audit-observability (FailedToStart — never launched, so an empty log is expected) ----
...
---- migrations (Finished, exit code -532462766 — the process ran and died) ----
...
2026-09-04T08:25:51.9030000Z [sys] Starting process...: Cmd = C:\Program Files\dotnet\dotnet.exe, Args = ["run", "--project", "D:\\Github\\smart-sentinel-eye\\src\\MigrationRunner\\SmartSentinelEye.MigrationRunner.csproj", "--no-build", "--configuration", "Debug", "--no-launch-profile"]
2026-09-04T08:25:55.8280000Z       Hosting starting
2026-09-04T08:25:56.1160000Z       Application started. Press Ctrl+C to shut down.
2026-09-04T08:25:56.1900000Z Unhandled exception. System.InvalidOperationException: SCRATCH: deliberate migration failure for #2061 phase 5 verification.
2026-09-04T08:25:56.1900000Z    at Program.<Main>$(String[] args) in D:\Github\smart-sentinel-eye\src\MigrationRunner\Program.cs:line 76
2026-09-04T08:25:56.1900000Z    at Program.<Main>(String[] args)
---- overlay-designer (FailedToStart — never launched, so an empty log is expected) ----
```

All four things the phase was asked to check are present:

- `migrations` is **selected** into the failed-resource section — it was not,
  before;
- its header names the state **and** the non-zero exit code, and distinguishes
  it from the nine that never launched;
- a `Likely cause:` line names `migrations` and the code, at the top, before
  the 46-line state list;
- the never-launched services carry an explanation rather than a bare
  placeholder.

**And more than the spec claimed.** The `migrations` section is **not** empty:
it carries the process's own stdout through to the unhandled exception and the
file and line that threw. So `ResourceLoggerService.WatchAsync` **does** serve
a resource whose process has already exited, at least on this build. The spec's
tier-2 contingency — *"if a future real boot failure shows `---- migrations
(Finished, exit code 134 …) ---- (no logs captured)`, then … that is when it
becomes a third issue"* — **was not triggered**. Of the two outcomes the brief
asked me to distinguish, this is the good one: the report both names the cause
and shows the crash. **No new issue is warranted on that ruling's trigger.**

## 2. The acceptance grep, with its controls

The spec's grep, on the real report:

```sh
$ grep -icE "migration.*fail|abort|SIGABRT"  provoked-run.log     → 2
$ grep -c  "(no logs captured)"              provoked-run.log     → 0
```

**Negative control — the same grep on the failure this issue was filed
against.** The job log was downloaded first (a re-run erases the failure from
CI history):

```sh
$ gh run view 33623647778 --log > ci-33623647778.log
$ grep "^integration tests (Docker)" ci-33623647778.log > ci-integration.log
$ wc -l ci-integration.log                                          → 27605
$ grep -icE "migration.*fail|abort|SIGABRT"  ci-integration.log     → 0
$ grep -c  "(no logs captured)"              ci-integration.log     → 2628
```

**0 → 2, and 2628 → 0.** Both of the spec's headline figures reproduce, and
both move the right way.

**One of the two matches is mine, and the note says so rather than banking it.**
The matching lines are:

```
99:Likely cause: migrations exited with code -532462766 — a non-zero exit is a failure, not a clean finish.
363:... Unhandled exception. System.InvalidOperationException: SCRATCH: deliberate migration failure for #2061 phase 5 verification.
```

Line 363 matches only because my scratch message contains the words "migration
failure". The load-bearing match is **line 99**, produced by `FormatLikelyCause`
from real snapshot data, and it appears for any non-zero `migrations` exit
whatever the message. **The honest figure attributable to the fix is 1, not 2.**

## 3. The empty case — the report does not invent a cause

Asked for explicitly by the brief; **provoked, not reasoned about.** The scratch
line was replaced with `await Task.Delay(Timeout.InfiniteTimeSpan);` so
`migrations` hangs instead of dying: the boot still fails, but **nothing exits
non-zero**. Same command, same 8-minute timeout. Verbatim
(`emptycase-run.log:53–115`, elided):

```
   System.TimeoutException : Aspire AppHost did not start within 8 minutes.
Resource states:
  ...
  camera-catalog: Waiting
  ...
  migrations: Running
  migrations-rebuilder: NotStarted
  ...
Failed-resource logs:
---- audit-observability (Waiting) ----
...
---- automation (Waiting) ----
```

- **No `Likely cause:` line at all** — `grep -c "Likely cause" → 0`. The report
  claims no cause when it has none.
- `migrations: Running` is **not** selected — the #1918 shape holds.
- The nine services are listed with a bare `(Waiting)` header: their state, and
  no invented explanation, because "never launched" is not what happened to
  them.
- `grep -icE "migration.*fail|abort|SIGABRT" → 0`. **This is the stronger
  negative control**: the *new* code, on a real boot failure whose cause was not
  a migration death, still answers 0. The grep tracks the fact, not the fix.

## 4. No regression on the healthy path

Both runs above are deliberate failures, so neither shows a healthy boot still
working. Scratch reverted, then:

```sh
$ dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release \
    --no-build --filter "FullyQualifiedName~LogTailDeliversIntegrationTests"
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 43 s
```

The stack boots, `migrations` exits 0, the stricter `IsHealthy` does not report
it, and no report is produced at all — which is the correct outcome and the
#1918 regression this change had to avoid.

## 5. Build, analyzers, and the tree

```sh
$ git checkout -- src/MigrationRunner/Program.cs
$ git status --short                                    → (empty)
$ dotnet build -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet test ... -c Release --no-build --filter "FullyQualifiedName~AspireFixtureReportSelectionTests"
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 78 ms
```

Release is clean at zero warnings under `TreatWarningsAsErrors`. **No
suppression was added.** `AspireFixture.cs` is 831 lines against ADR-0084's
300-line limit and Release does not flag it — pre-existing (it was already ~724
before this branch, which adds 107), unchanged in kind by this change, and left
alone deliberately rather than papered over.

**Latency**: `N/A — test-harness diagnostic on the startup-timeout path. It
touches no constitution §IV leg and executes only when the fixture has already
failed to boot.`

## What was **not** covered

1. **CI's own emptiness is not explained.** Locally *every* selected resource
   returned logs, including the nine `FailedToStart` services, which returned
   Aspire orchestration output. In CI those same nine returned nothing. So the
   Linux/CI log-delivery behaviour that produced 2,628 placeholders is **still
   unobserved**; what is now established is that the local Windows build serves
   an exited resource's logs. Do not read §1 as proof that CI will.

2. **The exit code was `-532462766`, not `134`.** A Windows unhandled exception,
   not a Linux SIGABRT. The selection is code-agnostic (`is not null and not 0`),
   so this is a difference in the digits printed and not in the branch taken —
   but the literal 134 of run 33623647778 was not reproduced.

3. **A minor wording finding, non-blocking, for the reviewer.** In §1 the header
   `(FailedToStart — never launched, so an empty log is expected)` sat above ~14
   to 25 lines of real orchestration log. The clause states an *expectation* the
   very same section then contradicted. It is correct about CI (where those
   sections were empty) and correct about the state, but "an empty log is
   expected" reads badly directly above a non-empty log. Not fixed here — this
   is phase 5, and the change is not wrong, only over-committed in its wording.

   **Closed in phase 6.** The header now reads `(FailedToStart — never reached
   a running state)`: the state, and no prediction about the section beneath
   it. `A_resource_that_ran_and_died_is_distinguishable_from_one_that_never_launched`
   asserts the new clause, so the distinction the header exists to draw is
   still pinned.

4. **One test, not the suite.** Each provoked run executed a single test filter;
   the fixture fails before any test body runs, so the report is what was under
   observation, not the suite's own greenness. The full integration suite was
   not run on this branch.

5. **Log retrieval is verified only incidentally.** The seam this change touches
   is selection and formatting. §1 happens to show `migrations`' logs coming
   back, which is a fact about `ResourceLoggerService` on this build and not
   something this change makes true or keeps true.
