# Plan 391: The two older MediaMTX list readers bound their page read, and a cut-short health read fails the tick

**Spec**: [spec.md](./spec.md). **Issue**: #3010 (split out of #2994).
**Prior art**: spec 388 (the bounded `ListConfiguredPathSourcesAsync`, plan §4), spec 309 (the
idempotent `add/`), spec 378.

## 1. Context and layers

This plan touches one bounded context, **StreamDistribution**, and only its Infrastructure layer,
apart from one doc comment in Domain. It needs no contract, no other context, no new project
reference, and no DI or AppHost change (constitution §III; spec FR-008).

| File | Change |
|---|---|
| `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` | A new private `ReadPagesAsync` loop (§3). `GetAllPathsHealthAsync` and `ListConfiguredPathsAsync` now use it. `AddPageToPaths` and `AddPageToHealth` return `bool` (whether the page had items). `ListConfiguredPathSourcesAsync` and `AddPageToSources` stay **byte-identical** |
| `src/StreamDistribution/Infrastructure/Log.cs` | Two new `[LoggerMessage]`s (§4). The existing templates do not change |
| `src/StreamDistribution/Domain/Stream/IRtspGateway.cs` | The doc comment of `GetAllPathsHealthAsync` only: a read that cannot be completed throws, and never returns part of the list (§5). No signature changes |

**Not touched:** `StreamHealthWatcher.cs` (FR-004), `MediaMtxReconciler.cs` (FR-005), every test
fake, and every other `IRtspGateway` implementation. The interface signatures stay the same, so the
seven test doubles compile unchanged.

## 2. Types and invariants

There is no domain type. `MediaMtxPath` is already a value object. The page-read outcome is private
to the gateway:

```csharp
// Private to MediaMtxRtspGateway.
private const int MaxListPages = 1000;

private enum PageReadEnd { Complete, PageCountUnreadable, PageCapReached }

private readonly record struct PageRead(PageReadEnd End, int Page);  // Page: the unreadable page, or the cap
```

It is a private infrastructure record, so constitution §II does not apply (its scope is the domain
model; ADR-0139/0140). Spec 388 used the same reasoning for `Outcome`.

- **B1 (bounded).** `ReadPagesAsync` issues at most `MaxListPages` GETs per call, whatever the
  response says.
- **B2 (no throw on the token).** `pageCount` is read with `TryGetInt32` and never with `GetInt32`.
  A JSON number that is not an Int32 ends the read as `PageCountUnreadable`.
- **B3 (an empty page ends the read).** A page whose raw `items` is absent, not an array, or of
  length zero ends the read as `Complete`. The count is taken before the canonical-name filter.
- **B4 (unchanged well-formed reads).** An absent or non-number `pageCount` means one page, and
  `page + 1 >= pageCount` means `Complete`. For every well-formed MediaMTX answer the requested
  pages are therefore the same as today's.
- **B5 (the health read never returns a partial list).** `GetAllPathsHealthAsync` returns only on
  `Complete`. On the other two ends it throws (FR-002).
- **B6 (the reconcile read always returns).** `ListConfiguredPathsAsync` returns on every end, and
  logs one Warning on the two that are not `Complete` (FR-003).

## 3. The shared loop

```
private async Task<PageRead> ReadPagesAsync(string listPath, Func<JsonElement, bool> addPage, CancellationToken ct)
  for page in 0 .. MaxListPages-1:
      GET {listPath}?page={page}&itemsPerPage=100 ; EnsureSuccessStatusCode      // unchanged
      payload = ReadFromJsonAsync<JsonElement>
      if !addPage(payload): return new(Complete, page)                          // B3
      if !payload.TryGetProperty("pageCount") or kind != Number: return new(Complete, page)   // B4
      if !pageCountElement.TryGetInt32(out pageCount): return new(PageCountUnreadable, page)   // B2
      if page + 1 >= pageCount: return new(Complete, page)                      // B4
  return new(PageCapReached, MaxListPages)                                      // B1
```

This is spec 388 plan §4's loop as it was proven there, with the two log calls lifted out to the
callers. The order matters in the same way: page 0's items are always kept, and a page is checked
for emptiness before its `pageCount` is trusted.

```
GetAllPathsHealthAsync(ct)
  Dictionary<MediaMtxPath, RtspPathHealth> health = []
  var read = await ReadPagesAsync("/v3/paths/list", payload => AddPageToHealth(payload, health), ct)
  return read.End switch
      Complete            => health
      PageCountUnreadable => throw new HttpRequestException(HttpRequestError.InvalidResponse,
                               $"MediaMTX path list reported a pageCount that is not a 32-bit integer on page {read.Page}; the health sweep cannot tell an unread path from a missing one.")
      PageCapReached      => throw new HttpRequestException(HttpRequestError.InvalidResponse,
                               $"MediaMTX path list still reported more pages after {read.Page} pages; the health sweep cannot tell an unread path from a missing one.")

ListConfiguredPathsAsync(ct)
  List<MediaMtxPath> paths = []
  var read = await ReadPagesAsync("/v3/config/paths/list", payload => AddPageToPaths(payload, paths), ct)
  if read.End == PageCountUnreadable: logger.MediaMtxConfiguredPathsPageCountUnreadable(read.Page)
  if read.End == PageCapReached:      logger.MediaMtxConfiguredPathsPageCapReached(read.Page)
  return paths
```

`AddPageToPaths` and `AddPageToHealth` keep their filtering as it is, and gain
`return items.GetArrayLength() > 0;`. The early return for an absent or non-array `items` returns
`false`. This mirrors `AddPageToSources` (`MediaMtxRtspGateway.cs:166-188`).

**Why the shared loop is not used by `ListConfiguredPathSourcesAsync` here** (spec §5 A2): it would
work, but it would be a refactor of spec 388's method inside a behaviour-changing fix. The house
rule says that is two issues. The follow-up moves that method onto `ReadPagesAsync`, folds
`MaxConfiguredSourcePages` into `MaxListPages`, and keeps every spec 388 test unmodified and green
as its characterisation.

**Why the shared loop is safe for the older readers:** spec 388 required that a shared enumerator
leave the two older readers' behaviour unchanged. That condition failed only because a bound
changes their behaviour on hostile input. The decision has now authorised exactly that change, so
the remaining requirement is B4. The characterisation set in tasks.md pins B4, together with
`MediaMtxRtspGatewayPaginationTests` kept unmodified.

**Existing comments.** The comment in `ListConfiguredPathsAsync` that points to "GetAllPathsHealthAsync's
own pagination loop above" is wrong after this change, and was already wrong about "above". Reword it
to say that the method shares `ReadPagesAsync`. Keep the reasoning comments, which explain why
pagination matters at 250 cameras.

## 4. Logging (ADR-0050; spec 388 FR-011)

The existing templates do not change. New entries in `Infrastructure/Log.cs`, next to spec 388's
pair:

| Name | Level | Template | Args |
|---|---|---|---|
| `MediaMtxConfiguredPathsPageCountUnreadable` | Warning | `MediaMTX config path list reported a pageCount that is not a 32-bit integer on page {Page}; the startup reconcile removes orphans only among the paths read so far.` | `int` |
| `MediaMtxConfiguredPathsPageCapReached` | Warning | `MediaMTX config path list still reported more pages after {PageCap} pages; the startup reconcile removes orphans only among the paths read so far.` | `int` |

The health path adds no log message. `StreamHealthWatcher`'s existing `HealthSweepFailed` (Warning,
with the exception) is the one line per failed tick. The two exception messages above appear only
as that line's exception. They name the page index or the cap, never the raw token, which is of
unbounded length (`1e400`, a 10,000-digit literal).

## 5. Port contract (`IRtspGateway.GetAllPathsHealthAsync` doc comment)

Add one `<para>` after the existing one about absent paths:

> A list that cannot be read to its end, because a `pageCount` cannot be read or the page cap is
> reached, throws `HttpRequestException` and never returns part of the list. A caller reads an
> absent path as "not registered", so a partial result would report live streams as gone.

This is the non-obvious *why* that a future implementation, or a refactor, has to keep. It is a
comment-only edit to the Domain port. The Domain's dependencies do not change.

## 6. Messaging

None. There is no domain or integration event. `StreamHealthChangedV1` is produced exactly as today.
The change makes fewer announcements happen, not different ones.

## 7. Boundary rules

- Only StreamDistribution, plus the BCL's `HttpRequestException` and `HttpRequestError` (.NET 8+).
- No `static` mutable state. `MaxListPages` is a `const`.
- No new guard. `ReadPagesAsync` is private, and its arguments are lambdas and literals
  (ADR-0105/ADR-0036: guards go at trust boundaries).
- House rules: use `var`; private fields have no underscore; use collection expressions; write the
  `switch` expression with one arm per line.

## 8. Tests (phase 4a: RED)

### 8.1 Existing tests on these paths, and what this change does to each

| Test (file) | What it pins | Under this change |
|---|---|---|
| `MediaMtxRtspGatewayPaginationTests` (5 facts) | Well-formed pages, `[0,1]` / `[0]`, canonical filtering, endpoint path | **Unchanged** (B4) |
| `StreamHealthWatcherScopeTests` (6 facts, including `A_failed_batch_call_skips_the_whole_tick_without_crashing_the_watcher`) | Watcher scoping, batch call, `HttpRequestException` → tick skipped | **Unchanged.** The watcher is not touched, and the stub gateway is not the real one |
| `MediaMtxRtspGatewayConfiguredSources*Tests`, `MediaMtxSourceRepair*Tests` | Spec 378/388 behaviour | **Unchanged** (FR-007, byte-identical) |
| `MediaMtxReconcilerPassCharacterisationTests` (C7, integration) | The exact ordered reconcile events | **Unchanged.** Its fake gateway is in-file, so the real gateway is not involved |
| `MediaMtxReconcilerIntegrationTests`, `StreamHealthTransitionTests`, `ProvisionRetireRaceIntegrationTests` (integration, real MediaMTX) | Real list reads | **Unchanged.** Real MediaMTX sends a well-formed `pageCount` (B4) |

**Expected edits to existing assertions: none. Expected compile-necessity edits: none.** If phase 4
finds that one must change, that is a **BLOCK**.

### 8.2 New tests

The files, facts and commands are in tasks.md (T001-T003). All of them are in
`tests/StreamDistribution.Infrastructure.Tests`. That is a unit project, which CI runs whole in the
backend job. **No `tests/Integration.Tests/ci-shards/shard-N.filter` entry is needed.** The shard
files partition `Integration.Tests` only.

## 9. Risks

| Risk | Guard |
|---|---|
| A real MediaMTX list is cut short and the watcher goes blind | The cap is 100k paths, 400 times the 250-camera target. On the health path the outcome is a failed tick (Warning), not a false outage, which is the decision's point |
| An older reader changes on well-formed input | B4. `MediaMtxRtspGatewayPaginationTests` stays unmodified, and the T003 characterisation guards (non-number, non-canonical-only page, `pageCount=0`) run for both readers |
| Stop-on-empty misreads a filtered page as empty | The raw `items` length is used. Guard T003(b) |
| The watcher is not actually protected (the gateway throws a type the inner catch misses, and the failure goes to the Error catch-all) | T002 drives the **real** watcher over the **real** gateway and asserts exactly one `HealthSweepFailed` Warning and nothing at Error. It fails if the exception type drifts |
| The spec 388 reader changes by accident | FR-007. The review diff has no hunk in `ListConfiguredPathSourcesAsync` or `AddPageToSources`, and every spec 388 test is unmodified |
| The partial reconcile list re-adds a live path non-idempotently | Guard T003(d) pins `AddPathAsync`'s 400 → `get/` 200 branch at the gateway, which had no unit cover before (spec P6) |
| Health-path Warning flood during a sustained hostile `pageCount` | Accepted (spec §6). One line per 2 s tick, each carrying the exception |
| A test hangs on today's unbounded loop instead of failing red | The stub handler throws `InvalidOperationException("unbounded read")` on its 1,101st request (spec 388 precedent) |
