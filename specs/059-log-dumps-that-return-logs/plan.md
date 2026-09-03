# Implementation Plan: A log dump returns logs

**Spec**: `specs/059-log-dumps-that-return-logs/spec.md`

**Issue**: #2053

**Branch**: `fix/2053-log-dumps-that-return-logs`

## Bounded context and layers

**None.** This change touches no bounded context, no domain, no application
layer, no API. Both files live under `tests/`:

| File | Role | New? |
|---|---|---|
| `tests/Architecture.Tests/LogTailCoverageTests.cs` | The guard | new |
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | The four names | edit, one line |

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

## No new ADR is required

Both halves implement decisions already made. The tail list is
testing-infrastructure detail inside ADR-0103's choice of harness, and a
build-failing convention guard is ADR-0139's general answer, already instantiated
20 times in `tests/Architecture.Tests`. Nothing here decides how the system is
built.

## Risks

| Risk | Mitigation |
|---|---|
| Regex misses a call-site form and the guard passes vacuously | Non-zero counter (AS-4); the guard's first run must be **red** with all seven sites listed, which is itself proof the scan works |
| Backslash path literals pass on Windows, fail on Linux CI | Normalise separators to `/` before comparing or reporting |
| Four more tails slow fixture bring-up | Measured at phase 5, both sides, second run of each |
| `TailedResources` gets reformatted and the parse breaks | Parse breakage yields an empty set, which the guard treats as a failure rather than a pass |
