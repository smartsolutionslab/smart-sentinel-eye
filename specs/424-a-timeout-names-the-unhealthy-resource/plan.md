# Implementation Plan: A timeout names the unhealthy resource

**Feature**: 424 | **Issue**: #2146 | **Spec**: `./spec.md`
**Branch**: `test/2146-fixture-timeout-snapshot` (worktree `D:/Github/sse-2146`,
cut from `origin/develop` at `7909dd20`)

---

## Summary

Extend the startup-timeout report that `AspireFixture.InitializeAsync`'s existing
`catch` already throws: capture each resource's health alongside its state and
exit code, print it in the state list (bounded), and add a third arm to the
`Likely cause:` line that names a resource that is unhealthy when the timeout
fires. No watchdog, no new wait, no ADR (owner decision, spec §1).

## Technical context

- **Where**: `tests/Integration.Tests/Fixtures/AspireFixture.cs` (one partial of
  `AspireFixture`) and its Docker-free test class
  `tests/Integration.Tests/Fixtures/AspireFixtureReportSelectionTests.cs`.
  US3 adds one new test class and its shard-filter entry.
- **Stack**: .NET 10, xUnit + Shouldly (ADR-0052), Aspire.Hosting.Testing
  13.6.1. `Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus` and
  `Aspire.Hosting.ApplicationModel.HealthReportSnapshot` are already transitively
  available to the test project.
- **Bounded context / layers**: none. Test infrastructure; no `src/` file is
  touched. No domain entity, value object, integration event or
  `Shared.Contracts` change, so no boundary rule is in play.
- **Constitution**: §Testing (new behaviour starts red — see tasks.md
  declarations), §IV N/A, §II N/A (not a domain model).

## Design

### 1. The captured datum

A test-project-local record, beside the fixture:

```csharp
internal sealed record ResourceHealth(HealthStatus? Status, ImmutableArray<HealthReportSnapshot> Reports);
```

Captured into a third per-resource dictionary, `healthByResource`, written in
`CaptureResourceStateMapAsync` from the same `WatchAsync` event that already
writes `states[...]` and `exitCodes[...]` — last event wins, as for the exit
code. Recorded only when `evt.Snapshot.HealthStatus` is non-null (Aspire makes it
null off `Running`, spec §2d), so a non-running resource has no entry and renders
exactly as today (FR-004).

Why a third dictionary rather than reshaping `states`/`exitCodes` into one
snapshot map: reshaping would rewrite the signatures 23 existing tests call,
turning a behaviour-changing slice into a refactor-plus-change (ADR-0144: two
issues). The existing two-dictionary shape is kept and extended.

### 2. Signatures — existing call sites unmodified

`FormatResourceStates`, `FormatLikelyCause` and `FormatTimeoutMessage` each gain
a trailing optional parameter
`IReadOnlyDictionary<string, ResourceHealth>? health = null`; `null` means "no
health captured" and reproduces today's output exactly. This is what lets every
existing `FixtureLogic` test compile and pass **unmodified** (FR-004, SC-002).
Nullable is acceptable here: test infrastructure, not Domain/Application
(ADR-0141's scope). S107 is `NoWarn` for test projects (`Directory.Build.props`).

The single production call site in the `catch` passes `healthByResource`.

### 3. Rendering (FR-001..FR-003)

- Parenthetical facts joined with `", "`: `(exit code N)`, `(health H)`,
  `(exit code N, health H)`; none → no parenthetical.
- Report lines, indented four spaces, only for reports whose `Status` is not
  `Healthy`, in Aspire's report order; `null` status renders `pending`.
- Text = `Description` ?? first line of `ExceptionText` ?? empty; truncated to
  200 chars with a trailing `…`.
- At most three report lines; then `    (+N more)`.
- Constants `MaxHealthReportLines = 3`, `MaxHealthTextLength = 200` as
  `private const` beside the other formatter fields, each with a one-line *why*.

### 4. The cause line's third arm (FR-005..FR-007)

```
unhealthy = UnhealthyResources(states, exitCodes)        // existing ordering + IsHealthy
              ∪ Running resources (IsHealthy says healthy) // shape 1 lives here
            where (state is RuntimeUnhealthy)
               or (state is Running and any report.Status == Unhealthy)
            minus died, minus ended, minus *-rebuilder
```

Note `IsHealthy` treats `Running` as healthy, so shape 1's `rabbitmq` is **not**
in `UnhealthyResources` — the arm must scan `states` directly for the `Running`
half, ordered `StringComparer.Ordinal` like the others. `IsHealthy` is **not**
changed: it drives `SelectResourcesToReport`, i.e. which log sections are dumped,
and widening it is a separate behaviour (FR-007).

Sentence shape (the implementer owns exact wording, constrained by FR-006):
`"{name} was {state} with health Unhealthy ({report name}) — ..."` /
`"{name} was RuntimeUnhealthy — ..."`, followed by a clause that says an unhealthy
resource holds whatever waits for it, and nothing about death or exit. Joined
after the `died` and `ended` sentences under the one `Likely cause: ` prefix.

**Pending-only reports do not qualify** (spec §2d): Aspire's aggregate reads
`Unhealthy` for a check that has not run, and naming that resource would invent
a failure from a default. The predicate reads report statuses, never the
aggregate.

### 5. Capture extraction (US3, P2)

To make the capture testable without Docker, extract the body of
`CaptureResourceStateMapAsync` into

```csharp
internal static async Task<CapturedResourceStates> CaptureResourceStatesAsync(
    ResourceNotificationService notifications,
    TimeSpan window)
```

returning `(states, exitCodes, health)`. The instance method keeps its name, calls
it with `app.ResourceNotifications` and `TimeSpan.FromSeconds(3)`, and copies the
exit codes and health into the instance fields it fills today. The FixtureLogic
test constructs `new ResourceNotificationService(NullLogger<…>.Instance, fakeLifetime)`,
publishes two resources (`ContainerResource("rabbitmq")`,
`ProjectResource`-free: a second `ContainerResource("camera-catalog")` suffices),
and calls the static function with a short window (≤ 500 ms).

Hand-written `IHostApplicationLifetime` fake (ADR-0052: hand-written fakes are
allowed); it returns `CancellationToken.None` for the three tokens.

**If A2 fails** at phase 4a (the in-process service does not replay), stop US3,
record the limit in the PR, and ship US1/US2 — the formatter tests then carry the
same honest limit spec 084's plan recorded: deleting the capture line leaves them
green.

## The honest limits

1. **No Docker-backed observation is possible on this machine today** (Docker is
   down). Phase 5 cannot provoke a real unhealthy `rabbitmq` and read the real
   message. The evidence is FixtureLogic tests over hand-built snapshots plus,
   with US3, an in-process notification service — not a booted stack.
2. **The shapes are reconstructed, not captured.** Shape 2 is from #2146's
   2026-09-07 comment (prose, no log retained); shape 1 is reasoned from
   `WaitForDependenciesAsync`'s decompiled predicate and has never been observed
   in this repository.
3. **A healthy CI boot never reaches this code** — it runs only on a startup
   timeout. A green full integration run proves only that nothing *else* moved.

## Files

| File | Change |
|---|---|
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | `ResourceHealth` record (or a sibling file), `healthByResource` field, capture line, three formatter signatures, rendering, third cause arm; US3 extraction |
| `tests/Integration.Tests/Fixtures/AspireFixtureReportSelectionTests.cs` | new red facts (US1, US2) |
| `tests/Integration.Tests/Fixtures/AspireFixtureResourceCaptureTests.cs` | new class, `[Trait("Category", "FixtureLogic")]` (US3) |
| `tests/Integration.Tests/ci-shards/shard-N.filter` | entry for the new class (US3) — a missing entry fails deterministically |

No `src/`, AppHost, CI workflow, ADR or constitution change.
