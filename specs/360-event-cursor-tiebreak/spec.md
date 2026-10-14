# Spec 360: The event list cursor breaks IngestedAt ties on the event identifier

**Feature Branch**: `fix/2938-event-cursor` (cut from `origin/develop` at `c6fe36d3`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[JUDGEMENT]** (§6).

**Input**: Issue [#2938](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2938),
*EventIngestion cursor pages on IngestedAt strictly.* Raised from tracker #2916 ("EventIngestion
cursor uses IngestedAt strictly"), pinned as-is by spec 346's characterisation test
`The_cursor_is_strict_on_the_ingested_time`. The issue asks for the defect to be confirmed by a
failing test (a tie across a page boundary) before any change, and to be closed if the ordering
already has a tiebreaker.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (which rows page 2 returns changes):
Phase 4a is **RED** (constitution §Testing; ADR-0139).

**Spec number.** 360. Checked 2026-10-14 after `git fetch origin`: `origin/develop` (`c6fe36d3`)
holds up to 357; across every remote head (`git ls-tree <ref> specs/`) 358
(`fix/2935-system-variables-defects`) and 359 (`fix/2937-…`) are claimed. Re-check before the PR is
opened.

**ADRs.** ADR-0039 (Guid v7 identifiers — the tiebreak key), ADR-0090 (`EventIdentifier`),
ADR-0089 / ADR-0047 (`INVALID_CURSOR` stays the only cursor failure), ADR-0103 (integration tests on
the Aspire fixture), ADR-0037 / ADR-0144 (workflow, lane), ADR-0139 (red first), ADR-0036 (smallest
change). Spec 006 FR-018 is the requirement this restores: *"Pagination: cursor-based on
`(ingestedAt, eventId)`."* **No new ADR is needed**: the cursor's wire format already carries the
event identifier and does not change (§2).

**Latency budget.** N/A — `GET /events` is an operator read API, not on the event-to-overlay path
(constitution §IV).

---

## 1. Premise verified (Phase 1 investigation)

| Question | Finding (at `c6fe36d3`) |
|---|---|
| Does the cursor predicate compare `IngestedAt` strictly? | **Yes.** `ListEventsQueryHandler.BuildPagedQuery` (`src/EventIngestion/Application/Queries/Handlers/ListEventsQueryHandler.cs:86-93`): `source.Where(e => e.IngestedAt < c.Ingested)`. The code comment says the cursor's event id "is reserved for a future tuple-compare tightening". |
| Is there a tiebreaker in the ordering? | **In the `ORDER BY`, yes; in the cursor predicate, no.** Lines 95-98 order `IngestedAt DESC, Id DESC`, and `EncodeCursor` writes `(last.IngestedAt, last.Id)`. The predicate ignores the id, so ordering is stable *within* a page but the page boundary is not. |
| Skip or repeat? | **Skip only, never repeat.** Strict `<` excludes every row whose `IngestedAt` equals the cursor's — including the tied rows with a *smaller* id that had not been served yet (they fell past `pageSize`). A row can never be served twice. The issue's "or repeat" does not occur. |
| Can `IngestedAt` actually tie? | **Yes, by construction, and most likely on the batch path.** `Event.Ingest` stamps `clock.UtcNow` per event (`Event.cs:57,66`; `SystemClock` = `DateTimeOffset.UtcNow`, 100 ns ticks). `ingested_at` is a Postgres `timestamptz` — **microsecond** precision — so two events stamped within the same microsecond store identical values. `IngestEventBatchCommandHandler.Build` (`:200-213`) creates a drained batch's aggregates in a tight loop with one `clock`; sub-microsecond spacing between consecutive envelopes is plausible, and nothing prevents it. Guid v7 ids do not help: they tiebreak the `ORDER BY` but the predicate never consults them. |
| Is the consistent convention established elsewhere? | **Yes.** AuditObservability's `SearchAuditQueryHandler.FilterByWindowAndCursor` (`:185-211`) uses `OccurredAt < c.OccurredAt \|\| (OccurredAt == c.OccurredAt && ((Guid)Id).CompareTo(c.AuditIdentifier) < 0)` over the identical `(ticks.guid)` base64 cursor shape (`AuditCursor`), and `SearchAuditPagingCharacterisationTests.Paging_newest_first_walks_ties_by_descending_identifier_without_overlap_or_gap` proves it. EventIngestion is the outlier. |
| Is the cursor a public contract whose change needs versioning? | **The format does not change.** It stays base64 of `"{UtcTicks:D19}.{eventId:N}"`. Every cursor already issued carries the id and decodes unchanged; it simply starts being honoured. The cursor is opaque: no `Shared.Contracts` type, no frontend consumer (`apps/` has no `/events` cursor client — only the audit page pages), no e2e spec. |
| Is it pinned? | **Yes, twice.** `tests/EventIngestion.Application.Tests/Queries/ListEventsPagedQueryCharacterisationTests.cs` → `The_cursor_is_strict_on_the_ingested_time` (spec 346); `tests/EventIngestion.Infrastructure.Tests/ListEventsQueryTranslationCharacterisationTests.cs` → `Every_filter_and_the_cursor_reach_the_where_clause` counts `ingested_at` **3** times in the `WHERE` (the composite predicate references it twice, making 4). |
| Does the existing integration paging test cover it? | **No.** `ListEventsFilteringTests.Paging_through_a_set_yields_every_event_exactly_once` seeds seven events through `POST /events/manual`, one HTTP round trip apart — their `IngestedAt` values are milliseconds apart and never tie. |

**Verdict: proceed.** The defect is real (spec 006 FR-018 says the cursor is `(ingestedAt,
eventId)`; the implementation pages on `ingestedAt` alone), reachable (microsecond column, per-event
stamping inside a batch loop), and the fix has an in-repo precedent.

## 2. Decision

Replace the strict predicate with the composite one, mirroring the Audit context:

```text
IngestedAt < cursor.Ingested
  OR (IngestedAt == cursor.Ingested AND EventId < cursor.EventId)
```

`ORDER BY ingested_at DESC, event_id DESC` is unchanged, so the predicate and the ordering now
agree on the same key. Encoding, decoding, `INVALID_CURSOR`, page-size rules and every filter are
untouched.

## 3. User stories

### US1 (P1) — An operator paging through events sees every event exactly once, even when two share an ingest timestamp

**Why P1**: it is the whole issue. A skipped event is invisible: each page looks complete, and the
gap only shows when pages are reassembled — exactly the audit-trail kind of loss an industrial
event log must not have.

**Independent test**: page through a set containing ≥ 3 events with an identical `IngestedAt`
straddling a page boundary; the concatenated pages equal the seeded set, in order, with no gap and
no duplicate. Proven in-memory against `BuildPagedQuery`/`HandleAsync` (unit) and against Postgres
through `GET /events` (integration).

## 4. Acceptance scenarios

```gherkin
Scenario: AS-1 ties straddling a page boundary are all served (the defect)
  Given fab "munich" has events A, B, C with the same IngestedAt T and ids A > B > C
  And one older event D at T - 1 min
  When the operator lists with pageSize 2
  Then page 1 is [A, B] with a nextCursor
  And page 2 is [C, D] with no nextCursor
  And no event is missing and none appears twice

Scenario: AS-2 a cursor positioned on the last tied row serves only older rows
  Given events A, B, C tied at T, and D at T - 1 min
  When the operator lists with the cursor (T, C)
  Then the result is [D]

Scenario: AS-3 strictly older rows still follow the cursor (unchanged)
  Given a cursor (T, X) where no row shares T
  Then exactly the rows with IngestedAt < T are returned, newest first

Scenario: AS-4 a cursor issued before this change still works (format unchanged)
  Given a cursor encoded as base64("{UtcTicks:D19}.{eventId:N}") by the previous build
  When it is submitted
  Then it decodes and pages from (ingestedAt, eventId) without INVALID_CURSOR

Scenario: AS-5 bad request (unchanged)
  Given a cursor that is not base64 of "<ticks>.<32-hex>"
  Then the answer is 400 INVALID_CURSOR
  And a pageSize above 1000 is 400 PAGE_SIZE_OUT_OF_RANGE

Scenario: AS-6 auth and fab scope (unchanged)
  Given a caller without the event read scope -> 403
  And the cursor never widens the fab filter taken from the caller's claims (spec 018)
```

## 5. Functional requirements

- **FR-001** With a cursor `(t, id)`, `BuildPagedQuery` MUST select rows where
  `IngestedAt < t`, or `IngestedAt == t` and `Id < id` — the same order as its `ORDER BY`.
- **FR-002** The composite predicate MUST translate to SQL (no client evaluation), with both
  `ingested_at` and `event_id` in the `WHERE` clause.
- **FR-003** The cursor wire format, `INVALID_CURSOR`, page-size limits, filters and fab scoping
  MUST NOT change.
- **FR-004** No migration, no index, no `Shared.Contracts`, endpoint or frontend change.

## 6. Judgement calls

- **[JUDGEMENT] J1 — OR-form predicate, not a Postgres row-value comparison.** `(ingested_at,
  event_id) < (@t, @id)` is the index-friendlier SQL, but needs a provider-specific
  `EF.Functions` call that LINQ-to-objects cannot run, which would break every in-memory test of
  `BuildPagedQuery` (spec 346's characterisation suite). The OR form is what Audit already ships.
  No NFR on `GET /events` latency exists to trade against; revisit only on a measured regression.
- **[JUDGEMENT] J2 — the comparison is written as `((Guid)e.Id).CompareTo(c.EventId) < 0`**, the
  Audit spelling, because `EventIdentifier`'s own `<` operator is a user-defined operator on a
  value-converted struct and is not known to translate. The engineer may use the operator only if
  the translation tests prove it.
- **[JUDGEMENT] J3 — Guid ordering agreement.** .NET `Guid.CompareTo` and Postgres `uuid` order may
  differ for arbitrary GUIDs; it does not matter here because the `WHERE` and the `ORDER BY` are both
  evaluated by Postgres in production and both by .NET in the unit tests. The integration test
  (T004) is what proves the Postgres side, including that a `timestamptz` read back and re-encoded as
  ticks compares *equal* to the stored value.
- **[JUDGEMENT] J4 — the two pinned assertions are replaced, not edited to pass.** They pin the
  behaviour this spec removes; under ADR-0139 they are the red tests' counterparts, and the PR names
  them (§7).

## 7. Tests the change replaces

| Pinned test | Why it goes | Replacement |
|---|---|---|
| `ListEventsPagedQueryCharacterisationTests.The_cursor_is_strict_on_the_ingested_time` | Its name and premise (strict on time alone) is the defect. With `Guid.Empty` as the cursor id it would still pass by accident under the fix — an assertion that cannot tell the two behaviours apart. | `A_cursor_on_a_tied_ingested_time_serves_the_tied_rows_with_a_smaller_identifier` (red) + `A_cursor_on_the_last_tied_row_serves_only_older_rows` |
| `ListEventsQueryTranslationCharacterisationTests.Every_filter_and_the_cursor_reach_the_where_clause` — the `ingested_at` count of 3 | The composite predicate references `ingested_at` twice. | Same test, count 4 and an added `event_id` occurrence in the `WHERE`; message updated to name the composite. |

## 8. Out of scope

- The Audit cursor (already correct).
- A `(ingested_at DESC, event_id DESC)` index or a row-value rewrite (J1).
- The `IngestedAfter` / `IngestedBefore` filter bounds (they are filters, not cursors; strict by
  design and separately pinned).
