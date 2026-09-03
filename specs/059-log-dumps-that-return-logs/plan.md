# Implementation Plan: A log dump returns logs

**Spec**: `specs/059-log-dumps-that-return-logs/spec.md`

**Issues**: #2053, **#2054 (scope grown 2026-09-03)**

**Branch**: `fix/2053-log-dumps-that-return-logs`

**What changed in this plan on 2026-09-03.** Design decisions 1 and 2 are
unchanged and already implemented (`1217c32`, `5ff65d0`). Decisions **3** (how
the DCP id is resolved, and the restart wrinkle) and **4** (what the red test is,
and why it cannot be a guard) are new, and the file table, risks and cost
sections are updated to match. Nothing above decision 3 was rewritten to look
prescient.

## Bounded context and layers

**None.** This change touches no bounded context, no domain, no application
layer, no API. Both files live under `tests/`:

| File | Role | New? |
|---|---|---|
| `tests/Architecture.Tests/LogTailCoverageTests.cs` | The guard | new — **done, `1217c32`** |
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | The four names | edit — **done, `5ff65d0`** |
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | The DCP id resolution at `:411` and `:476` | edit, grown scope |
| `tests/Integration.Tests/Fixtures/LogTailDeliversIntegrationTests.cs` | The runtime red test | new, grown scope |

There are no entities, no value objects, no invariants to state, no domain
events and no integration events. The DDD sections a plan normally carries are
genuinely empty here, and this plan says so rather than inventing them.

**Boundary rules are untouched.** `Architecture.Tests` gains no project
reference — see the design decision below — so `BoundaryTests` and the
no-cross-context rule (ADR-0027) are unaffected.

## Design decision 1 — how the guard reads the two facts it compares

The guard needs two things: the set of literals passed to `RecentLogs`, and the
contents of `TailedResources`.

**Chosen: read both from source on disk.** `TailedResources` is `private
static`, so reflection cannot see it without changing its accessibility, and the
call-site literals leave a usable trace in IL only by accident. Reading source
is the established answer here and is documented as such in three existing
guards:

- `HandlerDeconstructionTests` — *"Reads source rather than metadata, because a
  deconstruction is syntax and leaves no trace in the assembly."*
- `StaleCodeConventionTests` — reads source because an `ApiError` code is a
  constructor argument.
- `GuardBanWiringTests` — reads repository **files** (`build/guards/BannedSymbols.txt`,
  project files) because *"the thing under test is build configuration, which
  leaves no trace in IL."*

**Rejected: make `TailedResources` `internal` and add a project reference from
`Architecture.Tests` to `Integration.Tests`.** It would drag the Aspire hosting
and DCP dependency graph into a test project that today runs in seconds with no
Docker, and it would put a fixture's internals on another assembly's surface to
serve a lint. Cost is real, benefit is a regex saved.

**Rejected: a Roslyn syntax-tree walk.** Correct, and heavier than the problem —
no existing guard does it, and adding the analyzer packages to
`Architecture.Tests` for one lint is speculative generality (ADR-0036).

### Guard mechanics

- Locate the repository root by walking up from `AppContext.BaseDirectory` to
  the directory containing `SmartSentinelEye.slnx` — the exact helper shape used
  by `HandlerDeconstructionTests.ReadSources()` and `GuardBanWiringTests`.
- Scan `tests/Integration.Tests/**/*.cs`, excluding `obj/` and `bin/` segments,
  matching `RecentLogs(` followed by a string literal. Non-literal arguments are
  skipped, not failed (spec AS-5).
- Parse `TailedResources` from `AspireFixture.cs` by matching the array literal
  on its declaration. **If the parse yields an empty set the guard must fail**,
  for the same reason as the next bullet.
- **Carry a counter and assert it is non-zero** (spec AS-4). A source-scanning
  guard that matches nothing passes, and a passing guard that checks nothing is
  the failure mode this repo has recorded more than once — including the
  standing note that a guard reading a design artefact proves the design was
  written down, not that it holds. Here the guard reads the real call sites, so
  what it proves is real; the counter is what stops it proving nothing.
- **Normalise path separators before reporting.** `Path.GetRelativePath` returns
  the platform separator, so a backslash literal in an expected string is green
  on Windows and red on Linux CI. Report with `/`.
- Failure message lists each violation as
  `<relative path>:<line> asks for '<resource>'`, grouped by resource, and ends
  by naming `AspireFixture.TailedResources` as the place to fix it — mirroring
  the placeholder's own wording, so the two say the same thing.

### What the guard does not prove

That a name in `TailedResources` is a real Aspire resource. A typo would
subscribe to nothing and `RecentLogs` would answer *"(tail subscribed but the
resource emitted nothing)"* — a different message, and one that already exists.
Checking names against `AppHost.cs` is a second guard for a problem nobody has
had; out of scope (ADR-0036, no speculative generality).

## Design decision 2 — the tailing cost, 4 tails to 8

The issue asks for this to be decided rather than assumed.

**What one extra tail actually costs**, read from `AspireFixture.cs:437-489`:

1. One `Task.Run` whose body is `await foreach` over
   `ResourceLoggerService.WatchAsync(resource)`. It is an idle async state
   machine parked on an await, not a thread and not a poll — the only
   `Task.Delay` on the path (250 ms, `:476`) runs when a watched process has
   *exited*, i.e. between lives, not steadily.
2. One `ConcurrentQueue<string>` trimmed to 400 lines (`:466`). At a generous
   200 bytes per line that is ~80 KB per resource; four more is ~320 KB against a
   fixture that boots Postgres, Keycloak, RabbitMQ, MediaMTX, mosquitto, MinIO
   and ten .NET services.
3. Per-line work proportional to **log volume**, not to tail count — an enqueue
   and a bounded trim.

The log lines are produced whether or not anyone watches; `WatchAsync` is a
subscription over output the host already has. That is assumption **A1** in the
spec, reasoned from the code and **not yet measured**, which is why the spec's
test procedure step 4 measures fixture bring-up on both sides.

**Measured at phase 5, and it said nothing** — 8 tails ran 137.87/166.60/154.29/135.95 s
against 4 tails at 140.26/143.00 s, a spread wider than any effect. The figures
are in the spec under A1. They also measured the wrong thing: with every tail on
an empty stream, this compared eight *idle* loops with four. **The reading that
matters can only be taken after the id fix**, when eight tails are actually
carrying lines — which is the one direction in which the cost could turn out to
be real. That does not change the recommendation; it does mean nobody should
cite these numbers as clearance.

**Recommendation: add the four names. 8 tails.**

Alternatives, and why not:

- **Tail everything (~12 resources).** Removes the possibility of a mismatch
  outright and would make the guard unnecessary. Rejected: it tails resources no
  test asks about, including `migrations` (which finishes) and containers whose
  output is voluminous and irrelevant, and it throws away the information the
  list currently carries — *which services a test has ever needed to explain
  itself*. Paying more to learn less.
- **Tail on demand — subscribe inside `RecentLogs`.** Attractive on cost and
  wrong on mechanics: `RecentLogs` is synchronous and called from 20 sites, and
  a subscription created at failure time has nothing behind it unless Aspire
  replays a backlog, which is internal behaviour this change would be betting
  on. That bet, if taken, is a design change worth its own issue — not a
  by-product of fixing four missing strings.
- **Delete the seven call sites.** The issue rejects this and is right.

If step 4 shows a bring-up regression that matters, the fallback is not to
shrink the list but to make the guard the thing that keeps it honest — a name
earns its tail by having a call site, which is exactly what the guard enforces.

## Design decision 3 — resolving the DCP resource id (grown scope, #2054)

`ResourceLoggerService.WatchAsync` keys on the DCP resource id
(`camera-catalog-thwaubpm`), not the app-model name. Both capture paths pass the
name, get an empty stream, and no exception. The fix is to resolve the id and
watch by that.

**Chosen: `ResourceNotificationService.TryGetCurrentState(name, out ResourceEvent evt)`,
taking `evt.ResourceId`.** It is synchronous, allocation-cheap, needs no watch
loop, and its published contract says a display name resolves as long as it is
unique — which every name in `TailedResources` is (no replicas anywhere in
`AppHost.cs`). `_app.ResourceNotifications` is already the fixture's vocabulary:
eleven `WaitForResourceAsync` calls and two `WatchAsync` scans.

**Rejected: scanning `ResourceNotifications.WatchAsync(ct)` for a matching
`evt.Resource.Name`,** the shape used at `:262` and `:299`. It works — it is how
the phase-5 probe read the ids — but it costs a bounded watch per resolution, and
the resolution happens on every turn of the re-subscribe loop. Keep it as the
fallback if `TryGetCurrentState` turns out not to see a resource the fixture can
otherwise wait on (assumption **A2** is reasoned from the doc, not exercised).

**Rejected: `WatchAsync(IResource)`.** The overload exists and looks like the
typed answer, but it is the same lookup with the resource's name — the phase-5
probe found no path from a name to logs. Not tried, not relied on.

**Rejected: caching the id at fixture start.** This is the bug the next
paragraph exists to prevent.

### The wrinkle: the id changes on every restart, not just every boot

`TailResourceLogsAsync` re-subscribes in a loop, added for #2038 because a watch
ends when its process does. **The id must be re-resolved inside that loop, on
every turn** — not captured once before it:

```
while not cancelled:
    id = resolve(name)              # ← inside the loop, every turn
    if id is null: delay, continue  # ← not yet known, or between lives
    await foreach batch in WatchAsync(id): enqueue
    delay 250 ms                    # ← the process went away
```

Resolve-once is the failure worth naming explicitly, because it *looks* correct
and passes AS-6 and AS-7: the first subscription works, the boot-time id is
right, and the tail delivers. It breaks only after a restart — the new instance
carries a new id, the loop re-subscribes to the dead one, the stream completes
immediately, and the resource goes **permanently quiet**. That is #2038's exact
symptom, reintroduced by the fix for #2054, in the one scenario the #2038
diagnostic was built to explain. **AS-8 exists solely to make resolve-once fail.**

Two consequences for the loop's shape:

- **The tails start before the ids exist.** `_logTailTasks` are launched right
  after `StartAsync` (`AspireFixture.cs:116`), before the eleven
  `WaitForResourceAsync` calls; a resource may not have published a snapshot
  yet, so `TryGetCurrentState` returns `false`. That is a *wait*, not a failure —
  delay and retry, and do not record it in `_logTailFailures` (a queue that is
  merely still filling must not be reported as broken).
- **A stale id resolves successfully.** Just after a restart, the notification
  service may still hand back the id of the instance that just died; watching it
  completes immediately and the loop spins. The existing 250 ms delay already
  bounds that, and the next turn re-resolves. No new machinery — but the delay
  must stay on the path that re-resolves, not be short-circuited.

### What this does not change

`RecentLogs`' signature, its three placeholder strings, the 400-line bound, the
120-line default, and `_logTailFailures`. The keys of `_logTails` stay the
**app-model names** — callers ask by name and must keep doing so. The id is an
internal detail of the subscription, and letting it leak into the dictionary
would put a per-boot random suffix into 20 call sites.

### One resolver, two call sites

`:411` (`CaptureOneResourceLogAsync`, the startup-timeout report) and `:476`
(`TailResourceLogsAsync`, `RecentLogs`) must both go through **the same private
resolver**. This is the only reason the spec can put `:411` out of scope for
runtime observation without pretending it is covered: a startup timeout cannot be
provoked in a test, so `:411`'s correctness rests on sharing code that *is*
observed. Two copies of the same three lines would make that claim false.

## Design decision 4 — what the red test is, and where it lives

**The red test must be a runtime observation.** `LogTailCoverageTests` is green
today while every tail is empty; it is a true statement about source that says
nothing about delivery. No source-scanning guard can be the evidence here, and
none is proposed.

### Which project — `tests/Integration.Tests`, and the boot is why

The test needs a booted fixture (~140 s). It belongs in `Integration.Tests`
**precisely because that boot is already being paid**: `AspireFixture` is an
xUnit collection fixture booted **once per assembly**, so joining
`[Collection(AspireCollection.Name)]` adds a few seconds of HTTP and polling to a
run that is happening anyway. Any other home — a new project, a separate
collection — buys a **second** 140 s boot to observe the same mechanism. That is
the whole argument, and it is a cost argument rather than a taxonomy one.

File: `tests/Integration.Tests/Fixtures/LogTailDeliversIntegrationTests.cs`, next
to `AspireFixtureReportSelectionTests.cs`, which establishes that tests *about
the fixture* live in `Fixtures/`. That sibling is a pure-logic test with no boot;
this one is its runtime counterpart, and the contrast is the point.

### Is one resource enough? No — three tests, each covering what the others cannot

| Test | Drives | Proves | Cost |
|---|---|---|---|
| **A. delivery** | `POST /cameras` with an invented name on `camera-catalog`, poll `RecentLogs` up to 30 s | the tail carries a line **this test caused**, from **this resource**, **now** | one HTTP call |
| **B. breadth** | nothing | none of the eight names returns a placeholder | one dictionary read each |
| **C. restart** | restart `event-ingestion`, then `POST /webhook-integrations` with an invented name | the id is **re-resolved**, not captured | one restart |

**Why one is not enough.** A alone proves the mechanism for one resource; the id
is resolved per name, so a name whose snapshot never appears under that spelling
would still be silent and A would not notice — B costs nothing and covers it. And
neither A nor B can fail against a resolve-once implementation, which is the most
likely wrong way to write this fix — C is the only thing that separates them.

**Why not more than three.** Driving a request through all eight would multiply
A's cost by eight to re-prove one mechanism; B already covers breadth at the
level breadth needs.

### What "contains real log content" means, concretely

Test A registers a camera under a name it generates (`Guid.CreateVersion7()`),
which makes `camera-catalog` write, at `Information`:

```
Registered camera {CameraIdentifier} with name {CameraName}.
```

The assertion is `tail.ShouldContain(name)` — the invented name, as a substring,
so it holds whichever way the `CameraName` value object renders. Plus three
negative assertions naming each placeholder explicitly:

- `(not tailed — add '<name>' to AspireFixture.TailedResources)` — not in the array
- `(tail subscribed but the resource emitted nothing)` — subscribed, no delivery
- `(log tail failed: <reason>)` — the tail threw; the reason is in the string, so
  assert it with the value visible in the failure message

**A `ShouldNotBeNullOrEmpty` assertion passes on all three.** That is the trap
this decision exists to close, and it is why the positive assertion — a token the
test invented seconds ago — is the load-bearing one and the negatives are there
to make a failure legible.

Three mechanics the test must get right:

- **Poll, do not assert once.** DCP sits between the service's stdout and the
  fixture's queue (spec A3). Re-read `RecentLogs` on a short interval to a 30 s
  timeout; report the last value read on timeout, so the failure says *which*
  placeholder it got.
- **`Microsoft.AspNetCore` is pinned to `Warning`** in `appsettings.json`, so
  ASP.NET's own request lines are never written. The marker has to come from
  application code — hence a registration, not a `GET`.
- **Do not assert on startup lines.** They are the tempting marker (the phase-5
  probe read 60 of them immediately), but the tail keeps only the last 400 lines
  and `RecentLogs` returns the last 120; a chatty service evicts its own startup
  by the time a late test runs. A line the test just caused cannot be evicted.

### Test C, and what it costs

C restarts a live resource inside a shared fixture — the most destructive thing
in the suite. It picks `event-ingestion` because that is **already** what
`RestartLosesNothingIntegrationTests` and `OutboxSurvivesAKillTests` restart, so
the hardened `RestartAsync` shape (restart, then `StartCommand` in a `finally`,
then wait for healthy — written after a failed restart on CI turned one test's
problem into eleven failures) can be reused rather than invented. Restarting
`camera-catalog` would reuse test A's driver instead, but would introduce the
first-ever restart of the resource most of the suite depends on. Not worth it.

`event-ingestion` writes `Registered webhook integration '{Name}' ({Identifier}).`
at `Information`, and `POST /webhook-integrations` is already exercised by four
test classes, so the marker and the client helper both exist.

**If C destabilises the suite, C is the one to drop** — and dropping it leaves
the restart wrinkle unobserved, which must then be said in the verification note
rather than quietly absorbed.

## Messaging

None. No domain event, no integration event, no `Shared.Contracts` change.

## ADRs referenced

- **ADR-0103** — integration tests are Aspire-only; the fixture is the harness
  being repaired.
- **ADR-0139** — rules that fail the build, not the review; the guard is an
  instance, and the red-first obligation the guard satisfies.
- **ADR-0144** — the autonomous lane; phase 4a colour and the two-agent split.
- **ADR-0052 / 0053** — xUnit + Shouldly, sentence-style test names.
- **ADR-0036** — smallest change; no speculative generality.
- **ADR-0083** — *stale*. See `spec.md`, "One record that is stale". Not
  reconciled here; recommend a separate issue.

## No new ADR is required — re-confirmed for the grown scope

Both original halves implement decisions already made: the tail list is
testing-infrastructure detail inside ADR-0103's choice of harness, and a
build-failing convention guard is ADR-0139's general answer, already instantiated
20 times in `tests/Architecture.Tests`.

**The grown scope does not change that, and the reason is not merely "it is only
test code".** An ADR records a decision about how the system is built that a
later reader would otherwise have to reverse-engineer. Here:

- **Watching by DCP id is not a choice**, it is the API's contract. Watching by
  name never worked; there is no alternative being rejected in favour of it, so
  there is nothing to record.
- **Where the resolution lives** is inside the fixture ADR-0103 already
  mandates, and changes nothing about a bounded context, a boundary, a
  dependency or a deployment.
- **"The red test must be a runtime observation, not a source scan"** is the one
  candidate worth pausing on, because it *is* a general lesson and it recurs.
  But it is not a new decision either: constitution §Testing and ADR-0139
  already require new behaviour to be observed red, and this scope is an
  instance of them, not an amendment. What it *is* is the sharpest live example
  yet of a guard that reads the design artefact — which belongs in the PR body
  and in this plan, where it now is.

**One thing does deserve a separate issue, and it is not an ADR:** the diagnostic
has been broken since it was written, and nothing failed. Every mechanism that
exists only to explain failures has that property. Whether the repo wants a
standing "the diagnostic must be exercised" convention is a real question, and it
is bigger than this branch. Raise it as an issue; do not settle it here.

**Still outstanding from the original scope:** ADR-0083 is stale (see `spec.md`).
Unchanged by the grown scope; still deserves its own issue.

## Risks

| Risk | Mitigation |
|---|---|
| Regex misses a call-site form and the guard passes vacuously | Non-zero counter (AS-4); the guard's first run must be **red** with all seven sites listed, which is itself proof the scan works |
| Backslash path literals pass on Windows, fail on Linux CI | Normalise separators to `/` before comparing or reporting |
| Four more tails slow fixture bring-up | Measured at phase 5, both sides, second run of each — **no signal; and the reading was taken on empty tails, so it must be repeated after the id fix** |
| `TailedResources` gets reformatted and the parse breaks | Parse breakage yields an empty set, which the guard treats as a failure rather than a pass |

### Risks added with the grown scope

| Risk | Mitigation |
|---|---|
| The id is resolved once and reused — passes A and B, breaks after any restart, resource goes permanently quiet | **Test C** (AS-8) exists for this and nothing else; the loop shape in decision 3 puts the resolve *inside* the loop |
| `TryGetCurrentState` does not see a resource the fixture can otherwise wait on (A2 is reasoned, not exercised) | Fallback is the `ResourceNotifications.WatchAsync` scan already used at `:262`/`:299`; **test B fails loudly per resource** rather than one resource going quiet unnoticed |
| Tails start before any snapshot exists, so the first resolve returns nothing | Treat as *wait*, not failure: delay and retry, and **do not** write `_logTailFailures` — a filling queue reported as broken is the same class of misleading diagnostic this whole spec is about |
| A stale id resolves right after a restart and the watch completes instantly | The existing 250 ms delay bounds the spin; the next turn re-resolves. Keep the delay on the re-resolving path |
| The delivery assertion is written as "non-empty" and passes on a placeholder | Decision 4 fixes the assertion as a **positive** check for a token the test invented; the three negatives are named individually |
| Test A asserts before DCP has forwarded the line, and flakes as "the fix does not work" | Poll to a 30 s timeout (spec A3) and report the last value read |
| Test C's restart destabilises the shared fixture | Reuse the hardened `RestartAsync` shape from `RestartLosesNothingIntegrationTests`, on the resource that shape was written for; C is the first thing to drop, and dropping it is recorded, not absorbed |
| `:411` is fixed but never observed | Both call sites share one resolver, so the observed path exercises the code the unobserved one depends on — stated as the reason, and its residue is in the spec's out-of-scope list |
