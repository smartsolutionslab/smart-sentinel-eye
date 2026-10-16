# Plan 388: The source repair logs a failing path on change, bounds its list read, and reports its own startup failure

**Spec**: [spec.md](./spec.md). **Issue**: #2994. **Split out**: #3010 (older readers' `pageCount`).
**Prior art**: spec 378 (the repair), spec 208 US2 (`LogThrottleTransition`), spec 373 (origin-only URLs).

## 1. Context and layers

One bounded context, **StreamDistribution**, Infrastructure layer only. No contract, no other
context, no new project reference, no DI or AppHost change (constitution §III; spec FR-012).

| File | Change |
|---|---|
| `src/StreamDistribution/Infrastructure/Reconciler/SourceRepairFailureLog.cs` (new) | Per-path failure state plus the Warning/Debug decision (§3) |
| `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxSourceRepair.cs` | Owns one `SourceRepairFailureLog`. The catch delegates to it. Success, 404 and the end of the pass update it. Adds a `(ILogger, TimeProvider)` constructor and keeps `(ILogger)` |
| `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxSourceRepairSweep.cs` | One `MediaMtxSourceRepair` field, built from the injected `TimeProvider`, reused on every tick |
| `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxReconciler.cs` | `ReconcileOnceAsync`: try/catch around the trailing repair (FR-010) |
| `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` | `ListConfiguredPathSourcesAsync` only: cap, stop-on-empty, `TryGetInt32` (§4). The two older readers are **byte-identical** |
| `src/StreamDistribution/Infrastructure/Log.cs` | Five new `[LoggerMessage]`s (§5). Existing templates unchanged |

**Overlap with #2991's branch** (`fix/2991-repoint-ordered-by-changedat`, spec 385): none. That
branch touches `Application/Log.cs` (not `Infrastructure/Log.cs`),
`StreamDistributionInfrastructureModule.cs` (not touched here), persistence, migrations and
Application/Domain tests. It touches no `Reconciler/` file, no `Gateways/` file, no
`Infrastructure.Tests/Reconciler|Gateways|Fakes` file and no shard filter. The only shared file is
`tests/Integration.Tests/ci-shards/shard-N.filter`, and only if #2991 adds an integration class
(today it does not). That would be a one-line rebase conflict at worst.

**Test-file edit note:** `tests/StreamDistribution.Infrastructure.Tests/Fakes/SourceRepairFakes.cs` is an additive edit of an existing file (`StopFailingPatchesFor`), as §8.1 and tasks.md record.

## 2. Types and invariants

No domain type. `MediaMtxPath` is the key (an existing value object). Outcome and time are
infrastructure-private.

```csharp
// Reconciler/SourceRepairFailureLog.cs. Internal, sealed, not thread-safe by design:
// one instance per MediaMtxSourceRepair, and a repair never runs two passes at once
// (PeriodicTimer is sequential, pinned by A_pass_longer_than_the_interval_never_overlaps_the_next).
internal sealed class SourceRepairFailureLog(ILogger logger, TimeProvider timeProvider)
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(10);
    private readonly Dictionary<MediaMtxPath, FailureState> failures = [];
    private readonly HashSet<MediaMtxPath> failedThisPass = [];

    public void Failed(MediaMtxPath path, Exception failure);    // FR-002 / FR-003
    public void Succeeded(MediaMtxPath path);                     // FR-004
    public void PassCompleted();                                  // FR-005: retain only failedThisPass, then clear it

    private readonly record struct Outcome(Type ExceptionType, HttpStatusCode? Status);
    private sealed record FailureState(Outcome Outcome, long LastWarnedAt);  // GetTimestamp() ticks
}
```

The outcome is a private `record struct`. It is not a domain value object, because §II's scope is
the domain model (constitution §II, ADR-0139/0140), and this is infrastructure log bookkeeping.

- **Invariant L1 (first and changed failures always warn).** A path with no state, or with a
  different outcome, logs `ReconcilerFailedToRepairPathSource(ex, path)` (Warning, with the
  exception).
- **Invariant L2 (at most one repeat Warning per 10 minutes per path, per outcome).** When the
  outcome is the same and `timeProvider.GetElapsedTime(state.LastWarnedAt) < RepeatInterval`, the
  failure is logged at Debug only. At `>=`, it is a Warning and `LastWarnedAt` resets.
- **Invariant L3 (404 is never a Warning).** `HttpRequestException { StatusCode: NotFound }` logs
  `ReconcilerRepairPathGone(path)` at Debug, removes the path's state, and is not added to
  `failedThisPass`.
- **Invariant L4 (bounded).** `PassStarting()` clears `failedThisPass`; after `PassCompleted()`, `failures.Keys ⊆ failedThisPass ⊆ persisted
  paths of that pass`. The size is at most the live stream count.
- **Invariant L5 (a cut-short pass).** The catch filter is unchanged. `PassCompleted()` is the last
  statement of `RepairAsync`, so a throw (list GET failure, caller cancellation) skips it: entries
  applied before the throw stand, and `failedThisPass` is reset by `PassStarting()` at the next
  pass start, so nothing from the cut-short pass leaks into the next. `Succeeded` stays so that a
  success before a cancelled pass still resets its path.
- Spec 378's R1 (only the database's value is written), R2 (ordinal) and R3 (at most two PATCHes
  per path per pass) are untouched. This change touches only the catch and the bookkeeping.

## 3. The repair pass (`MediaMtxSourceRepair`)

```
ctor (ILogger logger, TimeProvider timeProvider)  -> failureLog = new(logger, timeProvider)
ctor (ILogger logger) : this(logger, TimeProvider.System)   // startup pass + every existing test

RepairAsync(gateway, snapshot, ct)
  observed  = gateway.ListConfiguredPathSourcesAsync(ct)   // may now be a bounded, partial read
  persisted = snapshot.ListLiveSourcesAsync(ct)
  for each diverged path: if TryRepairPathAsync(...) repaired++
  failureLog.PassCompleted()                              // FR-005, only on a completed pass
  return repaired

TryRepairPathAsync
  try
     PATCH, log Repaired, re-read, maybe PATCH again, log Repaired     // unchanged
     failureLog.Succeeded(path); return true
  catch (Exception ex) when (!ct.IsCancellationRequested)            // unchanged filter
     failureLog.Failed(path, ex); return false

SourceRepairFailureLog.Failed(path, ex)
  if ex is HttpRequestException { StatusCode: NotFound }: log PathGone (Debug); failures.Remove(path); return
  failedThisPass.Add(path)
  outcome = new(ex.GetType(), (ex as HttpRequestException)?.StatusCode)
  now = timeProvider.GetTimestamp()
  if !failures.TryGetValue(path, out state) or state.Outcome != outcome
       or timeProvider.GetElapsedTime(state.LastWarnedAt) >= RepeatInterval:
       logger.ReconcilerFailedToRepairPathSource(ex, path)          // Warning, existing template
       failures[path] = new(outcome, now)
  else:
       logger.ReconcilerStillFailingToRepairPathSource(path, outcome.ExceptionType.Name, StatusText(outcome.Status))   // Debug
       // LastWarnedAt is kept, so the next Warning is 10 min after the last one, not after the last Debug
```

The re-patch inside the same `try` (spec 378 FR-003) shares the catch, so its failure (including a
404) is classified the same way.

**Sweep** (`MediaMtxSourceRepairSweep`): add
`private readonly MediaMtxSourceRepair repair = new(logger, timeProvider);` (primary-constructor
parameters in a field initializer), and in `RunPassAsync` replace `new MediaMtxSourceRepair(logger)`
with `repair`. The constructor signature is unchanged, so `StreamDistributionRegistrationCharacterisationTests`
and every sweep test compile and pass untouched. The sweep's own catch (`SourceRepairSweepFailed`)
is unchanged (non-goal).

**Startup** (`MediaMtxReconciler.ReconcileOnceAsync`):

```csharp
logger.ReconcilerStartupPassComplete(configured.Count, expected.Count, removed, readded);

try
{
    await new MediaMtxSourceRepair(logger)
        .RepairAsync(gateway, new StreamSourceSnapshot(context), cancellationToken);
}
catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
{
    logger.ReconcilerStartupSourceRepairFailed(ex);
}
```

This filter mirrors `RunDeferredAsync`'s and spec 378 FR-009: shutdown still propagates and is
still not logged (`RunDeferredAsync`'s own filter swallows it, as today).

## 4. The bounded read (`ListConfiguredPathSourcesAsync` only)

```
const int MaxConfiguredSourcePages = 1000          // private const in MediaMtxRtspGateway
for page in 0 .. MaxConfiguredSourcePages-1:
    GET /v3/config/paths/list?page={page}&itemsPerPage=100 ; EnsureSuccessStatusCode   // unchanged
    payload = ReadFromJsonAsync<JsonElement>
    if !AddPageToSources(payload, sources): return sources          // items absent/not array/empty -> stop
    if !payload.TryGetProperty("pageCount") or kind != Number: return sources   // today's "one page"
    if !pageCountElement.TryGetInt32(out pageCount):
        logger.MediaMtxConfiguredSourcesPageCountUnreadable(page); return sources
    if page + 1 >= pageCount: return sources
logger.MediaMtxConfiguredSourcesPageCapReached(MaxConfiguredSourcePages); return sources
```

`AddPageToSources` returns whether the page held at least one item (any item, before the canonical
and `source` filters). A page of only non-canonical paths is still a page. The method is private
and used only by this reader. The order matters: page 0's items are always kept, and a page is
checked for emptiness before its `pageCount` is trusted.

Equivalence with today on well-formed input (the characterisation set, which must stay unmodified
and green):
`Every_page_mediamtx_reports_is_merged` gives `[0,1,2]`.
`A_response_without_a_page_count_is_one_page` gives `[0]`.
`An_empty_last_page_and_a_page_count_that_shrinks_mid_read_end_the_read_cleanly` gives `[0,1]`
(today page 1's `pageCount=2` stops the read; under the new code its empty `items` does, with the
same result).
`A_path_listed_on_two_pages_while_mediamtx_shifts...` gives `[0,1]`, with the later value winning.
The `MediaMtxSourceRepairOverRealGatewayTests` double serves `pageCount = 1`.

**Not shared with the older readers** (spec header, FR-009, #3010). A "shared enumerator" that left
them unchanged would have to carry both the old and the new termination rules behind a switch. That
is two loops wearing one name, and exactly the speculative generality ADR-0036 rules out.

## 5. Logging (ADR-0050, spec 373)

Existing templates are unchanged. New, in `Infrastructure/Log.cs`:

| Name | Level | Template | Args |
|---|---|---|---|
| `ReconcilerStillFailingToRepairPathSource` | Debug | `Reconciler still cannot repair MediaMTX path {Path} ({ExceptionType} {Status}); the Warning repeats at most every 10 minutes.` | `MediaMtxPath`, `string` (type `Name`), `string` (status as an integer string, or `"-"` when there is none) |
| `ReconcilerRepairPathGone` | Debug | `Reconciler skipped MediaMTX path {Path}: path gone (404), its stream was retired during the pass.` | `MediaMtxPath` |
| `ReconcilerStartupSourceRepairFailed` | Warning | `MediaMtxReconciler source repair after the startup pass failed; the periodic sweep retries.` | `Exception` |
| `MediaMtxConfiguredSourcesPageCountUnreadable` | Warning | `MediaMTX config path list reported a pageCount that is not a 32-bit integer on page {Page}; the source repair compares only the paths read so far.` | `int` |
| `MediaMtxConfiguredSourcesPageCapReached` | Warning | `MediaMTX config path list still reported more pages after {PageCap} pages; the source repair compares only the paths read so far.` | `int` |

**Why the Debug line has no exception.** A stack trace every 10 s is the volume this spec removes.
The type and status identify the outcome. The full exception is on the Warning that started the run
of failures and on every 10-minute repeat.

**Why no raw `pageCount`.** It is a MediaMTX-supplied token of unbounded length (`1e400`, or a
10,000-digit literal). The page index is enough to find it.

**The logger category is unchanged**: the sweep's logger
(`ILogger<MediaMtxSourceRepairSweep>`) or the reconciler's. The two gateway Warnings use
`ILogger<MediaMtxRtspGateway>`, which is already injected.

## 6. Messaging

None. No domain or integration event.

## 7. Boundary rules

- Only StreamDistribution plus `Shared.Kernel` (`Ensure`, `UrlRedaction`). The BCL's `TimeProvider`
  and `HttpStatusCode`.
- No `static` mutable state (the decision). `RepeatInterval` and `MaxConfiguredSourcePages` are
  immutable.
- Guards: `Ensure.That(logger).IsNotNull()` and `Ensure.That(timeProvider).IsNotNull()` where the
  existing code guards constructor inputs. The current primary constructor guards nothing, so
  mirror that and add none beyond `RepairAsync`'s existing guards (ADR-0036: no drive-by guards).
- House rules: `var`; no underscore fields; collection expressions; `Option<T>` is not needed (no
  absent parameter).

## 8. Tests (phase 4a: RED; adversarial and security required)

### 8.1 Pinned log behaviour today, and the effect of this change on each

Every existing assertion on these paths' log names, levels or texts:

| Test (file) | What it pins | Under this change |
|---|---|---|
| `MediaMtxSourceRepairPathFailureTests.A_patch_that_fails_with_a_non_http_exception_does_not_stop_the_other_path_being_repaired` (×3) | Exactly 1 Warning; message contains the path; `Exception` is the same instance; no `h264` | **Unchanged.** A fresh instance means a first failure, so it Warns with the exception |
| `MediaMtxSourceRepairPathFailureTests.A_committed_source_re_read_that_fails_does_not_stop_the_other_path_being_repaired` | Same as the row above | **Unchanged** |
| `MediaMtxSourceRepairPathFailureTests.A_cancellation_of_the_callers_token_that_surfaces_as_a_task_cancelled_exception_still_ends_the_pass` | Throws OCE; no entry ≥ Warning | **Unchanged** (FR-007) |
| `MediaMtxSourceRepairTests.A_refused_repair_is_logged_with_the_path_and_the_other_paths_are_still_repaired` | A Warning containing the path, with an `HttpRequestException` (status null) | **Unchanged** (first failure) |
| `MediaMtxSourceRepairTests.A_refused_repair_of_a_secret_bearing_source_does_not_leak_it_either` | Entries not empty; no `SECRET` | **Unchanged** |
| `MediaMtxSourceRepairTests.No_log_line_carries_the_full_source_only_its_origin` | Exact `Repaired` text; no `SECRET` in messages or exceptions | **Unchanged** |
| `MediaMtxSourceRepairAdversarialTests.A_retire_that_removes_the_path_before_our_patch_is_skipped_and_the_pass_continues` | Outcomes only (repaired=1, no add/remove, no re-read). **No log assertion** | **Unchanged.** Its 404 is now Debug "path gone" instead of a Warning, which nothing asserts. This is the behaviour the decision authorises |
| `MediaMtxSourceRepairAdversarialTests.A_cancellation_mid_pass_propagates_and_patches_nothing_further` | No entry ≥ Warning | **Unchanged** |
| `MediaMtxSourceRepairAdversarialTests.A_source_only_mediamtx_holds_is_logged_by_origin_and_never_verbatim` (×4) | Exact `Repaired` text | **Unchanged** |
| `MediaMtxSourceRepairUserInfoLogRedactionTests` (2) | `Repaired` text, placeholder | **Unchanged** |
| `MediaMtxSourceRepairOverRealGatewayTests.A_refused_patch_whose_error_body_echoes_the_secret...` | Entries not empty; no `SECRET` in messages or exceptions | **Unchanged.** The 400 is a first failure |
| `MediaMtxSourceRepairOverRealGatewayTests.An_empty_last_page_and_a_page_count_that_shrinks_mid_read_end_the_read_cleanly` | Pages `[0,1]` | **Unchanged** (§4 equivalence) |
| `MediaMtxRtspGatewayConfiguredSourcesTests` (5) | Pages, verbatim sources, one page without `pageCount` | **Unchanged** (§4 equivalence) |
| `MediaMtxRtspGatewayPaginationTests` (5) | The older readers' pages | **Unchanged.** Those readers are not touched |
| `MediaMtxSourceRepairSweepTests.A_failing_pass_is_logged_and_the_next_tick_still_runs` | Exact `SourceRepairSweepFailed` text, Warning, `InvalidOperationException` | **Unchanged** (non-goal) |
| `MediaMtxSourceRepairSweepAdversarialTests` (shutdown: none ≥ Warning; timeout: Warning `TaskCanceledException`) | Pass-level only | **Unchanged** |
| `MediaMtxReconcilerStartupOrderingTests.A_reconcile_failure_after_ApplicationStarted_is_logged_not_thrown` | Warning containing "startup pass failed", `InvalidOperationException` | **Unchanged.** The failure is in `CreateScope`, before the repair, so it still reaches `RunDeferredAsync` |
| `MediaMtxReconcilerPassCharacterisationTests` (C7) | The exact ordered event list ending with the summary | **Unchanged.** The repair finds nothing to repair, logs nothing, and does not throw |

**Expected edits to existing assertions: none. Expected compile-necessity edits to existing test
files: none.** The one-argument constructor and the sweep's constructor are kept, and
`IRtspGateway` is unchanged. If phase 4 finds that an existing assertion must change, that is a
**BLOCK**, not an adjustment: the decision authorises no such edit.

### 8.2 New tests

See tasks.md (R1-R8) for files, assertions and commands.

## 9. Risks

| Risk | Guard |
|---|---|
| Suppression hides a real new fault | L1: any change of type or status Warns at once (US1 scenarios 2-3). The adversarial pass covers the flap and the outage onset |
| State grows without bound | L4 / FR-005: retain only the paths that failed this pass. The test retires a path and confirms its next failure Warns again |
| State reset by mistake (the per-tick instance survives a refactor) | Integration R7(a): the real sweep over real Postgres, ≥4 passes, exactly 1 Warning |
| Wall-clock step silences or floods | FR-006 monotonic timestamp. Test R6 with a clock whose wall time steps back while its timestamp advances |
| A bounded read truncates a real list | The cap is 1,000 pages = 100k paths, 400× the target. A truncated repair read fails safe (unseen paths are not repaired). The cap is logged (A3) |
| Stop-on-empty misreads a filtered page as empty | Emptiness is the raw `items` length, before the canonical/`source` filters. Test: a page of only non-canonical paths followed by a canonical page is read through |
| Caught startup-repair failure hides shutdown | Same `when (!IsCancellationRequested)` filter. Test: a cancelled token propagates |
| An older reader changed by accident | `MediaMtxRtspGatewayPaginationTests` unmodified and green. Review diff: the hunks are confined to `ListConfiguredPathSourcesAsync`, `AddPageToSources` and `Log.cs` |
| Integration red not observable locally (Docker) | R1-R5 are assertion-red without Docker. R7 is quoted from the CI job log, or the PR says it was not observed (spec 378 precedent) |
