# Plan 360: The event list cursor breaks IngestedAt ties on the event identifier

**Spec**: [spec.md](spec.md) · **Issue**: #2938 · **Lane**: autonomous (ADR-0144), Phase 4a **RED**

## Constitution and ADR check

| Gate | Result |
|---|---|
| §II value objects | No new state; the predicate compares the existing `IngestedAt` / `EventIdentifier` VOs via their implicit unwraps. Pass. |
| Bounded contexts | EventIngestion only. No cross-context reference; Audit's `FilterByWindowAndCursor` is the *pattern* mirrored, not a dependency. Pass. |
| §IV latency | Off the event-to-overlay path. N/A. |
| §Testing / ADR-0139 | Behaviour-changing → red unit test + red integration test observed failing before the fix; failure quoted in the PR. |
| ADR-0047 / ADR-0089 | No error added; `INVALID_CURSOR` untouched. |
| ADR-0103 | Integration proof on the Aspire fixture, seeding via `aspire.CreateEventIngestionDbContextAsync()` raw SQL (pattern: `FabPartitionRemovalIntegrationTests`). No Testcontainers. |
| New ADR needed? | **No** — wire format unchanged; spec 006 FR-018 already decides `(ingestedAt, eventId)`. |

## Context and layers

- **Context**: EventIngestion.
- **Layer touched**: Application only —
  `src/EventIngestion/Application/Queries/Handlers/ListEventsQueryHandler.cs`, method
  `BuildPagedQuery`, the `if (cursor is { } c)` block (lines 86-93) and its stale comment.
- **Untouched**: Domain (`Event`, `IngestedAt`, `EventIdentifier`), Infrastructure
  (`EventConfiguration`, migrations), Api (`EventsEndpoints.Reads.cs`), `Shared.Contracts`, frontend.

## Entities / invariants

No new entity or value object. The invariant restored:

> For any cursor `(t, id)` produced from row *r*, the next page is exactly the rows that sort after
> *r* under `ORDER BY ingested_at DESC, event_id DESC`.

## The change (shape, for the engineer)

```csharp
if (cursor is { } c)
{
    source = source.Where(eventEntity =>
        eventEntity.IngestedAt < c.Ingested ||
        (eventEntity.IngestedAt == c.Ingested && ((Guid)eventEntity.Id).CompareTo(c.EventId) < 0));
}
```

Constraints:

- Compare the value objects / implicit unwraps, never `.Value` (does not translate — pinned by
  `ListEventsTranslationTests.Member_access_on_a_converted_column_does_not_translate`).
- If `IngestedAt == DateTimeOffset` does not bind to the implicit unwrap (record equality vs.
  conversion), compare `(DateTimeOffset)eventEntity.IngestedAt == c.Ingested`; prove translation
  with the Infrastructure test, do not guess.
- Replace the "negligible at 1k/s … reserved for a future tuple-compare" comment with a one-line
  *why* (ties are real at microsecond storage; ordering and cursor share one key).
- Possibly a method-length concern (ADR-0084 advisory, 30 LOC): if `BuildPagedQuery` grows past it,
  extract `AfterCursor(source, cursor)` alongside `FilterByOrigin`/`FilterByTime` — the existing
  split pattern from spec 346.

## Messaging

None. Read path only; no domain or integration event.

## Test strategy

| Level | File | What it proves | Colour before fix |
|---|---|---|---|
| Unit (LINQ-to-objects) | `tests/EventIngestion.Application.Tests/Queries/ListEventsPagedQueryCharacterisationTests.cs` | Predicate semantics on `BuildPagedQuery` | **red** |
| Unit (handler, two calls) | `tests/EventIngestion.Application.Tests/Queries/ListEventsQueryHandlerTests.cs` with `TestEventQuerySource` | `HandleAsync` page 1 → `nextCursor` → page 2 reassembles the tied set | **red** |
| Translation (offline) | `tests/EventIngestion.Infrastructure.Tests/ListEventsQueryTranslationCharacterisationTests.cs` | Composite predicate translates; `ingested_at` ×4 and `event_id` in `WHERE` | red (count 3 → 4) |
| Integration (Postgres) | `tests/Integration.Tests/EventIngestion/ListEventsFilteringTests.cs` (new `[Fact]` in the existing class — **no shard-filter entry needed**, the class is already in `shard-3.filter`) | Real `uuid` ordering + `timestamptz` µs round-trip through the cursor ticks | **red** |

**Is unit proof enough?** No — the unit tests prove the predicate's logic, but only Postgres can
prove that (a) a `timestamptz` read back, encoded as `UtcTicks` and decoded compares **equal** to
the stored value (the whole fix rests on that equality), and (b) Postgres's `uuid` `<` agrees with
its `ORDER BY event_id DESC`. HTTP ingest cannot manufacture a tie (the server stamps the time), so
the integration test seeds tied rows directly with `INSERT … ingested_at = @t` for three
`event_id`s, then pages through `GET /events?kind=<unique>&pageSize=2`.

## Risks

- **Index use**: an OR predicate may plan worse than the strict one on a large partition. Accepted
  (spec J1); no NFR exists on this endpoint.
- **Rebase-ordering**: two parked specs (358, 359) touch other contexts; no file overlap.
