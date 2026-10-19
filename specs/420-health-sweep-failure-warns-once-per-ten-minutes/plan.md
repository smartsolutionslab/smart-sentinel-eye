# Plan 420: the health sweep's failure Warning is throttled per failure kind

**Spec**: [spec.md](./spec.md). **Issue**: #3013 (feature-level; already on Project #13).

## 1. Constitution and ADR check

| Rule | How this plan meets it |
|---|---|
| ADR-0144: the lane implements decisions | The throttle, its rule and its 10 minutes are the owner's (issue comment 2026-10-19) and spec 409's; nothing is chosen here beyond A1-A3 (spec §7), each a copy of an existing shape |
| ADR-0139 / §Testing: new behaviour starts red | Five red cases (tasks T001-T004: T001 is a theory over 503 and 401) plus one guard, T006, green on both sides, pinning the error part of the kind, observed failing on `28bcec96`; existing tests characterise the rest, unmodified |
| §II primitives on a domain model | N/A: Infrastructure only, no domain type touched |
| No cross-context references | N/A: StreamDistribution.Infrastructure only |
| ADR-0050 `[LoggerMessage]` | One new Debug event in `Log.cs` |
| ADR-0084 metrics (advisory) | Constructor at 4 parameters (the limit); `DispatchAsync` grows by one call, the logging moves to a private method as in `MediaMtxSourceRepairSweep.LogFailure` so no method passes 30 LOC |
| ADR-0105 guards | None added; no new trust boundary |
| ADR-0051 DI | No new registration; `TimeProvider` is already a singleton in the composition |
| §IV latency | N/A (spec §6) |
| No speculative generality | No shared failure-kind type, no option, no new gate class (§2.3) |

No ADR is needed.

## 2. Design

### 2.1 Constructor (`src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs`)

The primary constructor gains a fourth parameter, `TimeProvider timeProvider`, and the current
three-parameter signature stays as a secondary constructor chaining to `TimeProvider.System`:

```csharp
public sealed class StreamHealthWatcher(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<StreamHealthWatcher> logger,
    TimeProvider timeProvider) : BackgroundService
{
    public StreamHealthWatcher(IServiceScopeFactory scopeFactory, IClock clock, ILogger<StreamHealthWatcher> logger)
        : this(scopeFactory, clock, logger, TimeProvider.System)
    {
    }
```

The four-parameter constructor carries `[ActivatorUtilitiesConstructor]` (as
`MediaMtxRtspGateway`'s does, `MediaMtxRtspGateway.cs:43`), written either as `[method:
ActivatorUtilitiesConstructor]` on the primary constructor or by making the four-parameter one
explicit; the engineer picks whichever compiles without a suppression. Why both:

- The container (`AddHostedService`, resolved by `ServiceProvider`) takes the longest satisfiable
  constructor; `TimeProvider` is registered (`StreamDistributionInfrastructureModule.cs:106`, `:167`),
  so production gets the four-parameter one and the injected provider.
- The red tests build the watcher with `ActivatorUtilities.CreateInstance<StreamHealthWatcher>`
  over a provider holding a `ManualTimeProvider`. On unchanged code that compiles and picks the only
  constructor, so the facts fail on the Warning count, not on a missing overload. After the change
  the attribute makes the choice explicit.
- The three existing watcher test files (`StreamHealthWatcherAbsenceHoldTests`,
  `...AbsenceHoldInvariantTests`, `StreamHealthWatcherScopeTests`, edited by PR #3065) and
  `StreamHealthWatcherCutShortReadTests` keep calling the three-parameter constructor untouched.

### 2.2 The gate and the kind

```csharp
private readonly WarningRepeatGate<SweepFailure> failures = new(timeProvider);
```

`SweepFailure` is a private `readonly record struct SweepFailure(Type ExceptionType, HttpStatusCode? Status, HttpRequestError Error)`
built from the caught `HttpRequestException`: `(ex.GetType(), ex.StatusCode, ex.HttpRequestError)`.
`Error` is not nullable here because the catch is typed (the source repair's catches `Exception`, so
its copy is). A field initialiser capturing a primary-constructor parameter is the form
`MediaMtxSourceRepairSweep` uses (`:29`).

In `DispatchAsync`:

```csharp
try
{
    observed = await gateway.GetAllPathsHealthAsync(cancellationToken);
}
catch (HttpRequestException ex)
{
    LogSweepFailure(ex);
    return;
}

failures.Clear();
```

`failures.Clear()` sits after the `try`, before `previouslyPresent` is taken, so a successful read
re-arms the Warning whatever the dispatch loop does afterwards (FR-005) and the early `return` keeps
the failed tick's state untouched exactly as today (FR-006).

```csharp
private void LogSweepFailure(HttpRequestException failure)
{
    SweepFailure outcome = new(failure.GetType(), failure.StatusCode, failure.HttpRequestError);

    if (failures.ShouldWarn(outcome))
    {
        logger.HealthSweepFailed(failure);
        return;
    }

    var status = outcome.Status.HasValue
        ? ((int)outcome.Status.Value).ToString(CultureInfo.InvariantCulture)
        : "-";
    logger.HealthSweepStillFailing(outcome.ExceptionType.Name, status);
}
```

The comment above the catch ("The whole tick is lost, not one camera...") stays; one sentence may be
added saying a repeat of the same kind is Debug for 10 minutes (spec 409's rule). The class doc may
gain one sentence to the same effect. No other comment changes.

**Thread-safety.** Ticks run one after another on the `ExecuteAsync` loop, which is why
`degradedSince` and `pathsPresentInLastSweep` are plain fields. The gate is locked anyway
(`WarningRepeatGate.cs:15`); no further synchronisation is added.

### 2.3 Why not share the kind type with `MediaMtxSourceRepairSweep`

Extracting `SweepFailure` and the status formatting into a shared internal type would edit
`MediaMtxSourceRepairSweep`, which FR-008 keeps out ("any other log"), and would make this a mixed
refactor plus behaviour change. The duplication is one record line and four formatting lines. If a
third sweep ever needs it, that is the moment to extract; recorded, not done.

### 2.4 New log event (`src/StreamDistribution/Infrastructure/Log.cs`)

Directly after `HealthSweepFailed` (`:109-110`):

```csharp
[LoggerMessage(Level = LogLevel.Debug, Message = "MediaMTX health sweep still failing ({ExceptionType} {Status}); skipping this tick for every stream. The Warning repeats at most every 10 minutes.")]
public static partial void HealthSweepStillFailing(this ILogger logger, string exceptionType, string status);
```

The red facts match on `"HttpRequestException"`, the status number and the substring
`"repeats at most every 10 minutes"`, the same substring the source-repair facts use
(`MediaMtxSourceRepairSweepTests.cs:99-101`), so the exact wording around them is the engineer's.
`HealthSweepFailed`'s message and level do not change (the existing
`StreamHealthWatcherCutShortReadTests.AssertTickFailed` pins the message verbatim).

## 3. Entities, value objects, messaging

None. No domain or application change, no integration event, no contract, no migration.

## 4. Rate arithmetic (for the PR body)

- Today: one Warning per failed tick. Tick period = read time + 2 s delay: 2 s for an immediate
  refusal (43,200 a day), up to 7 s when each read runs into the 5 s deadline (~12,343 a day).
- After: one Warning per 10 minutes per steady kind, 144 a day; every other failed tick is a Debug
  line (filtered out at the default Information level). A kind change or a successful tick in
  between adds at most one Warning each.

## 5. Tests

New file only: `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherSweepFailureThrottleTests.cs`.
It must not edit or depend on any file PR #3065 touches, so it:

- uses the **real** `MediaMtxRtspGateway` (two-parameter constructor) over its own private
  `HttpMessageHandler` stub that answers a scripted sequence of responses (status + body), the shape
  `StreamHealthWatcherCutShortReadTests` uses. **No `IRtspGateway` double**: the interface loses
  `GetPathHealthAsync` in PR #3065, and a double would have to change with it.
- builds the watcher with `ActivatorUtilities.CreateInstance<StreamHealthWatcher>(provider)` over a
  `ServiceCollection` holding: `IClock` (a private fixed clock), `ILogger<StreamHealthWatcher>` (a
  `CapturingLogger<StreamHealthWatcher>` instance), `TimeProvider` (the existing
  `Fakes/ManualTimeProvider`, unmodified) and the scoped recording `ReportStreamHealthCommand`
  handler (a private copy of the `RecordingHandler` pattern);
- runs ticks by calling the internal `DispatchAsync(streams, gateway, CancellationToken.None)`
  repeatedly on the **same** watcher and gateway, advancing the `ManualTimeProvider` between calls
  where a fact needs it.

The test project is not sharded (only `Integration.Tests` has `ci-shards/*.filter`), so no shard
entry is needed. No integration test: the behaviour is a log-level decision inside one class, fully
reachable through `DispatchAsync` (ADR-0103's reason for that seam).

Characterisation (green before and after, unmodified): `StreamHealthWatcherCutShortReadTests`
(single failed tick: one Warning with the exception, nothing dispatched),
`StreamHealthWatcherAbsenceHoldTests` (a failed tick between does not reset the hold),
`StreamHealthWatcherAbsenceHoldInvariantTests`, `StreamHealthWatcherScopeTests`,
`WarningRepeatGateTests`, `MediaMtxSourceRepairSweep*Tests`,
`StreamDistributionRegistrationCharacterisationTests`.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Two constructors confuse the container | `ServiceProvider` takes the longest satisfiable; `TimeProvider` is registered. `StreamDistributionRegistrationCharacterisationTests` composes the real container and stays green (SC-4); phase 5 boots the stack and reads `StreamHealthWatcherStarted` |
| Kind flicker under #3064 (spec A4) | Recorded for the owner on #3064; out of scope |
| A malformed-page kind switch is not re-warned (spec A1) | Accepted consequence of the decided reuse; the Debug line still names the type every tick |
| Rebase over PR #3065 | Disjoint files (spec P9); the new test uses no `IRtspGateway` double |
