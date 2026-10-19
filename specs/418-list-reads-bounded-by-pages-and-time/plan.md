# Plan 418: MediaMTX list reads stop at twenty pages and five seconds, and a configured-paths page without an items array warns

**Spec**: [spec.md](./spec.md). **Issue**: #3021 items 2-4. **Context**: StreamDistribution,
Infrastructure layer only.

## 1. Constitution and ADR check

| Rule | How this plan meets it |
|---|---|
| Lane implements, does not decide (ADR-0144) | 20 pages and 5 s are the owner's (issue comment 2026-10-19T19:02Z); the warning throttle is spec 409's, unchanged. No ADR, no constitution edit. |
| Red first (ADR-0139) | Three red/green pairs, tasks.md. |
| No cross-context refs | All edits inside `src/StreamDistribution/Infrastructure`. No `Shared.Contracts` change, no message change. |
| §II primitives | Infrastructure gateway, not a domain model; `PrimitiveBoundaryTests` scope unaffected. |
| ADR-0050 logging | Three new `[LoggerMessage]` events in `StreamDistribution/Infrastructure/Log.cs`, no remote text. |
| ADR-0049 | `CancellationToken` still last parameter; caller cancellation passes through. |
| ADR-0105 | No new public argument; no guard needed. |
| ADR-0084 | New code in `MediaMtxRtspGateway.PageReading.cs` (221 LOC today); `MediaMtxRtspGateway.cs` (320) gets only the constant change and the constructor. |
| ADR-0143 | Retry policy untouched. The deadline bounds retries in time; it does not change what is retried. |
| §IV latency | N/A (control plane). |

## 2. Design

### 2.1 Cap (US1)

`MediaMtxRtspGateway.cs:21`: `private const int MaxListPages = 20;`. Nothing else. All six existing
uses (`ReadPagesAsync`'s loop and cap return, `CompleteHealthOrThrow`, `ListConfiguredPathsAsync`,
`WarnOnUnfinishedRead` x2) follow.

### 2.2 Deadline (US2)

Constructor (in `MediaMtxRtspGateway.cs`):

- new field `private readonly TimeProvider timeProvider;`
- the `[ActivatorUtilitiesConstructor]` moves to a four-argument constructor
  `(HttpClient http, ILogger<MediaMtxRtspGateway> logger, MediaMtxConfiguredSourcesReadWarnings readWarnings, TimeProvider timeProvider)`;
- the three-argument constructor chains to it with `TimeProvider.System`; the two-argument one
  keeps chaining to the three-argument one. Four parameters is at the ADR-0084 limit, not over.
- DI already provides `TimeProvider` (`AddMediaMtxGateway` → `services.TryAddSingleton(TimeProvider.System)`,
  `StreamDistributionInfrastructureModule.cs:106`). No registration change.

`ReadPagesAsync` (in `PageReading.cs`), shape:

```csharp
private static readonly TimeSpan ListReadDeadline = TimeSpan.FromSeconds(5);

using var deadline = new CancellationTokenSource(ListReadDeadline, timeProvider);
using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
var page = 0;
try
{
    for (; page < MaxListPages; page++) { ... ReadPageAsync(listPath, page, linked.Token) ... }
}
catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
{
    throw new HttpRequestException(HttpRequestError.InvalidResponse, $"MediaMTX list {listPath} did not finish within {seconds} s; page {page} was being read.");
}
```

Notes for the engineer (constraints, not code to copy):

- The exception filter checks the **caller's** token first (FR-304).
- Only `OperationCanceledException` (incl. `TaskCanceledException`) is mapped. Every other
  exception, including the existing `InvalidPage` / `EnsureSuccess` / `EnsureObjectItem` ones,
  passes through unchanged.
- The `return` paths inside the loop are unaffected; the loop's existing structure stays. The page
  counter used in the message is the page being requested when the deadline fired. Format numbers
  with `CultureInfo.InvariantCulture`.
- No `InnerException` (spec A1). Keep the existing `ReadPageAsync` signature; pass it the linked
  token.
- The `FoldPage` path is synchronous and never observes the token; that is fine.

### 2.3 Items-unreadable warning (US3)

- `PageReadEnd.cs`: add `ItemsUnreadable` (append; do not reorder).
- `ReadPagesAsync`: when `FoldPage` returns `false`, classify the stop:
  `HasItemsArray(payload) ? Complete : ItemsUnreadable`, returning `new PageRead(end, page)`.
  `HasItemsArray` is a new `private static bool` (`payload.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array`).
  This is the one place the rule lives; the folds are not edited.
- **Health is unchanged by construction**: `GetAllPathsHealthAsync`'s closure records
  `itemsUnreadablePage` and returns `false`, so `ReadPagesAsync` now returns `ItemsUnreadable`
  for it, but `GetAllPathsHealthAsync` throws on `itemsUnreadablePage >= 0` *before*
  `CompleteHealthOrThrow` sees `read.End`. Do not edit `GetAllPathsHealthAsync` or
  `CompleteHealthOrThrow`; its `_ => UnreachableException` arm stays unreachable (the existing
  health characterisation tests prove the message is unchanged).
- `ListConfiguredPathsAsync`: add `case PageReadEnd.ItemsUnreadable: logger.MediaMtxConfiguredPathsItemsUnreadable(read.Page);`.
- `WarnOnUnfinishedRead`: add **explicit** arms `ItemsUnreadable when warn` → Warning and
  `ItemsUnreadable` → Debug, **before** the `default` arm. The `default` arm currently logs
  `PageCapStillReached`; falling into it would log the wrong event. Consider making the cap's Debug
  arm explicit too and `default` an `UnreachableException`, only if it keeps the method under the
  ADR-0084 complexity limit; otherwise leave `default` as is.

### 2.4 New log events (`src/StreamDistribution/Infrastructure/Log.cs`)

Mirror the wording of the existing `PageCountUnreadable` siblings; each takes `int page` only.

| Event | Level | Message |
|---|---|---|
| `MediaMtxConfiguredSourcesItemsUnreadable` | Warning | "MediaMTX config path list page {Page} has no items array; the source repair compares only the paths read so far." |
| `MediaMtxConfiguredSourcesItemsStillUnreadable` | Debug | "MediaMTX config path list still has a page without an items array (page {Page}); the source repair compares only the paths read so far, and the Warning repeats at most every 10 minutes." |
| `MediaMtxConfiguredPathsItemsUnreadable` | Warning | "MediaMTX config path list page {Page} has no items array; the startup reconcile removes orphans only among the paths read so far." |

### 2.5 Throttle decision (item 4, spec 410 §8 Q3)

The existing throttle fits exactly: `MediaMtxConfiguredSourcesReadWarnings` (spec 409) is already the
singleton gate for the source repair's whole-read Warnings, keyed on `PageReadEnd`, with
`WarningRepeatGate`'s 10-minute repeat and clear-on-complete. Adding one `PageReadEnd` value puts the
new Warning under it: a persistently malformed page yields at most 144 Warnings a day (plus Debug
lines), not 8,640. `SourceRepairFailureLog` does **not** fit (it is per path and per repoint failure,
not per read). The startup read needs no throttle (once per process, like its siblings). **No new
volume or number decision is needed.**

## 3. Entities, value objects, messaging

None. No domain change, no domain or integration event, no contract change, no migration.

## 4. Rate and time arithmetic (for the PR body)

| Read | Cadence | Worst GETs before | after |
|---|---|---|---|
| Health | every tick, ≥ 2 s apart | 1,000 / tick | 20 / tick (≤ 600/min) |
| Source repair | 10 s | 1,000 / pass | 20 / pass (≤ 120/min) |
| Startup reconcile | once | 1,000 | 20 |

`api` partition: 2,000 / min. Time: worst read before ≈ 1,000 x 30 s; after 5 s.

## 5. Tests

Owned by test-writer (tasks.md T001-T003). Unit tests only, in
`tests/StreamDistribution.Infrastructure.Tests`; no Integration.Tests shard filter is involved.

- A **new** hand-written timer-capable fake, `Fakes/SchedulingTimeProvider.cs`, a minimal copy of
  `tests/Identity.Infrastructure.Tests/Fakes/ManualTimeProvider.cs`'s timer support (test projects
  do not reference each other). Do **not** add timers to the existing
  `StreamDistribution.Infrastructure.Tests/Fakes/ManualTimeProvider.cs`: its doc says no timers, and
  the existing tests rely on that.
- Deadline tests must compile on unchanged `develop` (ADR-0087: the red commit builds on its own),
  so they obtain a gateway with the fake clock **through the registration**: register the fake as
  `TimeProvider` first, then `AddMediaMtxGateway()` with a stub primary handler, then resolve
  `IRtspGateway`. `MediaMtxProbeLogVolumeTests` already composes `AddMediaMtxGateway`; mirror it.
  Do not add `ConfigureHttpClientDefaults` / the resilience handler, so no retry or attempt timeout
  interferes.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A deadline-triggered cancellation reported as a caller cancel (or the reverse) | Filter order FR-304; existing cancellation tests unmodified + a new race test. |
| `WarnOnUnfinishedRead`'s `default` arm logs the cap Debug for the new end | Explicit arms; T002 asserts the event names. |
| The red deadline tests pass vacuously because the fake timer never fires | T003 includes "4.9 s does not end the read" and the test-writer proves the fake by counterfactual (advance 5 s with the fake is the only trigger). |
| Health read's message changes because `ReadPagesAsync` now classifies its stop | Health code untouched; `MediaMtxRtspGatewayHealthReadCharacterisationTests` and `StreamHealthWatcherCutShortReadTests` malformed-page cases pass unmodified. |
| A stray 1,000 left in a doc comment or test name | T001 renames the "one_thousand" tests and their summaries; T030 greps `1000\|1,000\|thousand` under the gateway tests. |
