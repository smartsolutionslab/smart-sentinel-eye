# Plan — Spec 199, the chunk a failed archive keeps

**Feature:** #2425 · **Spec:** `spec.md` · **Phase-4a colour:** RED

## Context and layers

One bounded context, one layer. **AuditObservability / Infrastructure.**

| File | Change |
|---|---|
| `src/AuditObservability/Infrastructure/Persistence/TimescaleAuditChunkInventory.cs` | `DropChunkAsync` — the SQL, the read, the log call, the comment |
| `src/AuditObservability/Infrastructure/Log.cs` | Replace `DroppedChunks`; add the over-drop and no-op lines |
| `docs/runbooks/audit-observability.md` | The retry claim at `:114` is currently false; it becomes true |
| `tests/Integration.Tests/AuditObservability/DropChunkIdentityIntegrationTests.cs` | **New.** The regression test (phase 4a) |

**`FakeAuditChunkInventory` must not be changed.** It already drops by
identity — the intended contract — which is why no unit test could catch this
(`spec.md` §*Why no existing test could have caught this*). After the fix it
becomes an accurate model rather than an optimistic one. Rewriting it to drop
by range would encode the bug as the safety net, and `tasks.md` states it as a
prohibition rather than leaving it to judgement.

**Not touched:** `AuditChunk` (`Application/Retention/IAuditChunkInventory.cs:20`)
keeps its three fields; `IAuditChunkInventory` keeps its signature;
`AuditRetentionHostedService` is not modified at all; no `Shared.Contracts`
change, so no integration-event version bump and no consumer migration. The
no-cross-context-reference rule is not engaged — nothing crosses a context.

Domain and Application are untouched, so the §II primitive-boundary rule and
the `Option<T>` preference (ADR-0141) have no new surface here.

## The design

### 1. Bound the drop to the chunk's own range

```csharp
IReadOnlyList<DroppedChunkRow> dropped = await context.Database
    .SqlQuery<DroppedChunkRow>(
        $"""
        SELECT drop_chunks(
            'audit_events',
            older_than => {chunk.OccurredUntil},
            newer_than => {chunk.OccurredFrom})::text AS "ChunkName"
        """)
    .ToListAsync(cancellationToken);
```

with

```csharp
private sealed record DroppedChunkRow(string ChunkName);
```

`chunk.OccurredFrom` and `chunk.OccurredUntil` are `range_start` and
`range_end` read straight from `timescaledb_information.chunks`
(`TimescaleAuditChunkInventory.cs:43-46`), so the window is **exactly** the
chunk's own half-open interval. No new field, no new lookup, no change to the
record that carries them.

**Bounds, verified rather than reasoned about** (`spec.md` §*Verified
behaviour*). The intersection is `range_start >= newer_than AND range_end <=
older_than`:

- `newer_than => chunk.OccurredFrom` is **inclusive** and tests `range_start`,
  so the target chunk is in.
- The previous chunk shares the boundary instant (`A.range_end ==
  B.range_start`) but its `range_start` is strictly earlier, so it is out.
  This was probed against genuinely adjacent chunks, which is the only way the
  off-by-one shows.
- `older_than => chunk.OccurredUntil` keeps its existing meaning and its
  existing value. The change is purely **additive**: one more bound on a call
  that already had one.

Both bounds are `timestamptz` **parameters**, not interpolated text — the
`$"""..."""` form in EF Core's `SqlQuery` parameterises them, exactly as the
existing `{boundary}` at `:38` already is.

### 2. Why not `DROP TABLE <chunk_schema>.<chunk_name>`

The issue offers this as the alternative and it was investigated, not waved
off. A controlled A/B on the pinned image showed **both** paths leave the
catalog clean — TimescaleDB's DDL event triggers pick up a direct `DROP TABLE`
and remove the `_timescaledb_catalog.chunk` rows and the `compress_hyper_*`
companion. So "it corrupts the catalog" is **not** a true objection and is not
the reason to reject it. Four real ones are:

1. **ADR-0101 already decided the API.** `docs/adr/0101-timescaledb-for-audit.md:56-59`
   commits to *"`add_retention_policy()` … called by our own retention worker
   after the per-chunk export … (we invoke `drop_chunks()` **directly**)"*.
   Swapping to raw DDL on an internal chunk table is a decision ADR-0101 made
   the other way, and the autonomous lane may not write an ADR (ADR-0144).
2. **It is an injection surface, and the window is not.** Table and schema
   names cannot be SQL parameters. `DROP TABLE` needs identifier interpolation
   — `quote_ident`, or `format('%I.%I')` — and would be the only string-built
   SQL in the context. The windowed call passes two typed parameters and builds
   no identifier at all. For a fix whose entire purpose is preventing
   unintended deletion, choosing the variant that concatenates a name into a
   `DROP` statement is the wrong direction.
3. **It leaks Timescale internals into Application.** `AuditChunk`
   (`Application/Retention/IAuditChunkInventory.cs:20`) carries an identifier
   and two instants. `DROP TABLE` needs `chunk_schema` and `chunk_name` carried
   through the Application-layer record — raw physical table names crossing a
   layer boundary for no gain. The windowed call uses fields the record
   **already has**.
4. **`drop_chunks` returns the evidence; `DROP TABLE` returns nothing.** One
   row per dropped chunk is what makes design point 3 possible. A `DROP TABLE`
   would leave the observability hole exactly as wide as it is now.

`drop_chunks` with both bounds is therefore the design. It was confirmed to
work on **compressed** chunks, on the **oldest** chunk, and across a **gap** —
the three production conditions that could have made the supported API the
wrong choice.

### 3. Close the observability hole

The `-1` is not a cosmetic defect: it is why this bug survived. `drop_chunks`
returns a **result set**, so `ExecuteNonQuery` — which does not read result
sets — can never report anything about it. Reading the rows fixes the cause,
not the symptom.

```csharp
if (dropped.Count == 1)
{
    logger.DroppedChunk(chunk.ChunkIdentifier, dropped[0].ChunkName);
}
else if (dropped.Count == 0)
{
    logger.ChunkAlreadyDropped(chunk.ChunkIdentifier, chunk.OccurredFrom, chunk.OccurredUntil);
}
else
{
    logger.DroppedMoreThanOneChunk(
        chunk.ChunkIdentifier, dropped.Count, string.Join(", ", dropped.Select(row => row.ChunkName)));
}
```

Three `[LoggerMessage]` entries (ADR-0050), replacing `DroppedChunks`:

| Level | When | Why that level |
|---|---|---|
| `Information` | exactly one dropped | the normal case, and it now **names** the chunk |
| `Warning` | zero dropped | verified to be a legal, non-error outcome (0 rows, no exception). Benign — the chunk was already gone — but worth a line |
| **`Error`** | more than one dropped | the invariant this spec establishes has broken. This is the line that would have caught #2425 on day one |

`string.Join` over a list that is one element in every real case is cheap and
only evaluated on the error branch.

**The `Error` branch does not throw.** By the time the rows come back the drop
has already committed; throwing cannot undo it, and
`AuditRetentionHostedService`'s `catch` (`:174-180`) would merely log it and
move on — swallowing it into the same shape of silence. A loud log is the
honest maximum. This is deliberate, and is stated so the next reader does not
read it as a missed guard.

### 4. Correct the comment that asserted the opposite

`TimescaleAuditChunkInventory.cs:56-58` currently states the false conclusion
that caused the bug. Replacing it with a true one is part of the fix, not a
drive-by: a comment that has already misled one reader will mislead the next.
It must state what the two bounds do, and that `older_than` **alone** drops
every older chunk — the fact whose absence produced #2425.

### 5. Correct the runbook

`docs/runbooks/audit-observability.md:114` tells an operator the next sweep
retries a chunk whose archive failed. That is false today and true after the
fix. It stays as it is in wording, but the section gains a line recording that
before this fix the retry could not happen — an operator reading old incident
notes needs to know which side of the fix they were on.

## Noted, not fixed — the publish-before-drop ordering

`AuditRetentionHostedService.cs:136-160` publishes `AuditChunkArchivedV1` and
commits the outbox **before** calling `DropChunkAsync`. If the drop then fails,
the chunk is announced but still present, and the next sweep lists it, archives
it again (ETag-idempotent, so no second upload) and announces it again — a
duplicate downstream event, not data loss.

The issue raises this and explicitly asks that it be **noted, not fixed here**.
It is out of scope by that instruction and by the smallest-change rule
(ADR-0036): a data-loss fix and a messaging-ordering change do not belong in
one diff, and reordering the publish after the drop trades this duplicate for
the opposite failure (a chunk dropped whose archive was never announced), which
is a worse trade and needs its own argument. **Carried into the PR body as a
one-line note.**

## The regression test

### Where it goes

`tests/Integration.Tests/AuditObservability/` — namespace
`SmartSentinelEye.Integration.Tests.AuditObservability`, collection
`[Collection(AspireCollection.Name)]`, primary-constructor injection of
`AspireFixture`. The project already references
`SmartSentinelEye.AuditObservability.Infrastructure`, so
`TimescaleAuditChunkInventory`, `AuditObservabilityDbContext` and `AuditChunk`
are all in scope with no csproj change.

`RetentionRoundtripIntegrationTests.cs` in the same folder is the pattern to
copy: it already seeds backdated rows with
`SeedBackdatedRowAsync(DateTimeOffset)` via raw `INSERT`, and already reads
`timescaledb_information.chunks` through `SqlQuery`. Reuse that shape rather
than inventing a second one.

`TimescaleAuditChunkInventory` needs an
`IDbContextFactory<AuditObservabilityDbContext>`, and the fixture exposes a
context, not a factory (`AspireFixture.Db.cs`:
`CreateAuditObservabilityDbContextAsync`). A small test-local factory over
`App.GetConnectionStringAsync("audit-db")` closes that gap; it is four lines
and belongs in the test file, not in the fixture.

### Why the regression test does not drive the sweep

Three findings make a sweep-driven test the wrong instrument, and each is
independently disqualifying:

1. **There is no integration seam that fails an archive.** `MinioOptions`
   exposes only `Bucket` and `ObjectKeyTemplate`; nothing in
   `tests/Integration.Tests` injects a fault or issues an Aspire resource
   command; the archiver is registered unconditionally
   (`AuditObservabilityInfrastructureModule.cs:87-91`). Building one would mean
   a new AppHost E2E switch — a new production-visible seam, for a test whose
   subject does not need it.
2. **The worker runs out of process.** The Aspire fixture boots
   `audit-observability` as its own process, so the test has no
   `IServiceProvider` for it and cannot call `RunOnceAsync`. The existing
   integration test drives the sweep *passively* via the 3-second tick the
   AppHost sets for E2E (`AppHost.cs:539`) and then polls.
3. **Constructing the worker in-process would be destructive.**
   `ListChunksOlderThanAsync` has only an upper bound, so a test-owned worker
   with a shortened `RetentionWindow` would list, archive and drop **every**
   chunk past that boundary — including chunks other tests seeded. A
   regression test for data loss must not cause any.

The defect is wholly inside `DropChunkAsync`, and the sweep's half is already
pinned by `Archiver_failure_leaves_the_chunk_in_place_for_next_sweep`. Testing
the method directly is both the tightest expression of the bug and the only
non-destructive one.

### The seeding recipe, and why these instants

Seed two rows at **`now - 85 days`** and **`now - 55 days`** — exactly one
chunk interval apart. Every number here is load-bearing:

- **Exactly 30 days apart** puts them in **consecutive** chunk slots, which are
  contiguous by construction (`older.range_end == newer.range_start`). That
  adjacency is the whole point: it is the shared-boundary instant where the
  `newer_than` off-by-one would show. Instants merely "far apart" can leave a
  gap, and a gap makes the test pass for a weaker reason. The interval is
  `INTERVAL '1 month'`
  (`20260529124335_InitialAuditObservability.cs:76-78`), observed live to
  resolve to **30-day** spans.
- **Both stay inside the 90-day retention window.** The older chunk's
  `range_end` falls in `(now-85d, now-55d]`, comfortably newer than
  `now - 90 days`. So the **ambient production worker — which sweeps every 3
  seconds in the integration run — never lists either chunk**, and cannot race
  the test. This is why the instants are not simply "well past the boundary".
- **Nothing aged past the boundary is left behind.**
  `RetentionRoundtripIntegrationTests` asserts
  `CountChunksPastBoundaryAsync().ShouldBe(0)` over the **whole hypertable**.
  A test that parked an un-archived aged chunk would break it depending on
  execution order. These instants cannot.

**The recipe was run, not reasoned about.** Two rows at `now() - INTERVAL '85
days'` and `now() - INTERVAL '55 days'` on the pinned image, against a
hypertable built from the real migration:

```
     chunk_name     |      range_start       |       range_end        | inside_retention_window
--------------------+------------------------+------------------------+------------------------
 _hyper_14_27_chunk | 2026-06-06 00:00:00+00 | 2026-07-06 00:00:00+00 | t
 _hyper_14_28_chunk | 2026-07-06 00:00:00+00 | 2026-08-05 00:00:00+00 | t

 contiguous
------------
 t
```

Two distinct chunks; `older.range_end == newer.range_start` exactly; both
`range_end > now() - INTERVAL '90 days'`, so the production worker's own query
does not list either.

Seed with a bespoke `event_kind` (as `RetentionSeedV1` already does) so the
rows are identifiable: there is **no** `ResetAuditObservabilityAsync` on the
fixture and no cleanup anywhere in this folder — isolation in this context is
by unique data, not by truncation.

`RetentionRoundtripIntegrationTests.cs:69` is the assertion at risk —
`(await CountChunksPastBoundaryAsync()).ShouldBe(0)`, unqualified across the
whole hypertable — and it seeds at ages `[200, 120]`
(`:41`). These instants stay clear of it.

### Risks

- **R1 — the red is green for the wrong reason.** If both rows land in one
  chunk, nothing can be over-dropped and the test passes on today's code,
  reading as "already fixed". Mitigation: the test **asserts its own premise**
  before acting — read `timescaledb_information.chunks`, require **two
  distinct chunks**, and require them **contiguous**
  (`older.RangeEnd == newer.RangeStart`), failing with a message that says
  which half broke. Not a comment, an assertion.
- **R2 — the assertion checks its own input.** Asserting "the older chunk
  survives" against the list the test itself built proves nothing. Mitigation:
  re-read `timescaledb_information.chunks` **after** the drop, and separately
  count the older chunk's rows in `audit_events`. Neither is written by the
  test after seeding, so the subject can change without the assertion text
  changing.
- **R3 — shared-database bleed.** Other tests' backdated rows may add chunks.
  Mitigation: assert on **the specific chunk names** captured before the drop,
  never on a total chunk count.
- **R4 — the drop is real and irreversible.** The test genuinely drops a chunk
  from the shared hypertable. That is acceptable only because it drops **its
  own** seeded chunk, identified by name. It must never call `DropChunkAsync`
  with bounds it did not read back from the chunk view.
- **R5 — compression.** Production chunks in the 30–90 day band are compressed
  before the sweep reaches them. The fix was verified against compressed
  chunks in the live probe; the integration test does not compress, and
  phase 5 must say which state it observed rather than implying both.
- **R6 — `drop_chunks` argument typing.** The function is polymorphic; an
  untyped parameter can produce *"function drop_chunks(unknown, unknown) is not
  unique"*. The existing single-bound call already passes a `DateTimeOffset`
  through Npgsql as `timestamptz`, and the second bound is the same CLR type on
  the same path. Low risk, but it is the first thing to check if 4b hits a
  Postgres error rather than a test failure.
- **R7 — one machine, one Aspire stack.** The integration run needs the
  fixture and nothing else may be booted against it concurrently.
- **R8 — reading the log line.** `"audit-observability"` is **not** in
  `AspireFixture.TailedResources`, so `RecentLogs("audit-observability")`
  returns a placeholder and `LogTailCoverageTests` enforces that. The test
  therefore asserts on **database state, not on log output**; the SC-5 log line
  is read by hand at phase 5 instead. Adding the resource to `TailedResources`
  to satisfy an assertion would widen this slice for no gain.

## Verification

Phase 4a runs the new integration test against the **real Aspire fixture**
(ADR-0103) and returns the **verbatim** failing output: on today's tree the
older, un-archived chunk is gone. That output is the phase-4 gate artefact and
is quoted in the PR (ADR-0139, ADR-0144). Phase 4b may not edit those
assertions — if an assertion has to move to pass, that is evidence the change
did more than intended; stop and report.

Phase 5 records: the suite red → green with the 4a output quoted; the
`DroppedChunk` log line read verbatim from the running stack, naming a chunk
and carrying no `-1`; and the direct `drop_chunks` observation from `spec.md`
§*Independent end-to-end test procedure* item 2 repeated against the stack's
own Postgres rather than a throwaway container. Latency: **N/A**, stated
explicitly, with the reason (nightly background sweep, not on any §IV leg).

Gates before the PR: `dotnet format --verify-no-changes`, a Release build
(analyzers are errors there), the AuditObservability unit suites, and the new
integration test. Coverage gates (ADR-0065) are unaffected — the change is in
Infrastructure, which carries no threshold.

**One machine, one Aspire stack.** The integration run needs the fixture and
nothing else may be booted against it concurrently.

## Roles

| Phase | Agent | Why |
|---|---|---|
| 4a | `test-writer` | Integration test against the Aspire fixture; needs the TimescaleDB chunk-seeding and archive-failure seams |
| 4b | `backend-engineer` | C#/EF Core raw SQL, `[LoggerMessage]` source-gen, one Infrastructure file |
| 6 | `backend-reviewer` | DDD/boundary correctness, EF usage, guards, test hygiene |
| 6 | `infra-reviewer` **(also)** | see below |

**`infra-reviewer`: yes — added deliberately.** This repository's pattern for
data-integrity-critical fixes is a second review dimension, and the two
dimensions here are genuinely different. `backend-reviewer` reads the C#.
Nothing in the C# is hard; **the whole defect is in the SQL semantics**, and a
review that reads the C# and takes the SQL on trust is exactly the review that
passed this code the first time.

`infra-reviewer` holds the Postgres/TimescaleDB and Aspire-fixture expertise
and is asked three specific questions:

1. **The bounds.** With `newer_than => chunk.OccurredFrom` and `older_than =>
   chunk.OccurredUntil`, is there any chunk other than this one that can fall
   inside the window — under a changed `chunk_time_interval`, a re-chunked
   hypertable, a compressed chunk, or a multi-dimensional hypertable?
2. **The evidence.** Does the phase-4a red actually prove the defect, or could
   it pass on today's code for an unrelated reason (R1, R3)? Does the test
   assert its own premise that two distinct chunks exist?
3. **The blast radius.** Does anything else in the repository call
   `drop_chunks` — or any other windowed Timescale function — with a single
   bound and the same mistaken assumption?

Question 3 is the one that most needs asking: this spec fixes the call site the
issue names, and nobody has yet checked whether it is the only one.
