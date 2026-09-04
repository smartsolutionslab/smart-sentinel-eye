# Feature Specification: A wait that asks how it finished

**Feature Branch**: `fix/2064-a-wait-that-asks-how-it-finished`

**Created**: 2026-09-04

**Status**: Draft — phases 1–3 complete, phase 4a not started

**Issues**: #2064

**Lane**: ADR-0144 autonomous. `agent:ready` present, `agent:blocked` absent,
board status *In Progress*.

**Input (#2064)**: "`AspireFixture` waits for the MigrationRunner to reach
`Finished` and does not look at how it finished. In CI run 33623647778,
`migrations` reached `Finished` with exit code 134, so the wait **succeeded**.
The fixture then went on to wait for nine services that could never start, and
burned the full `StartupTimeout` before throwing."

---

## The question that decides the approach — answered first, with commands

The issue is explicit that its own suggested predicate is a sketch, and that
one fact decides whether that sketch is even buildable:

> **Whether `ExitCode` is populated on the snapshot the wait predicate sees.**
> […] If it is not, the predicate shape above does not work and the fix needs a
> different mechanism. **Check this first — it decides the whole approach.**

It was checked, by decompiling the shipped assembly rather than by reading
documentation or reasoning from the type's shape. `ilspycmd 11.0.0.9375` was
installed for this and run against
`~/.nuget/packages/aspire.hosting/13.5.3/lib/net8.0/Aspire.Hosting.dll` — the
exact binary this repo restores (`Directory.Packages.props:25,32`).

### Finding A — the predicate overload exists

```
$ grep -o 'M:…ResourceNotificationService.WaitForResourceAsync[^"]*' Aspire.Hosting.xml
…WaitForResourceAsync(System.String,System.Collections.Generic.IEnumerable{System.String},System.Threading.CancellationToken)
…WaitForResourceAsync(System.String,System.Func{Aspire.Hosting.ApplicationModel.ResourceEvent,System.Boolean},System.Threading.CancellationToken)
…WaitForResourceAsync(System.String,System.String,System.Threading.CancellationToken)
```

Three overloads. The middle one takes `Func<ResourceEvent, bool>` and —
decompiled — **returns the `ResourceEvent` that matched**:

```csharp
public async Task<ResourceEvent> WaitForResourceAsync(
    string resourceName, Func<ResourceEvent, bool> predicate, CancellationToken cancellationToken)
```

The string overload returns only `Task<string>` (the state text). That
difference is load-bearing and is exploited below.

### Finding B — the predicate runs over the same event stream the fixture already reads

`WaitForResourceCoreAsync` is a plain `await foreach` over
`WatchAsync(token)`, filtered by resource name:

```csharp
await foreach (ResourceEvent item in WatchAsync(token))
    if (string.Equals(resourceName, item.Resource.Name, …) && predicate(item))
        return item;
```

That is the *identical* stream `AspireFixture.CaptureResourceStateMapAsync`
already consumes at `AspireFixture.cs:305–312`, where it assigns
`_exitCodes[evt.Resource.Name] = evt.Snapshot.ExitCode` for every event. So the
snapshot type a predicate receives is the same type from which #2061 already
reads exit codes successfully. **The question was never about the type.**

### Finding C — state and exit code are projected from one object, in one snapshot

This is the answer the issue asked for, and it is the strongest form available
short of a live boot. `Aspire.Hosting.Dcp.ResourceSnapshotBuilder.ToSnapshot(Executable, …)`
builds both fields from the same `executable.Status`, in the same
`previous with { … }` expression:

```csharp
string text2 = (executable.AppModelInitialState == "Hidden") ? "Hidden" : executable.Status?.State;
…
return previous with {
    ResourceType = previous.ResourceType ?? "Project",
    State    = text2,
    ExitCode = executable.Status?.ExitCode,
    …
};
```

`migrations` is `builder.AddProject<Projects.SmartSentinelEye_MigrationRunner>("migrations")`
(`src/AppHost/AppHost.cs:224`), so it is an Executable/Project resource and this
is the exact code path. There is **no branch in Aspire that publishes
`State = Finished` from one status while taking `ExitCode` from another.**

Note in passing that the *container* branch does not behave this way: it maps
`ExitCode == -1` to `null` deliberately. Irrelevant to `migrations`, recorded so
nobody generalises Finding C to the container waits.

### Finding D — `WatchAsync` replays, so the wait cannot start too late

```csharp
foreach (var s in _resourceNotificationStates)
    if (s.Value.LastSnapshot is not null)
        yield return new ResourceEvent(s.Value.Resource, s.Key, s.Value.LastSnapshot);
```

A fresh subscription first replays the latest snapshot per resource. So a
`migrations` that had already finished before the wait began is still seen, with
whatever exit code its latest snapshot carries.

### The ruling

**The issue's mechanism is available. Its predicate is still the wrong shape,
for a reason the issue could not have known.** See the next section — this is
the single most important finding in this spec.

---

## The finding that changes the design: `null or 0` in a *predicate* fails open

#2061's rule is **`null or 0` is success**, and it must be preserved exactly.
That rule is correct where #2061 uses it — in a **post-mortem report** over a
captured state map, where every observed resource holds a key and the `Running`
ones hold a *present null*, so reading unknown as failure would name 35
innocent resources.

Put the same rule inside a **wait predicate** and its failure direction
inverts:

```csharp
e => e.Snapshot.State?.Text == KnownResourceStates.Finished
     && e.Snapshot.ExitCode is null or 0        // ← matches on a null
```

- If the exit code were ever absent on the first `Finished` event, the
  predicate returns **true**, the wait **succeeds**, and the fixture proceeds
  exactly as it does today. The fix becomes a silent no-op — and it would look
  identical to a working fix on every healthy boot.
- If the exit code is present and non-zero, the predicate returns **false** and
  the wait simply … keeps waiting.

And that second branch is the one that matters, because of the second finding:

### Timing out at the migrations wait saves **no** wall-clock time

Every wait in `InitializeAsync` shares **one** budget, created before
`CreateAsync` at `AspireFixture.cs:88`:

```csharp
using CancellationTokenSource cts = new(StartupTimeout);   // 8 minutes, once
```

Every `WaitForResourceAsync` is passed `cts.Token`. So a predicate that never
matches does not fail earlier — it fails at **the same wall-clock instant** the
current code fails, when that one `cts` expires. All that changes is the
exception's site and its message ("failed to meet the predicate condition"
rather than a `TimeoutException` from the `camera-catalog` wait).

**The issue's stated value is time.** A predicate-only change delivers none of
it. This is the reason scope ruling 1 goes the way it does.

---

## Verification of the premise — CI's own figures, re-derived

The job log was downloaded before anything else (a re-run flips the run to
success and erases the failure):

```
$ gh run view 33623647778 --json jobs -q '.jobs[] | "\(.name) | \(.conclusion) | \(.startedAt) -> \(.completedAt)"'
frontend — lint + typecheck + test          | success   | 11:17:05 -> 11:18:23
backend — build + unit tests + coverage gate| success   | 11:17:04 -> 11:20:57
integration tests (Docker)                  | failure   | 11:21:00 -> 11:31:35
e2e (Playwright, full stack)                | cancelled | 11:21:00 -> 12:01:21

$ gh run view 33623647778 --log > ci.log ; wc -l ci.log        → 32134
$ grep "did not start within" ci.log | head -1
  2026-09-02T11:31:19.667Z  System.TimeoutException : Aspire AppHost did not start within 8 minutes.
$ grep "exit code 134" ci.log | head -1
  2026-09-02T11:31:19.669Z    migrations: Finished (exit code 134)
$ awk -F'\t' '$2=="Run integration tests"' ci.log | tail -2
  2026-09-02T11:22:29.242Z  ##[group]Run dotnet test …
  2026-09-02T11:31:31.382Z  Failed!  - Failed: 292, Passed: 57, Total: 349, Duration: 8 m 49 s
```

**The premise holds and is sharper than the issue states.**

- The step ran **9 m 02 s**; the test run itself reported **8 m 49 s**, of which
  the 8-minute fixture timeout is essentially all.
- 292 tests failed on one fixture exception; 57 passed — those are the
  Docker-free tests (`AspireFixtureReportSelectionTests` and friends), which is
  also why phase 4a can be red without Docker.
- The cause (`exit code 134`) is printed at 11:31:19 — **at throw time**, in the
  report #2061 built. It was knowable minutes earlier and nothing asked.

**What CI cannot tell us:** *when* `migrations` actually died. The migrations
log is one of the 2,628 `(no logs captured)` placeholders of #2061, so the
CI-side saving can be stated only as a bound — *at most* 8 minutes, minus
time-to-death. The figure has to be measured, and §"Independent end-to-end test
procedure" says how.

---

## User Scenarios & Testing

### User Story 1 — the person waiting on a red CI run (P1)

**As** an engineer whose PR just went red on `integration tests (Docker)`,
**I want** the fixture to stop the moment the migration runner dies,
**so that** I get the answer in a minute or two instead of nine, and so that a
30-minute job budget is not spent watching a boot that is already dead.

This is the whole slice. There is no second story; the change is one call site,
one decision function, and one throw.

#### Acceptance scenarios (Gherkin)

**Happy path — the failure is caught at its source**

```gherkin
Scenario: the migration runner dies and the fixture says so immediately
  Given the AppHost is booting in E2ETests mode
  And the MigrationRunner process exits with a non-zero code
  When the fixture's wait on "migrations" returns
  Then the fixture throws before waiting on any service that depends on migrations
  And the exception names the resource "migrations"
  And the exception carries the exit code
  And the elapsed time is bounded by how long migrations took to die, not by StartupTimeout
```

**The regression to avoid (#1918's shape) — a clean finish is untouched**

```gherkin
Scenario: a migration runner that succeeds changes nothing
  Given the AppHost is booting in E2ETests mode
  And the MigrationRunner completes and exits with code 0
  When the fixture's wait on "migrations" returns
  Then no exception is thrown
  And the fixture proceeds to the remaining waits
  And the integration suite runs exactly as it does today
```

**The conflict case — an exit code that was never observed**

```gherkin
Scenario: an unobserved exit code is not a failure
  Given the fixture has observed "migrations" in state Finished
  And the snapshot's ExitCode is a present null
  When the fixture decides whether the migration failed
  Then it does not report a failure
  And it proceeds, exactly as it does today
```

This is #2061's rule, transplanted, and it is the scenario that carries the
mutation-resistance of the whole change. It must be written with a **present
null** — `new Dictionary<string, int?> { ["migrations"] = null }` or an
explicitly-typed `(int?)null` argument — never with an absent key or an empty
dictionary. An empty dictionary passes for the wrong reason and #2061's blocker
was exactly this.

**The bad-request analogue — a negative exit code is still a failure**

```gherkin
Scenario: a Windows unhandled exception is a failure too
  Given the fixture has observed "migrations" Finished with exit code -532462766
  When the fixture decides whether the migration failed
  Then it reports a failure naming that code
```

Not decorative. #2061's phase 5 observed exactly `-532462766` on Windows against
CI's `134`, and a `> 0` mutant would pass every other scenario here.

**Auth** — `N/A`. No trust boundary, no scope, no token. This is a test-harness
change with no runtime surface. Recorded rather than omitted, because the spec
template asks for it.

---

## Independent end-to-end test procedure

Two tiers, because two different things are being claimed and only one of them
can be asserted by a committed test. **The split is stated up front rather than
discovered at phase 5** — see "The honest limits" in `plan.md`.

### Tier 1 — the decision, automated, Docker-free, red first

Runs in the `AspireFixtureReportSelectionTests` neighbourhood (a sibling file;
see `tasks.md` T002). No Docker, seconds, runs in CI on every PR:

```sh
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release \
  --filter "FullyQualifiedName~AspireFixtureMigrationGateTests"
```

Passes when: non-zero → a failure message naming the code; `0` → no failure;
**present null** → no failure; negative → a failure.

### Tier 2 — the behaviour, manual, runtime, measured

The only way to observe the wiring is a real boot with a real dying migration
runner, and #2061's phase 5 established that this is one scratch line
(`specs/061-a-boot-failure-names-its-cause/verification.md`, §"How the failure
was provoked"). Reproduce it here, **before and after**, on the same machine,
back to back:

1. Add the scratch throw to `src/MigrationRunner/Program.cs` immediately after
   `await host.StartAsync();`.
2. **On the pre-fix commit**, run a single test and time it:
   ```sh
   git stash -- tests/ && \
   dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release \
     --filter "FullyQualifiedName~A_camera_registration_reaches_the_camera_catalog_log_tail" \
     > pre-fix.log
   ```
   Record the runner's own `Duration:` line and which wait threw. Expected:
   ~8 min, `TimeoutException` from the `camera-catalog` wait — the CI shape.
3. **On the post-fix commit**, same command, `> post-fix.log`. Record the same
   two facts. Expected: seconds-to-a-couple-of-minutes, and an exception that
   names `migrations` and its exit code.
4. Revert the scratch line, `git status --short` must be empty, then prove the
   healthy path is unaffected:
   ```sh
   dotnet test … --filter "FullyQualifiedName~LogTailDeliversIntegrationTests"
   ```
   Expected: `Passed! - Failed: 0, Passed: 3`. This is the #1918 regression
   check and it is not optional.
5. **Run steps 2–4 twice.** The first run after machine churn looks exactly like
   a regression; the second is the figure.

The verification note reports `pre − post` in seconds, with both raw figures and
the machine named. It must **not** report the CI saving as measured — CI's
time-to-death is unrecoverable (§"Verification of the premise"), so the CI
figure is an upper bound of 8 m, stated as a bound.

---

## Requirements

- **FR-001** — When the fixture's wait on `migrations` returns with a state of
  `Finished` and an exit code that is **not null and not 0**, the fixture MUST
  throw before executing any subsequent wait.
- **FR-002** — That exception MUST name the resource and the exit code.
- **FR-003** — That exception MUST include `migrations`' own recent output where
  it is available, and MUST remain useful when it is not. (#2061 proved the log
  is served on Windows for an exited resource and proved nothing about Linux.)
- **FR-004** — When the exit code is **null or 0**, the fixture MUST behave
  exactly as it does today: no exception, no extra wait, no change to timing on
  the healthy path.
- **FR-005** — The decision in FR-001/FR-004 MUST live in one `internal static`
  pure function so a Docker-free test can hold it, and MUST NOT be a second
  copy of `ExitedNonZero`'s rule.
- **FR-006** — The exception type MUST NOT be `TimeoutException`. Nothing timed
  out; the fixture learned an answer. It must also not be an
  `OperationCanceledException` subtype, or the existing catch at
  `AspireFixture.cs:182` would reclassify it as a startup timeout and the report
  would say "did not start within 8 minutes" about a boot that failed in 40
  seconds.

### Out of scope, each with its reason

- **The eleven `Running` waits.** Real, comparable, and a separate issue — see
  the sweep below.
- **Splitting `AspireFixture.cs`.** 831 lines against ADR-0084's 300, and it
  trips nothing by configuration (`Directory.Build.props` scopes those limits to
  non-test projects). Pre-existing, unchanged in kind, explicitly excluded by
  the brief.
- **Making `migrations` provokable from a test.** That needs a failure switch in
  production `MigrationRunner` code. ADR-0036 forbids a config knob for a need
  that does not exist outside the test, and a production migration runner with a
  "die now" flag is a worse artefact than an unautomated tier-2 check.
- **Explaining CI's empty logs.** #2061's "not covered" item 1, still open,
  still not this.

---

## Scope ruling 1 — throw immediately, not "time out at the right place"

The issue offers both and leans toward the throw. **The measurement above makes
it not a preference but the only option that delivers the issue's stated
value.**

| | What it buys | What it costs |
|---|---|---|
| **Let the wait time out** | A better exception *site* and message. The reader sees `migrations` named in the predicate failure instead of `camera-catalog`'s timeout. | **Zero seconds.** One shared `cts` means the throw lands at the same instant as today. And #2061 already prints a `Likely cause:` line naming `migrations`, so even the diagnostic gain is small. |
| **Throw immediately** | The whole saving: 8 min minus time-to-death, per occurrence, per developer, per CI job. Plus the same naming. | One decision function, one call site, one new exception. A behaviour change that must not fire on a healthy boot — the risk FR-004 and the tier-2 healthy-path run exist to hold. |

**Ruling: throw immediately.** The other option is what the code already
effectively does, dressed differently.

**A second ruling inside it — how much to put in the message.** The fixture
could reuse the entire timeout report (`CaptureResourceStateMapAsync` +
`CaptureFailedResourceLogsAsync` + `FormatLikelyCause`). It should **not**. At
the instant migrations dies, the nine dependents are `Waiting`, not
`FailedToStart` — #2061's empty-case run shows exactly that shape — so the full
report would print nine `(Waiting)` sections that have nothing to do with the
failure. That is the noise #2061 removed. **The message is scoped to
`migrations`**: the cause sentence, the exit code, and that one resource's log.

---

## Scope ruling 2 — the `.WaitFor` sweep

The issue says only this wait was examined. All of them were, here.

| Call site | Shape | Comparable gap? |
|---|---|---|
| `:125` `migrations` → `Finished` | one-shot | **Yes — this issue.** `Finished` is reached by success *and* by death. |
| `:121,129,133,137,141,145,149,153,157,161,169` → `Running` (11 waits: keycloak, camera-catalog, mediamtx, stream-distribution, layout-composition, overlay-designer, audit-observability, event-ingestion, system-variables, automation, identity) | long-running | **Yes, and a different shape.** A service that dies during startup lands in `Finished`/`FailedToStart`, so a `Running` wait never matches and the shared 8-minute budget is burned. That is literally #1918 (`automation` exiting during startup). |
| `WaitForKeycloakRealmAsync` / `WaitForMediaMtxAsync` / `WaitForServiceHealthAsync` | HTTP poll, 60 × 1 s | **No.** Each is bounded independently of the shared budget and already throws a named `TimeoutException` inside ~60 s. |
| `AppHost.cs:396` `dependent.WaitForCompletion(migrations)` | Aspire's own | **No.** `WaitForCompletion` already checks the expected exit code — it is *why* the nine went to `FailedToStart`. The gap was only on the fixture side. |

**Ruling: the eleven `Running` waits are a separate issue, not this one.**
Three reasons, in order of weight:

1. **Different mechanism.** They need `WaitForResourceAsync(name, [Running, Finished, FailedToStart])`
   — the *states* overload, which returns which state was reached — and then a
   decision per resource. Not the predicate/return-value shape used here.
2. **Eleven healthy-path regression surfaces instead of one.** Every one of them
   would need its own evidence that a normal boot is unaffected. That is a
   different-sized change with a different risk profile.
3. **ADR-0036 and ADR-0144.** Smallest change; smallest independently-shippable
   slice. Fixing `migrations` alone fixes the confirmed occurrence
   (run 33623647778) end to end, because in that run everything else failed
   *because of* migrations.

Recommended follow-up issue, for the orchestrator to file (phases 1–3 do not
file issues): **"The eleven `Running` waits burn the whole budget when a service
dies during startup"** — same family as #1918, #2061, #2064; the third
recurrence the issue predicted, found on purpose this time.

---

## Locked technology choices

| Concern | Choice | Authority |
|---|---|---|
| Integration test harness | Aspire fixture, real stack, no Testcontainers | ADR-0103 |
| Fixture composition | `DistributedApplicationTestingBuilder` against `AppHost` | ADR-0024, ADR-0068 |
| Wait API | `ResourceNotificationService.WaitForResourceAsync(name, predicate, ct)` — Aspire.Hosting 13.5.3 | verified above |
| Test framework / assertions | xUnit 2.9.3 + Shouldly | ADR-0052 |
| Test naming | sentence-style with underscores | ADR-0053 |
| Guards | `Ensure.That(...)` where an argument guard is needed | ADR-0105 |
| Collections | explicit type + collection expression | CLAUDE.md house rule |
| Commits | Conventional Commits, no `Co-Authored-By` | ADR-0030, ADR-0086 |
| Red-first | new behaviour observed failing, failure quoted in the PR | ADR-0139, ADR-0144 §4a |

Nothing here is a new choice. Every row is an existing decision this change
obeys — which is also the answer to declaration 2 in `plan.md`.

## Latency budget impact (constitution §IV)

**`N/A — test-harness startup diagnostic.`** It executes only inside
`AspireFixture.InitializeAsync`, only when the migration runner has exited
non-zero, and touches no constitution §IV leg. It ships no runtime code: no
`src/` file changes (the scratch line of tier 2 is reverted before commit).
Same ruling as #2061, for the same reason.

## Assumptions

1. **`migrations` remains a project resource.** Finding C is specific to
   `ToSnapshot(Executable, …)`. If `migrations` ever became a container, the
   `-1 → null` mapping would apply and the rule would need re-reading. Flagged,
   not guarded — a guard here would be exactly the "reads the design artefact"
   test this repo has learned not to write.
2. **The scratch-line provocation still works.** Observed on 2026-09-04 by
   #2061's phase 5, on this same Aspire build. If it has stopped working, tier 2
   cannot run and the change must block rather than ship on tier 1 alone.

## Guesses marked

1. **That DCP publishes the exit code in the same status update as
   `State = Finished`.** Finding C proves Aspire never *separates* them; it does
   not prove the DCP controller (a Go binary, not decompilable here) never
   publishes `Finished` a beat before the code lands. If it did, the fix would
   be a silent no-op — and **tier 2's pre/post run is precisely the experiment
   that detects it**: a post-fix run that still takes 8 minutes says the exit
   code lagged. `plan.md` §Risks names the contingency so the engineer does not
   invent one at 4b.
2. **That the saving is "minutes".** Bounded above by 8 minutes from CI's own
   log; the actual figure is unmeasured until tier 2 runs. Stated as a bound
   everywhere in this spec, never as a measurement.
