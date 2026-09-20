# Spec 199 — The chunk a failed archive keeps

**Issue:** #2425 — *AuditObservability's retention sweep drops chunks it never archived — `drop_chunks(older_than)` is bounded by the window, not by the one chunk that was just archived*
**Branch:** `2425-drop-chunk-by-identity`
**Phase-4a colour:** **RED** (behaviour-changing), discharged by a **real counterfactual against a real TimescaleDB instance**
**ADRs:** ADR-0101 (TimescaleDB for audit — and the source of "we invoke `drop_chunks()` directly"), ADR-0103 (integration tests against the Aspire fixture, no Testcontainers), ADR-0050 (`ILogger<T>` + `[LoggerMessage]` source-gen), ADR-0105 (`Ensure.That`), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases), ADR-0109 (parallel markers)
**Severity:** **Silent, permanent data loss in production.** Audit rows that were never written to MinIO are irrecoverably dropped from the hypertable, and nothing in the log distinguishes that from a correct sweep.

## Problem

`src/AuditObservability/Infrastructure/Persistence/TimescaleAuditChunkInventory.cs:56-63`:

```csharp
// `drop_chunks` accepts a window and drops every chunk whose
// range_end <= older_than. Passing the chunk's exact end
// bounds the drop to one chunk per call.
int count = await context.Database.ExecuteSqlInterpolatedAsync(
    $"""
    SELECT drop_chunks('audit_events', older_than => {chunk.OccurredUntil})
    """,
    cancellationToken);
```

The comment's first sentence is correct. Its second does not follow from it and
is false: the call is bounded by the *instant* `chunk.OccurredUntil`, not by
"this chunk". Every chunk whose `range_end` is at or before that instant goes
with it.

### The failure, end to end

`AuditRetentionHostedService.RunOnceAsync` lists chunks oldest-first
(`TimescaleAuditChunkInventory.cs:39`, `ORDER BY range_end ASC`) and loops one
chunk at a time (`AuditRetentionHostedService.cs:90-110`). A failed archive
**deliberately** leaves its chunk in place to be retried
(`AuditRetentionHostedService.cs:174-180`: *"Leave the chunk in place; next
sweep retries."*).

1. Chunk **A** — MinIO briefly unreachable. `ArchiveChunkAsync` throws, the
   `catch` logs and continues. A is **not** archived and **not** dropped. This
   is the designed, correct behaviour.
2. Chunk **B**, the next iteration — MinIO is back. B archives, the event
   publishes, the outbox commits, and `DropChunkAsync(B)` runs
   `drop_chunks(older_than => B.range_end)`.
3. That call drops **A and B**. A's rows are gone from the hypertable and were
   never written to MinIO.

The loss is permanent, and it lands on the one record the system exists to
keep. It is worst precisely when things are already going wrong, because a
MinIO outage is what produces the un-archived chunk in the first place — and an
outage spanning several chunks loses all of them the moment the first one
succeeds.

### Three places already promise the opposite

- The code's own comment (`TimescaleAuditChunkInventory.cs:56-58`).
- `specs/009-audit-observability/spec.md:149` — *"Calls TimescaleDB's
  `drop_chunks()` on **that one chunk**."*
- `docs/runbooks/audit-observability.md:114` — tells an operator that after
  fixing the outage *"the next nightly sweep retries the same chunk; archiver
  is idempotent"*. It cannot. The chunk no longer exists, so the next sweep
  does not list it, does not retry it, and reports nothing wrong.

### Why nobody saw it

`ExecuteSqlInterpolatedAsync` maps to `ExecuteNonQuery`, which does not read a
result set at all. `drop_chunks` is a set-returning function: it answers with
one **row per dropped chunk** (verified live — see below), so there is no row
count for `ExecuteNonQuery` to return and Npgsql yields `-1`.

`logger.DroppedChunks(chunk.OccurredUntil, count)` then logs that `-1` through
a message that reads (`src/AuditObservability/Infrastructure/Log.cs:15`):

> `Dropped TimescaleDB chunks for AuditObservability up to {Until} (procedure rows: {Count}).`

So the operator sees `procedure rows: -1` whether one chunk was dropped or
eleven. **There is no observable difference between a correct sweep and a
catastrophic one.** The count is not merely wrong, it is structurally incapable
of being right.

## Verified behaviour — live, not inferred

Run against **`timescale/timescaledb:2.27.1-pg17`**, the exact image
`src/AppHost/AppHost.cs:77-78` pins, on a hypertable built to match
`20260529124335_InitialAuditObservability.cs` (`occurred_at timestamptz`,
`chunk_time_interval => INTERVAL '1 month'`, `timescaledb.compress` set). Three
chunks were created and the production query from
`TimescaleAuditChunkInventory.cs:31-40` was used to read their bounds:

```
    chunk_name    |      range_start       |       range_end
------------------+------------------------+------------------------
 _hyper_1_1_chunk | 2025-01-12 00:00:00+00 | 2025-02-11 00:00:00+00   <- A
 _hyper_1_2_chunk | 2025-02-11 00:00:00+00 | 2025-03-13 00:00:00+00   <- B
 _hyper_1_3_chunk | 2025-03-13 00:00:00+00 | 2025-04-12 00:00:00+00
```

**Today's call**, with B's `range_end`:

```sql
SELECT drop_chunks('audit_events', older_than => TIMESTAMPTZ '2025-03-13T00:00:00Z');
```

```
              drop_chunks
----------------------------------------
 _timescaledb_internal._hyper_1_1_chunk
 _timescaledb_internal._hyper_1_2_chunk
(2 rows)
```

**Two chunks dropped. Chunk A's row was gone.** The bug is confirmed directly,
not taken on the issue's word or on documentation.

**The windowed call**, with B's own bounds:

```sql
SELECT drop_chunks('audit_events',
                   older_than => TIMESTAMPTZ '2025-03-13T00:00:00Z',
                   newer_than => TIMESTAMPTZ '2025-02-11T00:00:00Z');
```

```
              drop_chunks
----------------------------------------
 _timescaledb_internal._hyper_3_5_chunk
(1 row)
```

**Exactly one chunk. A survived, and so did the chunk after B.**

### The exact predicate, probed rather than assumed

`older_than` and `newer_than` intersect as
**`range_start >= newer_than AND range_end <= older_than`**. Both ends were
probed, because an off-by-one here either drops nothing or still takes a
neighbour:

- **`newer_than` is inclusive on `range_start`, and tests `range_start`, not
  `range_end`.** With `newer_than` set to chunk B's `range_end`
  (`2025-03-13`), **B was not dropped** — so the predicate is not
  `range_end > newer_than`. With `newer_than` set to B's own `range_start`, B
  **was** dropped — so it is `>=`, inclusive.
- **Chunk A survives even though `A.range_end == B.range_start` exactly.**
  Timescale chunk ranges are contiguous half-open intervals, so the adjacent
  chunk shares the boundary instant. Passing `newer_than => B.range_start`
  excludes A because A's `range_start` is strictly earlier. This is the
  off-by-one that matters most, and it was verified with adjacent chunks
  sharing a boundary, not with a convenient gap between them.

### Four further conditions that hold in production were checked

| Condition | Result |
|---|---|
| **Compressed chunks** (the 30-day policy compresses everything the 90-day sweep later touches) | Windowed `drop_chunks` drops a compressed chunk correctly and removes its `compress_hyper_*` companion table |
| **The oldest chunk** (no predecessor) | Dropped correctly; `newer_than` below every chunk is not an error |
| **Non-contiguous chunks** (a gap where no audit rows exist) | Window is keyed on the chunk's own bounds, so a gap changes nothing |
| **Zero-drop** (window names a chunk that is already gone) | Returns **0 rows** — not an error. So the returned row count is a genuine signal, and "exactly one" is a real assertion |

## Locked tech choices

TimescaleDB 2.27.1-pg17 via the Aspire-composed Postgres resource (ADR-0101,
`AppHost.cs:75-78`); EF Core against `AuditObservabilityDbContext`; raw SQL
through `Database.SqlQuery<T>` / `ExecuteSqlInterpolatedAsync` exactly as the
file already does; `[LoggerMessage]` source-gen for the log change (ADR-0050);
xUnit + Shouldly + hand-written fakes, integration against the real Aspire
fixture (ADR-0052, ADR-0054, ADR-0103 — no Testcontainers). **No new
dependency, no new abstraction, no change to `AuditChunk`'s shape or to
`IAuditChunkInventory`'s signature.**

## User story

### US1 (P1) — A chunk that was never archived is never dropped

*As* a compliance officer for a fab,
*I want* the nightly retention sweep to drop only chunks it has just archived
to MinIO,
*so that* an outage during the sweep costs me a delay, not the audit record.

This is the whole slice: one production file, one SQL statement, one log
message, and the regression test that proves it. It is independently shippable
and independently observable.

## Acceptance scenarios

**SC-1 (the defect — the un-archived chunk survives)**

```gherkin
Given the audit hypertable holds two chunks past the retention boundary
  And chunk A is older than chunk B
  And archiving chunk A fails
When the retention sweep runs once
Then chunk B is dropped from the hypertable
  And chunk A is still present in timescaledb_information.chunks
  And chunk A's audit rows are still readable from audit_events
```

**SC-2 (the retry the runbook promises is real)**

```gherkin
Given chunk A survived a sweep in which its archive failed
  And the cause of that failure is resolved
When the retention sweep runs again
Then chunk A is listed, archived to MinIO, and dropped
```

SC-2 is the scenario `docs/runbooks/audit-observability.md:114` already
describes to operators and that today's code makes impossible. It is the
reason the fix is not merely "drop fewer chunks". It follows from SC-1 rather
than being separately mechanised: once chunk A survives, the next sweep lists
it by the same `ORDER BY range_end ASC` query that listed it the first time.

**SC-3 (control — the ordinary sweep still drops what it archived)**

```gherkin
Given the audit hypertable holds two chunks past the retention boundary
  And both archive successfully
When the retention sweep runs once
Then both chunks are dropped
  And no chunk newer than the retention boundary is dropped
```

**SC-4 (control — a chunk inside the retention window is untouched)**

```gherkin
Given the audit hypertable holds a chunk whose range_end is after the boundary
When the retention sweep runs once
Then that chunk is still present
```

**SC-5 (observability — the log states what was actually dropped)**

```gherkin
Given a chunk is dropped by the sweep
When the drop completes
Then the log names the chunk that was dropped
  And it never reports "-1" as a count
```

**SC-6 (observability — an over-drop would be loud)**

```gherkin
Given a drop call returns more than one chunk name
Then an error-level line is logged naming every chunk dropped
```

SC-6 guards the class rather than the instance. With the window bounded by one
chunk's own half-open range, no second chunk can fall inside it — so if one
ever does, the invariant this spec establishes has broken and that must not be
silent a second time. This is the direct answer to *"why nobody saw it"*.

**SC-7 (idempotence — a drop that finds nothing is not a failure)**

```gherkin
Given the chunk named by the window has already been dropped
When DropChunkAsync runs for it
Then zero rows are returned
  And a warning is logged
  And no exception is thrown
```

**SC-8 (auth / trust boundary)**

There is no HTTP endpoint, no caller-supplied input and no scope check on this
path: the sweep is a `BackgroundService` and its only input is the chunk list
it just read from the database it is about to write to. The usual
bad-request/401/403 scenarios do not apply. The trust-relevant boundary here is
**durability of the audit record**, and SC-1, SC-2 and SC-6 are its scenarios.

The one injection-shaped question this change *could* raise is answered by the
chosen design rather than tested around: the fix passes two `timestamptz`
**parameters** and interpolates no identifier. See `plan.md`
§*Why not `DROP TABLE`*.

## Why no existing test could have caught this

Worth stating, because it shapes where the regression test goes.

`DropChunkAsync` has **no test of any kind** — not a unit test, not an
integration test. And the Application-layer fake cannot express the defect:
`FakeAuditChunkInventory.DropChunkAsync`
(`tests/AuditObservability.Application.Tests/Fakes/FakeAuditChunkArchiver.cs`)
drops **by identity**:

```csharp
_chunks.RemoveAll(c => c.ChunkIdentifier == chunk.ChunkIdentifier);
```

That is the *intended* contract, faithfully modelled. Every one of the nine
`AuditRetentionHostedServiceTests` — including
`Archiver_failure_leaves_the_chunk_in_place_for_next_sweep` — runs against it
and passes, and would have passed no matter how wrong the SQL was. **The seam
that made the sweep testable is the seam that hid the bug**, and the fake is
not at fault: it encodes what production was supposed to do.

The consequence for this slice: **the fake must not be changed.** After the fix
production *does* drop by identity, so the fake becomes accurate rather than
optimistic. Rewriting it to drop by range would encode the bug as the safety
net. The regression must live where the untested code is — against real
TimescaleDB.

## Independent end-to-end test procedure

The whole defect is TimescaleDB chunk behaviour, so it cannot be observed
against an in-memory provider or a fake inventory. Verification is against the
real stack (ADR-0103, no Testcontainers):

1. **Primary (mechanised, and the phase-4a red).** A new integration test in
   `tests/Integration.Tests/AuditObservability/` boots the Aspire fixture,
   seeds audit rows at two backdated `occurred_at` instants **exactly one
   chunk interval apart** so they land in adjacent, contiguous chunks, reads
   the real chunk bounds back from `timescaledb_information.chunks`, and calls
   `TimescaleAuditChunkInventory.DropChunkAsync` for the **newer** chunk only.
   It asserts the **older** chunk is still listed and its rows are still
   readable. On today's tree this fails — the older chunk is gone. The failing
   output is quoted in the PR (ADR-0139, ADR-0144).

   **This tests `DropChunkAsync` directly rather than driving the sweep, and
   that is deliberate.** The defect is entirely inside that method; the sweep's
   contribution (an archive failure leaves its chunk in place for the next
   iteration to destroy) is already pinned by
   `Archiver_failure_leaves_the_chunk_in_place_for_next_sweep`. SC-1 is
   therefore discharged by the two together: the unit test proves the sweep
   leaves chunk A behind, the integration test proves dropping chunk B no
   longer takes A with it.

   Driving the real sweep instead was considered and **rejected as destructive**
   — see `plan.md` §*Why the regression test does not drive the sweep*.

2. **Secondary (observed by hand, against the running stack).** Boot the Aspire
   AppHost, and in the Postgres resource:

   ```sql
   -- three aged chunks
   SELECT chunk_name, range_start, range_end
     FROM timescaledb_information.chunks
    WHERE hypertable_name = 'audit_events'
    ORDER BY range_end;
   ```

   Then confirm against the running database that the *fixed* statement removes
   one chunk and the *old* statement removes more, using the middle chunk's
   bounds. This is the observation recorded in §*Verified behaviour* above; it
   is repeated at phase 5 against the stack the code actually runs on rather
   than a throwaway container.

3. **The log line is read, not assumed.** Phase 5 quotes the actual emitted
   `DroppedChunks` line and confirms it names a chunk and carries no `-1`.

## Latency budget

**N/A.** This path is a nightly `BackgroundService` sweep in
AuditObservability. It is not on the event-to-overlay path and touches none of
constitution §IV's six legs. No leg is affected, no re-measurement is required,
and none is claimed.

## Out of scope

- **The publish-before-drop ordering.** `AuditRetentionHostedService.cs:136-160`
  publishes `AuditChunkArchivedV1` and commits the outbox **before**
  `DropChunkAsync`. A drop that fails after a successful archive leaves an
  announced-but-undropped chunk, which the next sweep announces again. The
  issue states this is not data loss (the archiver is ETag-idempotent) and
  explicitly asks that it be **noted, not fixed here**. It is noted in
  `plan.md` §*Noted, not fixed* and will be carried in the PR body. Folding it
  in would mix a data-loss fix with a messaging-ordering change in one diff,
  against the smallest-change rule (ADR-0036).
- **Replacing the whole retention worker with `add_retention_policy()`.**
  ADR-0101 decided against it deliberately: the export must succeed before the
  drop, which a Timescale background policy cannot sequence.
- **Recovering rows already lost to this bug.** They are gone; nothing in this
  slice can bring them back. Whether any production fab has lost rows is an
  operational question for the issue, not a code change.
- **Changing `AuditChunk`, `IAuditChunkInventory`'s signature, or the archiver.**

## File contention

**None.** `gh pr list --state open` returns **no open PRs** on the repository at
time of writing. No other branch, worktree or in-flight spec touches
`src/AuditObservability/Infrastructure/Persistence/TimescaleAuditChunkInventory.cs`
or `src/AuditObservability/Application/Retention/AuditRetentionHostedService.cs`
— checked across every remote branch and both local worktrees
(`D:/Github/smart-sentinel-eye` on `develop`, `D:/Github/sse-2425` on this
branch).

Spec numbering: **199** is free. `origin/develop` holds up to `198`, and a
sweep of every remote branch for `specs/19[5-9]` and `specs/2[0-9][0-9]` found
no branch claiming `199`.

**Board:** #2425 is on Project #13 with status **Todo**, verified with
`gh project item-list 13 --owner smartsolutionslab --limit 2000` filtered by
`content.url`.
