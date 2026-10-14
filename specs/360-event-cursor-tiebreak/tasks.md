# Tasks 360: The event list cursor breaks IngestedAt ties on the event identifier

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2938 (feature-level; on
Project #13 — the Phase 3 gate)

**Phase 4a colour: RED** (behaviour-changing). Engineer: **backend-engineer**. Test author:
**test-writer**, who returns verbatim red output before the engineer starts.

No foundational task: no `Shared.Kernel`, `Shared.Contracts`, AppHost or migration work.

## US1 (P1) — every event served exactly once across tied page boundaries

### Phase 4a — red (test-writer)

- [ ] **T001 [P] [US1]** In `tests/EventIngestion.Application.Tests/Queries/ListEventsPagedQueryCharacterisationTests.cs`,
  **replace** `The_cursor_is_strict_on_the_ingested_time` with:
  - `A_cursor_on_a_tied_ingested_time_serves_the_tied_rows_with_a_smaller_identifier` — events
    `0x03`, `0x02`, `0x01` all at `Noon`, `0x04` at `Noon - 1 min`; cursor `(Noon, id 0x02)`;
    expects `[1, 4]`. **Red today**: returns `[4]`.
  - `A_cursor_on_the_last_tied_row_serves_only_older_rows` — same set, cursor `(Noon, id 0x01)`;
    expects `[4]`. (Green today; kept as the guard that the fix does not over-include.)
  - Update the class summary's counterfactual line: changing `CompareTo(...) < 0` to `<= 0`
    turns the second test red (mirror `SearchAuditPagingCharacterisationTests`).
- [ ] **T002 [P] [US1]** In `tests/EventIngestion.Application.Tests/Queries/ListEventsQueryHandlerTests.cs`,
  add `Paging_across_events_sharing_an_ingested_time_serves_each_exactly_once` — seed A/B/C tied
  at `T` (ids descending) plus D at `T - 1 min` through `TestEventQuerySource`; `HandleAsync`
  with `pageSize: 2`, then again with the returned `NextCursor`; concatenated ids equal
  `[A, B, C, D]` and the second page's `NextCursor` is null. **Red today**: page 2 is `[D]`.
  Update the class summary, which says cursor pagination is left to integration tests.
- [ ] **T003 [P] [US1]** In `tests/EventIngestion.Infrastructure.Tests/ListEventsQueryTranslationCharacterisationTests.cs`,
  `Every_filter_and_the_cursor_reach_the_where_clause`: `ingested_at` occurrences **4** ("IngestedAfter,
  IngestedBefore and the cursor's two-part compare") and `where.ShouldContain("event_id")`.
  **Red today** (3, and `event_id` absent from `WHERE`). The no-cursor sibling stays at 2, unmodified.
- [ ] **T004 [P] [US1]** In `tests/Integration.Tests/EventIngestion/ListEventsFilteringTests.cs`
  (existing class, already in `ci-shards/shard-3.filter`), add
  `Paging_through_events_sharing_an_ingested_time_yields_each_exactly_once`:
  seed via `aspire.CreateEventIngestionDbContextAsync()` + `ExecuteSqlRawAsync` (pattern:
  `FabPartitionRemovalIntegrationTests.SeedDresdenEventAsync`) three rows in fab `munich` with one
  unique kind, one literal `ingested_at` (microsecond value), and three fixed `event_id`s, plus one
  row a minute older; walk `GET /events?fabId=munich&kind=<kind>&pageSize=2` reusing the existing
  walking loop shape; assert the walk equals the four ids in `ingested_at DESC, event_id DESC`
  order. **Red today**: three ids returned ("the cursor skipped some").

T001–T004 own disjoint files → all `[P]`. Run all four; quote each failure verbatim.

### Phase 4b — green (backend-engineer)

- [ ] **T005 [US1]** (blocks on T001–T004 red) `src/EventIngestion/Application/Queries/Handlers/ListEventsQueryHandler.cs`:
  replace the strict cursor predicate with the composite of plan.md; replace the stale comment;
  extract an `AfterCursor` helper only if ADR-0084's method length requires it. Tests T001–T004
  must pass **without edits**.
- [ ] **T006 [US1]** (after T005) Regression sweep, unmodified and green:
  `ListEventsPagedQueryCharacterisationTests` (the other 11 facts),
  `ListEventsQueryTranslationCharacterisationTests` (other 3),
  `Integration.Tests.EventIngestion.ListEventsTranslationTests` (incl.
  `Member_access_on_a_converted_column_does_not_translate`),
  `ListEventsFilteringTests` (existing two facts), `ListEventsQueryHandlerTests` validation facts.
  `dotnet format --verify-no-changes` and the Release build clean.

## Phase 5 — verify

- [ ] **T007** Boot the stack; seed the tied set as in T004 (or run T004 alone) and record the
  page-1/page-2 bodies in the verification note. Latency: N/A (off the SLO path).

## Dependencies

```
T001 ─┐
T002 ─┤
T003 ─┼─> T005 ─> T006 ─> T007
T004 ─┘
```
