# Feature Specification: A boot failure names its own cause

**Feature Branch**: `fix/2061-a-boot-failure-names-its-cause`

**Created**: 2026-09-04

**Status**: Draft — phases 1–3 complete, phase 4a not started

**Issues**: #2061

**Lane**: ADR-0144 autonomous. `agent:ready` present, `agent:blocked` absent,
board status *In Progress*.

**Input (#2061)**: "When `AspireFixture` cannot boot, it throws a
`TimeoutException` carrying a resource-state list and a 'Failed-resource logs'
section. In the one CI failure anyone has examined, that report printed
`(no logs captured)` 2,628 times and never once named the cause — which was
sitting in the state list it had already printed."

---

## Verification of the premise — done here, not inherited

The brief for this spec said to check the evidence rather than accept it,
because the issue this one displaced (#2060) was written from a relayed error
string that turned out to be a reconstruction, and two agents in a row produced
a false negative on it using a tool that was not installed. So every number
below was re-derived, with a control.

The job log was **downloaded first** (a re-run flips the whole run to success
and erases the failure from history), then grepped locally:

```
$ gh run view 33623647778 --json conclusion,headBranch,createdAt,event
failure | 057-typed-at-the-boundary | 2026-09-02T11:15:21Z | pull_request

$ gh run view 33623647778 --log-failed > run-33623647778-failed.log
$ wc -l run-33623647778-failed.log
27175

$ grep -ic "no logs captured"                       run-33623647778-failed.log   → 2628
$ grep -icE "migration.*fail|abort|SIGABRT"         run-33623647778-failed.log   → 0
$ grep -c  "exit code 134"                          run-33623647778-failed.log   → 292
$ grep -ic "Failed-resource logs"                   run-33623647778-failed.log   → 292
$ grep -ic "zzzzz-not-present-zzzzz"                run-33623647778-failed.log   → 0   (control: negative)
$ grep -ic "migrations"                             run-33623647778-failed.log   → 584 (control: positive)
```

The last two lines are the control the brief asked for: the same grep on the
same file returns zero for a string that cannot be there and a non-zero count
for one that must be. So the 0 for `migration.*fail|abort|SIGABRT` is a real
absence, not a broken tool.

**Both headline numbers reproduce.** Two findings modify how they should be
read, and both change the scope decision below.

### Finding 1 — the report emits nine placeholder lines, not 2,628

`2628 = 9 × 292`, exactly. The `Failed-resource logs:` header appears **292**
times: once per failing test, because a collection-fixture exception is
re-rendered by xUnit for every test in the collection. Each rendering contains
**nine** `(no logs captured)` lines — one per `FailedToStart` service.

So the report the fixture writes is about 75 lines long and contains nine
placeholders. The thousands are xUnit's multiplier, and the fixture cannot
reach it. Collapsing nine placeholder lines into one summary line takes 2,628
to 292 and takes the whole log from 27,175 lines to roughly 24,800 — a 9% cut
that leaves 292 copies of the report untouched. **The noise is the 292, and it
is not ours.** This is the arithmetic behind the scope ruling.

### Finding 2 — the cause *is* printed, 292 times, and still cannot be found

`migrations: Finished (exit code 134)` appears **292 times** — once in every
state list. The issue says so too ("sitting in the state list it had already
printed"), and the count confirms it literally.

So "never named the cause" is not quite the defect. The defect is that the
cause is rendered **in the same typeface as the forty-four resources that are
fine**, and the `Failed-resource logs:` section — the part of the report whose
entire job is to say *which resource broke* — omits it and lists nine
irrelevant services instead. A reader scanning for the failure looks at the
section labelled failures. It does not contain the failure.

The zero for `migration.*fail|abort|SIGABRT` measures exactly this: the string
present is `Finished (exit code 134)`, which contains none of the words a
person greps for when they suspect a crash. You can only find it if you
already know to look for it.

One verbatim report from the run, trimmed of the CI line prefix:

```
System.TimeoutException : Aspire AppHost did not start within 8 minutes.
Resource states:
  ...
  mediamtx: Running
  migrations: Finished (exit code 134)          ← the cause, indistinguishable
  migrations-rebuilder: NotStarted
  ...
Failed-resource logs:
---- audit-observability ----
(no logs captured)
---- automation ----
(no logs captured)
---- camera-catalog ----
(no logs captured)
---- event-ingestion ----
(no logs captured)
---- identity ----
(no logs captured)
---- layout-composition ----
(no logs captured)
---- overlay-designer ----
(no logs captured)
---- stream-distribution ----
(no logs captured)
---- system-variables ----
(no logs captured)

Last camera-catalog logs:
(tail subscribed but the resource emitted nothing)
```

(The last line is #2054's fix working: the tail reports an omission rather than
reading like output.)

### Finding 3 — the stated mechanism is wrong, and the real one is worse

The issue and the brief both say `CaptureFailedResourceLogsAsync` "collects
logs only for resources in the `FailedToStart` state". **It does not.** The
string `FailedToStart` does not appear in `AspireFixture.cs` at all:

```
$ grep -n "FailedToStart" tests/Integration.Tests/Fixtures/AspireFixture.cs
(no matches)
```

The actual selection, read from the current file (post-#2054, current
`develop` at `48f3b00`), is `AspireFixture.cs:342-357`:

```csharp
internal static string[] SelectResourcesToReport(Dictionary<string, string> states) =>
    states
        .Where(kv => !IsHealthy(kv.Key, kv.Value))
        .Select(kv => kv.Key)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

private static bool IsHealthy(string name, string state) =>
    state is "Running"
    || (state is "Finished" && IsOneShot(name))
    || (state is "NotStarted" && name.EndsWith("-rebuilder", StringComparison.Ordinal));

// Resources that run once and stop; finishing is how they succeed.
private static bool IsOneShot(string name) =>
    name is "migrations" || name.StartsWith("migrations-", StringComparison.Ordinal);
```

`migrations` is excluded not because it fails a `FailedToStart` test, but
because `IsHealthy` classifies it as **healthy**. `Finished` + one-shot is
success, unconditionally, *whatever the exit code*. The report did not
overlook `migrations`; it examined it and pronounced it fine while the state
list two inches above printed exit code 134.

That exemption was added deliberately by #1918 — before it, `Finished` counted
as failure for everything, so a successful `migrations` was reported as a
failure with no logs, alongside eleven idle rebuilders, and the one service
that had actually died was omitted. The fix was right and over-corrected: it
bought migrations an exemption from ever being suspected.

The exit code is already captured, in the same watch loop
(`AspireFixture.cs:309-310`), into a separate `_exitCodes` field — and is
passed to `FormatResourceStates`, which renders it, but **not** to
`SelectResourcesToReport`, which decides. The fact needed to make the right
call is collected, printed, and withheld from the decision.

This correction matters for the fix. "Also select `Finished` resources" would
be wrong (it re-breaks #1918). The change is narrower: *the one-shot exemption
must be conditional on the exit code*.

---

## The seam, verified

`tests/Integration.Tests/Fixtures/AspireFixtureReportSelectionTests.cs` runs
the selection logic with no Docker and no fixture. Confirmed by running it:

```
$ dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
    --filter "FullyQualifiedName~AspireFixtureReportSelectionTests"
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 82 ms
```

Six tests, 82 ms. The seam is real and fast.

**It does not admit the test as the issue words it.** The issue asks for "a
state map containing `migrations: Finished (exit code 134)`". There is no such
state map: `SelectResourcesToReport` takes `Dictionary<string, string>` and the
values are bare state text (`"Finished"`). The exit code is not an input to the
function under test, which is the defect restated as a signature. Making the
red test expressible therefore requires widening that signature first — see
`plan.md`, which makes it the first task and explains why that is honest rather
than a dodge.

---

## User Scenarios & Testing

### User Story 1 — the person reading a red CI run (P1)

An engineer opens a failed `integration tests (Docker)` job. Nine services are
`FailedToStart`. They need to know, within about thirty seconds, which resource
actually broke, so they file the right issue against the right component.

**Why this priority**: it is the whole issue, and it has already cost the
project a wrong issue (#2060, filed against MediaMTX, premise did not survive
checking) and a second wrong reading of the same signature. There is no P2.

**Independent test**: hand `SelectResourcesToReport` the state map and exit
codes from run 33623647778 and assert that `migrations` is among the selected
resources and is labelled as having exited, not as having failed to start.
Runs in milliseconds without Docker.

#### Acceptance scenarios

**Happy path — a one-shot that died is selected and named**

```gherkin
Given the resource states from run 33623647778
  And "migrations" is "Finished" with exit code 134
  And nine services are "FailedToStart" with no exit code
 When the startup-timeout report selects resources to report
 Then "migrations" is among the selected resources
  And its section header names the state "Finished" and the exit code 134
  And its section is distinguishable from a "FailedToStart" service's section
```

**The #1918 regression this must not cause — a one-shot that succeeded stays out**

```gherkin
Given "migrations" is "Finished" with exit code 0
  And "camera-catalog" is "FailedToStart"
 When the report selects resources to report
 Then "migrations" is not selected
  And "camera-catalog" is
```

**The absent-exit-code case — unknown is not a failure**

```gherkin
Given "migrations" is "Finished" and no exit code was observed for it
 When the report selects resources to report
 Then "migrations" is not selected
```

*Rationale*: the snapshot's `ExitCode` is nullable and the state map is built
from whichever watch event arrived last, so an absent code means "not
observed", not "zero". Treating unknown as failure would report `migrations` on
runs where nothing is wrong with it, which is #1918 again. The existing test
`A_one_shot_job_that_finished_is_not_reported` already pins this and must keep
passing **unmodified in its assertion**.

**Conflict / mixed — a died one-shot and failed services together**

```gherkin
Given "migrations" is "Finished" with exit code 134
  And nine services are "FailedToStart"
  And eleven "-rebuilder" resources are "NotStarted"
  And fifteen infrastructure resources are "Running"
 When the report is formatted
 Then the cause line names "migrations" and exit code 134
  And no "-rebuilder" resource appears in the failed-resource section
  And each "FailedToStart" service's empty log is presented as expected
      rather than as an unexplained placeholder
```

**Bad request / degenerate input — an empty map**

```gherkin
Given no resource states were captured
 When the report is formatted
 Then it says the app was not built
  And it does not claim a cause
```

**Auth**: N/A. This is a test-harness diagnostic. It crosses no trust boundary,
reads no credential, and is unreachable from any deployed process. No
`RequireScope`, no fab scoping, no idempotency surface. Stated so the phase-6
reviewer can see it was considered rather than skipped — and so
`security-reviewer` is not spawned for a change that has nothing for it to
read.

---

## Independent end-to-end test procedure

**Tier 1 — provable, and what phase 5 will actually verify.**

1. `dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj --filter "FullyQualifiedName~AspireFixtureReportSelectionTests"`
2. All tests pass, including the new ones, in under a second, with no Docker.
3. A dedicated formatting test renders the **full** report from the exact
   state map and exit-code map of run 33623647778 and asserts on the produced
   string: `migrations` appears in the failed-resource section with its exit
   code; the nine services appear with an explanation rather than a bare
   placeholder; no `-rebuilder` appears.
4. Re-run the two greps from the top of this document against **that produced
   string**. `grep -icE "migration.*fail|abort|SIGABRT"` must now be non-zero.
   This is the closest thing to an end-to-end check that exists here: it is the
   literal query a human ran on the real log, answered by the new report.

**Tier 2 — expected, not proven, and said so on purpose.**

Whether `migrations`' own stdout comes back is a separate question. The report
fetches logs through `ResourceLoggerService.WatchAsync(resourceId)`, and
nothing in this repository establishes what that returns for a resource whose
process has already exited. It may return the buffered output; it may return
nothing.

**The fix is worth landing either way**, and this is the point of the section
headers carrying the reason: if the logs come back, the report explains the
crash. If they do not, the report still reads

```
---- migrations (Finished, exit code 134 — the process ran and died) ----
(no logs captured)
```

which names the cause, distinguishes it from the nine services that never
launched, and hands the reader #2062. Today it says nothing at all.

**Do not claim tier 2 was verified.** A boot failure cannot be provoked
deterministically, so nobody can watch this happen on demand. If the next real
CI boot failure shows an empty `migrations` section, that is a *new* issue
about log delivery for exited resources — see the ruling on `TailedResources`
below, which names the trigger.

---

## Requirements

- **FR-001**: A one-shot resource that finished with a **non-zero** exit code
  is selected for the failed-resource report.
- **FR-002**: A one-shot resource that finished with exit code **0**, or with
  **no observed exit code**, is not selected. (#1918 must not regress.)
- **FR-003**: Each selected resource's section header states *why* it was
  selected — the state, and the exit code where one exists — so that a resource
  that ran and died is distinguishable from one that never launched.
- **FR-004**: An empty log for a resource that never launched is presented as
  the expected outcome it is, not as an unexplained placeholder. (The fixture
  already records this reasoning at `AspireFixture.cs:250-256`, from #2038; the
  report does not yet say it.)
- **FR-005**: When any resource exited non-zero, the report carries a leading
  cause line naming that resource and its exit code, before the state list.
- **FR-006**: The behaviour is covered by tests that run without Docker and
  assert on *which resource is selected* and *what the report says about it* —
  never on the report's shape, and never that a method was called.

### Out of scope (each with its reason)

- **Why the MigrationRunner exited 134** — #2062, open, unlabelled for the
  lane. This spec makes that question findable; it does not answer it.
- **#2060** — stays blocked. Its premise was a misreading of this very
  signature.
- **Deduplicating the placeholder lines** — see the scope ruling.
- **Adding `migrations` to `TailedResources`** — see the ruling.
- **The 292 repeats of the report** — xUnit's rendering of a collection-fixture
  exception, once per test in the collection. Not reachable from the fixture,
  and worth ~90% of the log volume. If anyone wants it, it is a different
  issue about test-collection structure, not about diagnostics.

---

## Scope ruling — the three "Done means" items are not one change

The issue lists three things. The brief asked me to decide whether they belong
in one change rather than let them travel together because they appeared in one
list. They do not all belong.

**Items 1 and 3 are the same change and both ship here.** "A non-zero-exit
resource appears distinctly from a failed-to-start one" (item 1) and "the cause
is prominent rather than buried" (item 3) are the selection defect and its
presentation. Once `migrations` is selected, naming it in a section header and
in a cause line is the same commit's natural completion — you cannot make it
prominent without first making it selected, and having made it selected it
would be perverse to render it anonymously.

**Item 2 — deduplication — is separable, and I am ruling it out of this
change.** Three reasons, in descending order of force:

1. **The arithmetic does not support it.** Finding 1 above: the report emits
   nine placeholder lines. Collapsing nine into one saves 8 lines out of a
   75-line report, and 2,336 lines out of 27,175. The 2,628 figure that makes
   the item sound urgent is 292 × 9, and the 292 is out of reach.

2. **FR-004 addresses the actual complaint better.** The reader's problem with
   nine `(no logs captured)` lines is not that there are nine of them — it is
   that each one looks like a failure to collect evidence when it is in fact
   the expected and *correct* result for a process that never started. That is
   #2054's lesson exactly ("a dump that reads like output when it is an
   omission"), and here it runs the other way: an omission that reads like a
   defect. Explaining the nine is worth more than hiding them, and it costs one
   string rather than a summarisation pass.

3. **Summarising is how signal gets lost.** Nine resources with individually
   empty logs is a fact about the run. A line saying "9 resources produced no
   logs" is the same fact minus the names, and the next reader who needs to
   know *which* nine has to reconstruct it from the state list — which is the
   move this whole issue is about.

**The case for including it**, since the brief asked for both sides: it is
cheap (a `GroupBy` over the sections that came back empty), it is uncontestably
in the issue text, and a reviewer who reads only the issue will ask why the
third bullet was dropped. If the trade is judged the other way, it is a
one-task addition to `tasks.md` and nothing in this spec has to change.
**Recorded as deliberately declined, not overlooked**, and the reason is
written down so the next reader is not left guessing — which is the failure
mode this repository has recorded against itself four times.

---

## Ruling — should `migrations` be added to `TailedResources`?

**Out of scope, and it should not be done. Not a third issue either — a
contingency with a stated trigger.**

The brief is right that it addresses a different half: having the logs versus
selecting the resource. It is the *wrong* half, and it would not have helped.

1. **The report does not read tails.** `CaptureOneResourceLogAsync`
   (`AspireFixture.cs:411-447`) resolves the DCP resource id and calls
   `ResourceLoggerService.WatchAsync` on demand. `TailedResources` feeds a
   different consumer — `RecentLogs`, which tests call when they get a status
   they cannot explain. Adding `migrations` to the tail list would not put one
   character into the failure report. The report's problem was that it never
   asked about `migrations` at all.

2. **The cost is real and, still, unmeasured.** #2053 established each tail as
   a background task alive for the fixture's whole lifetime, and left
   assumption A1 — the marginal cost of one — explicitly unmeasured. Paying an
   unpriced cost on every run of the suite to serve a path that executes only
   on a timeout is speculative generality (ADR-0036).

3. **The guard would allow it and that is not an endorsement.**
   `LogTailCoverageTests` checks `requested ⊆ tailed` only. Its own XML doc
   names the gap: *"`tailed ⊆ requested` is unchecked too, so a name nobody
   asks about keeps its subscription — an unpriced cost while assumption A1 is
   still unmeasured."* An entry with no `RecentLogs` call site is precisely the
   entry that doc warns about. Adding one would be walking into a trap the
   repository already wrote down.

**The trigger that would change this ruling**: if a future real boot failure
shows `---- migrations (Finished, exit code 134 …) ---- (no logs captured)`,
then `WatchAsync` does not serve exited resources and the delivery half is a
genuine gap. **That is when it becomes a third issue**, filed with that
observation as its evidence — and even then a tail is one candidate fix among
several, not the obvious one. Filing it now would be filing an issue against a
behaviour nobody has observed.

---

## Locked technology choices

Nothing new. This change touches one file plus its test file.

- **ADR-0103** — integration tests run against the Aspire fixture, no
  Testcontainers. The fixture is the harness, and its diagnostics are part of
  it. This is the anchor ADR; spec 059 established the same reading for the
  same file.
- **ADR-0139** — new behaviour is observed failing first and the failure is
  quoted in the PR. Also: rules that matter fail the build. The new test in
  `AspireFixtureReportSelectionTests` *is* the guard — no separate architecture
  test is wanted here.
- **ADR-0144** — the autonomous lane; the two-agent phase-4 split and the
  declaration of phase 4a's colour at phase 3.
- **ADR-0052 / ADR-0053** — xUnit + Shouldly; sentence-style underscore test
  names.
- **ADR-0036** — smallest change; no speculative generality. Cited three times
  above and it is doing real work each time.
- **ADR-0105** — `Ensure.That` for argument guards. Likely not needed: the
  methods in question are `internal static` helpers called from one place
  inside the same type.

**No new ADR is required, and no ADR is missing.** Reasoning in `plan.md`
under *Declaration 2*.

**Constitution §II does not apply.** It binds domain models, and
`PrimitiveBoundaryTests` scopes to `src/*/Domain`. A `Dictionary<string,
string>` in a test fixture is not a domain model, and introducing value objects
for resource names here would be the speculative generality ADR-0036 forbids.
Noted so phase 6 can dismiss it in one line instead of debating it.

---

## Latency budget impact (constitution §IV)

**N/A — no leg.** The change is confined to
`tests/Integration.Tests/Fixtures/AspireFixture.cs` and its unit test. No
production code is touched, nothing runs in a deployed process, and no code on
the event-to-overlay path is modified, read, or re-timed. §VII's dashboard rule
for implemented legs is likewise not engaged.

---

## Assumptions

- **A1 — `ExitCode` is `0` for a successful `migrations` run**, not `null`.
  FR-002 is written to be correct either way (both are treated as healthy), so
  A1 being wrong costs nothing. Stated because a reviewer will want to know
  whether the null branch is defensive or load-bearing: it is load-bearing.

  **Corrected in phase 6.** This bullet named
  `A_one_shot_job_that_finished_is_not_reported` as the test that pinned it,
  and that test passes an **empty** dictionary — it pins the *absent-key*
  guard and says nothing about a present null. Nothing pinned the null branch
  at all: dropping `is not null` from `ExitedNonZero` built clean in Release
  with all thirteen tests green. It is now pinned by
  `A_one_shot_that_finished_with_a_captured_null_exit_code_is_not_reported`
  (selection) and
  `A_running_resource_with_a_captured_null_exit_code_is_not_named_as_a_cause`
  (the cause line), one per caller of the predicate, and that mutant now
  fails. Worth recording rather than editing away: the claim was written from
  the test's *name*, and a name is not an assertion.
- **A2 — the last watch event wins.** `CaptureResourceStateMapAsync`
  assigns into the dictionary on every event, so the recorded state and exit
  code are those of the final observation within its 3-second window. Unchanged
  by this work; recorded because FR-002's "no observed exit code" case depends
  on it.
- **A3 — `2628 = 9 × 292` implies one report per failing test.** Inferred from
  the counts plus xUnit's collection-fixture behaviour, not observed directly
  in the log's structure. If it is wrong the scope ruling on deduplication
  weakens, though the FR-004 argument stands on its own.

## Guesses marked

None unavoidable. The one place the issue's text was not followed — its
description of the selection mechanism — was corrected against the source
rather than guessed around, and the correction is Finding 3.
