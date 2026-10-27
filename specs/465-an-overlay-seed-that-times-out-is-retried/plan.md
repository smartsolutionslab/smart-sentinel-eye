# Implementation Plan: An overlay seed that times out is retried

**Branch**: `fix/3182-retry-reverse-index-seed` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/465-an-overlay-seed-that-times-out-is-retried/spec.md`

## Summary

`ReverseIndexSeederHostedService` makes one `GET /overlays?state=Published` and gives up on any
failure, leaving the in-memory reverse index empty for the life of the process (#3182). The fix keeps
the class, its registration, its URL, client and parse, and changes three things:

1. **The seed becomes a background retry loop** with capped exponential backoff, ending on the first
   success or on host shutdown (FR-001..005, FR-008). `StartAsync` no longer awaits the first call.
2. **A late listing cannot undo an event** (FR-006): the index gains a seed-only write that skips any
   overlay an event has already touched, archived ones included.
3. **The failure log lines are rewritten** (FR-007): they carry attempt number and next delay, and
   lose the false self-heal sentence. `SeededOverlays` is untouched.

No new ADR (the owner chose "retry only" on the issue). Phase 4a: **red first**.

## Technical Context

**Language/Version**: C# / .NET 10, NRT on.
**Primary Dependencies**: `Microsoft.Extensions.Hosting` (`BackgroundService`), `IHttpClientFactory`
named client `overlay-designer`, existing `TimeProvider.System` singleton
(`SystemVariablesInfrastructureModule.cs:85`). No new package.
**Storage**: N/A. The index stays in memory and is not persisted (spec Out of scope).
**Testing**: xUnit + Shouldly + hand-written fakes (ADR-0052/0054). A hand-written `ManualTimeProvider`
is added under `tests/SystemVariables.Infrastructure.Tests/Fakes/`, a minimal copy of the Identity one:
test projects do not reference each other, and `FakeTimeProvider` is deliberately not referenced anywhere.
**Target Platform**: Linux container on k3s, and Aspire run mode in dev.
**Project Type**: Backend service, bounded context `SystemVariables`.
**Performance Goals**: none per event. The leg is touched by availability only.
**Constraints**: starting never waits on the seed (ADR-0116); cancellation ends the loop promptly
(ADR-0049); no `ConfigureAwait`.
**Scale/Scope**: one hosted service, one interface method, one in-memory implementation, one Log file.

## Decisions fixed here (the spec deferred them)

| Item | Decision | Why |
|---|---|---|
| Backoff | initial 2 s, ×2, cap 30 s, no jitter | An attempt can already take 30 s (the Polly timeout). Doubling from 2 s reaches the cap on the fifth wait (2, 4, 8, 16, 30). 30 s matches the spec's "few tens of seconds". One instance per pod, so jitter buys nothing. |
| Constants | `private static readonly` fields on the service, not options | CLAUDE.md "no config knobs for needs that don't exist". Tests drive the delay through the clock, not through a knob. |
| Delay seam | `TimeProvider` constructor parameter, `Task.Delay(delay, timeProvider, ct)` | Already registered as a singleton; `AddHostedService` resolves it. No extra interface. |
| Loop shape | `ReverseIndexSeederHostedService : BackgroundService`; `ExecuteAsync` begins with `await Task.Yield()` | `BackgroundService` is an `IHostedService`, so `SystemVariablesRegistrationCharacterisationTests` stays green unmodified. `Task.Yield` makes FR-003 true even for a handler that completes synchronously. |
| Attempt body | `AttemptAsync(attempt, delay, ct) → bool` holds today's branches; `true` = seeded | Parsing and `SeedOverlays` are moved unchanged, so specs 300/349/358 stay the unmodified net. |
| Failure logging | `SeedNonSuccessStatus`, `SeedRefused`, `SeedFailed`, `SeedMissingPublishedKey` gain `{Attempt}` and `{RetryDelay}` | FR-007 asks for one line with the failure, the attempt and the delay. A second "retry scheduled" line would double the log volume. Levels are unchanged: Warning, Error for refusal. |
| Cancellation | `OperationCanceledException` is not caught by the failure branch (today's `when` filter stays) and ends the loop silently | FR-005; keeps `A_cancelled_token_propagates_and_logs_nothing`. |

## FR-006 design — a late listing never wins over an event

`IReverseIndex` gains one method:

```csharp
/// Applies a listing entry only if no event has set or removed the overlay.
/// Returns whether it was applied.
bool TrySeedOverlayReferences(Guid overlayIdentifier, IReadOnlyList<string> labelTexts);
```

`InMemoryReverseIndex` keeps a `ConcurrentDictionary<Guid, byte> touchedByEvent`. `UpsertOverlayReferences`
and `RemoveOverlay` (the two event entry points) add the identifier. `TrySeedOverlayReferences` runs under
one lock shared with those writes, skips touched identifiers, and otherwise writes exactly as an upsert
does without marking. This covers both stories: an archive handled before the listing lands leaves a
tombstone, and a republish keeps its text. Events handled after the listing is applied overwrite it as
before. The set is bounded by the number of overlays and is never cleared.

The seeder's count (`SeededOverlays(count)`) counts overlays it applied. An overlay skipped for being
touched is not counted, the same as a skipped malformed entry today.

The test fake `tests/SystemVariables.Application.Tests/Fakes/InMemoryReverseIndex.cs` implements the
interface and gets the same method.

## Constitution Check

| Principle | Result |
|---|---|
| §II value objects | Pass. Infrastructure, no domain model change; `Guid` on the index is existing signature. |
| §IV latency (leg: Event → overlay state, ≤ 200 ms) | Pass. Availability only: while the index is empty no text resolves at all. No per-event path changes; the loop is off the request path. |
| §VII observability | Leg is implemented and has its dashboard recorded in §IV. This change adds no metric. The log lines are the observable (spec assumption) and carry attempt and delay. |
| §Testing | Behaviour-changing, so red first (ADR-0139). Parse tests stay green unmodified. |
| §VIII trust boundaries | Pass. Same credential, same client; a refusal is retried at `Error`. |
| ADR-0116 | Pass. Start never waits and never fails because of the seed. |
| ADR-0143 | Pass. The Polly GET retry stays inside one attempt; the loop delay is on top of it. |
| ADR-0049 | Pass. `CancellationToken` last, no `ConfigureAwait`. |
| ADR-0105 | Pass. `Ensure.That(...)` for new guards, no `ArgumentNullException.ThrowIfNull`. |
| Coverage gate (ADR-0065) | Infrastructure is not a gated layer; `Log` is already `[ExcludeFromCodeCoverage]`. |

Re-check after design: unchanged. No violations, so no Complexity Tracking.

## Project Structure

### Documentation

```text
specs/465-an-overlay-seed-that-times-out-is-retried/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── checklists/requirements.md
```

`contracts/` is skipped: no HTTP, event or DTO shape changes.

### Source code

```text
src/SystemVariables/
  Application/Resolution/IReverseIndex.cs                      + TrySeedOverlayReferences
  Infrastructure/Resolution/InMemoryReverseIndex.cs            + touched set, seed-only write
  Infrastructure/Resolution/ReverseIndexSeederHostedService.cs BackgroundService + retry loop
  Infrastructure/Log.cs                                        4 messages reworded, + Attempt/RetryDelay

tests/
  SystemVariables.Infrastructure.Tests/Fakes/ManualTimeProvider.cs   new
  SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederHostedServiceTests.cs        moves (see spec)
  SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederBranchCharacterisationTests.cs  500-warning test moves
  SystemVariables.Infrastructure.Tests/Resolution/InMemoryReverseIndex… seed-only tests          new
  SystemVariables.Application.Tests/Fakes/InMemoryReverseIndex.cs   + method
```

`SystemVariablesInfrastructureModule.BindReverseIndexSeeder` is unchanged: the same `AddHostedService`.

## Phase 4a declaration

**Red.** New behaviour, observed failing before the fix, failure quoted in the PR:

1. first call times out, second succeeds → index populated (fails today: one request, empty index)
2. 503 then 200; connection error then 200; 200 without `published` then 200
3. 401, 403 and a refused token mint, each then 200 → index populated
4. success on the third attempt → exactly three requests, none after
5. `StartAsync` returns while the first attempt is still pending
6. cancellation between attempts ends the loop and logs nothing
7. a failure line names the attempt and delay and does not contain "events arrive"
8. archive handled before the listing → overlay absent; republish before listing → newer text kept

Moved, still pass: the `A_server_error` / `A_404` / transport / refused tests re-stated per the spec's
list. **Unmodified net:** every listing-parse test (specs 300, 349, 358), `SeededOverlays` wording, the
registration characterisation.

## Phase 5 observation

SC-005 on the live stack: boot with `overlay-designer` held back, observe `404 OVERLAY_NOT_IN_REVERSE_INDEX`
on the snapshot, release it, observe `200` and resolved text without touching `system-variables`. Needs
the AppHost running; it was stopped for low memory and is started only on request.
