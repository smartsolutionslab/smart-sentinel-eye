# Plan — Spec 131

## Phase 4a colour: RED, behaviour-changing

The ordering of a page changes. A test arriving green is a phase-4 failure.

## The hard part: making "undefined" fail on purpose

The defect is not that the handler produces a *wrong* order. It is that it
produces an order the query does not determine, and then pages by position
through it. Exhibiting that needs an execution where the database exercises the
freedom the missing tie-break leaves it.

Three ways to get there, and only one of them is both honest and deterministic.

### Rejected — assert the shape of the sort key

A test that reads `SortBy` and checks it mentions `camera_id` passes before and
after any equivalent fix and says nothing about paging. It is an assertion that
cannot fail for the reason it claims to. Not written.

### Rejected — page a real Postgres through the AspireFixture

This is the exhibition everyone wants and it cannot be made to fail on demand.
Postgres, given the same query twice against an unchanged heap, generally
returns ties in the same order — so the test would be **green today**, which is
the same defect as the first option wearing a fixture. Making it red would mean
provoking a plan change (`work_mem`, bound-dependent top-N heapsort) or
rewriting the heap between pages, and a test that depends on the planner's mood
is a flake, not evidence.

The fixture still has a job here, and it is a different one — see Phase 5.

### Chosen — two page requests, two physical row orders

Page 1 and page 2 are **two separate requests**: two HTTP calls, two
transactions, possibly two service instances. Nothing carries an ordering
guarantee between them beyond what the `ORDER BY` states. So the faithful model
of "the database is entitled to break the tie differently on the second request"
is to hand the second request's handler the same three cameras in a different
physical order.

No counters, no adversarial fake, no new test double — `InMemoryCameraQuerySource`
already takes a `List<Camera>` and LINQ-to-Objects `OrderBy` is stable, so the
physical order *is* the tie order. The test states the licence in the only place
it can be stated: the input.

This still exercises the real code path. `SortBy` is the production method, the
handler is the production handler, `Skip`/`Take` are the production paging, and
the assertion is on the rows the caller receives. What the test supplies is the
one thing the unit environment otherwise silently removes: a database's freedom
to order ties.

### Page boundary without touching production numbers

`limit: 2` over three cameras. The limit is a **query parameter the caller
chooses** — `ListCamerasQuery.Limit`, validated against `MaximumLimit = 200` and
well inside it. Nothing in production code moves. Seeding 201 cameras to reach
the default boundary would test the same `Skip(offset).Take(limit)` more slowly.

## Changes

**Production — one method, four arms:**

`src/CameraCatalog/Application/Queries/Handlers/ListCamerasQueryHandler.cs`
— append `.ThenBy(camera => camera.Id)` to each arm of `SortBy`, and replace the
two comments that claim `Fab` breaks the tie, because it does not.

`CameraIdentifier` is `IComparable<CameraIdentifier>` (`CameraIdentifier.cs:11,24`)
so it orders in memory, and is mapped `camera_id`/`uuid` through a plain value
converter (`CameraConfiguration.cs:37-38`) so it translates to
`ORDER BY camera_id` exactly as `Fab` already translates to `ORDER BY fab`.

**Tests:**

`tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs`
— two facts, one per sort field.

## Risks

- **EF translation.** The one thing the unit test cannot prove. A non-translatable
  `OrderBy` throws at runtime, not at build. Phase 5 answers this against real
  Postgres.
- **Query cost.** An added `ORDER BY` column can change a plan. Assessed in
  `verification.md`; the expectation is none, because `includeRetired=true`
  cannot use the partial index anyway and the sort is already a full sort.
- **Existing order assertions.** Any test asserting an exact order where two rows
  tie would now be asserting a different order. The suite says whether there are
  any.
