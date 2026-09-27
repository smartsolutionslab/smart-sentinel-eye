# Plan 274 — The success that answered 500

**Spec**: [spec.md](spec.md) · **Issue**: #2490 · **Phase**: 2 (Plan)
**ADRs**: ADR-0142, ADR-0050, ADR-0036, ADR-0139 · **New ADR**: none

## 1. Constitution / ADR check

| Gate | Result |
|---|---|
| §II primitives | N/A — no domain model touched; `IdempotentOutcome`'s `Guid` is existing ServiceDefaults surface. |
| No cross-context references | Unchanged — ServiceDefaults only; no endpoint file edited. |
| §IV latency budget | **N/A** — REST idempotency bookkeeping, not the event→overlay path. |
| ADR-0142 | Mechanism preserved: key completed on success, released on nothing-created, replay unchanged. Only the store-throws case changes, from 500 to the decided response. |
| ADR-0050 | Signal is an OTel exception event on the request span — same as `ReleaseQuietlyAsync` (spec §6 resolved ambiguity). |
| "No drive-by error handling" | Exempt: the catch is at the durability boundary and carries its *why* (FR-005). |
| ADR-0105 guards | No new public entry point, so no new `Ensure.That`. |
| ADR-0084 metrics (advisory) | `RunAndRecordAsync` is ~30 LOC today; the catch goes in a new private helper rather than inline, keeping both methods under the limit. |

## 2. Where it lives

- `src/ServiceDefaults/Idempotency/IdempotentRequest.cs` — the only production file.
- `tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs` — existing class; no new test class,
  so no shard-filter entry is needed.

No entities, value objects, messaging, migrations or AppHost changes.

## 3. Design

### 3.1 Production change (4b)

Replace the unguarded `if/else` at the end of `RunAndRecordAsync` with a call to a new private helper,
then `return outcome.Response;`:

```csharp
/// <summary>
/// #2490. Records the outcome of work that already succeeded, without letting a
/// store failure here turn that success into a 500.
/// </summary>
private static async Task RecordQuietlyAsync(
    IIdempotencyStore store, IdempotencyScope scope, IdempotentOutcome outcome)
{
    try
    {
        if (outcome.ResourceIdentifier.HasValue) { await store.CompleteAsync(scope, outcome.ResourceIdentifier.Value, CancellationToken.None); }
        else { await store.ReleaseAsync(scope, CancellationToken.None); }
    }
    catch (Exception recordFailure)
    {
        // why-comment per FR-005
        Activity.Current?.AddException(recordFailure);
    }
}
```

- Placed next to `ReleaseQuietlyAsync`, named to pair with it.
- `CancellationToken.None` kept, as today (the caller's token must not abort bookkeeping for work
  already done).
- **No release in the catch** (FR-003) — the comment says why: a released key lets a retry re-run the
  work; a reserved one makes it 409 until `StaleAfter`.
- Catches `Exception` broadly, as `ReleaseQuietlyAsync` does: every failure here has the same
  disposition, and no cancellation can originate from `CancellationToken.None`.

### 3.2 Test change (4a)

- `RecordingStore` gains `public Exception? CompleteThrows { get; set; }`; `CompleteAsync` returns
  `Task.FromException(CompleteThrows)` when set (after recording nothing, so `Completed` stays null).
- Four new facts, spec §7. The activity fact copies the existing listener pattern verbatim (own
  `ActivitySource`, `AllData` sampling, `activity.ShouldNotBeNull(...)` guard against a vacuous pass).

## 4. Risks

| Risk | Mitigation |
|---|---|
| The catch is later widened to cover `work()` too, masking real failures. | Helper wraps only the store calls; the existing #2290 facts (work throws → exception propagates) are characterisation and would fail. |
| The activity assertion passes vacuously (`Activity.Current` null in xUnit). | Reuses the listener pattern and its `ShouldNotBeNull` guard. |
| Reviewer reads the catch as a swallowed exception. | FR-005 comment + spec §6 records the decision and the rejected `ILogger` alternative. |
| Orphaned reservation after a failed complete. | Recorded as residual (spec §9); reclaimed after `StaleAfter`; retry design deferred per PO. |
