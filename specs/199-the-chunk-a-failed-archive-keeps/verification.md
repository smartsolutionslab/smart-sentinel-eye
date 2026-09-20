# Verification — Spec 199, the chunk a failed archive keeps (#2425)

## (a) Red → green, T006's verbatim output

Phase 4a (`test-writer`, commit `ace1742c`) ran
`DropChunkIdentityIntegrationTests.Dropping_a_chunk_leaves_the_older_unarchived_chunk_in_place`
against the real Aspire fixture on unpatched code and reported this failure
verbatim:

```
Failed ...Dropping_a_chunk_leaves_the_older_unarchived_chunk_in_place [359 ms]
  Shouldly.ShouldAssertException : afterDrop.Select(row => row.ChunkName)
   should contain "_hyper_1_4_chunk" but was actually ["_hyper_1_3_chunk"]
  chunk A was never archived, so dropping chunk B must not take it with it — ...
```

After phase 4b's fix (commit `67890403`), the orchestrator independently
re-ran the same test — not trusting phase 4b's own report — against a freshly
booted Aspire/TimescaleDB stack:

```
[xUnit.net 00:00:00.51]   Starting:    SmartSentinelEye.Integration.Tests
  Passed SmartSentinelEye.Integration.Tests.AuditObservability.RetentionRoundtripIntegrationTests.Every_chunk_past_the_retention_boundary_is_archived_dropped_and_announced [8 s]
  Passed SmartSentinelEye.Integration.Tests.AuditObservability.DropChunkIdentityIntegrationTests.Dropping_a_chunk_leaves_the_older_unarchived_chunk_in_place [472 ms]
...
Test Run Successful.
Total tests: 2
     Passed: 2
 Total time: 2,2245 Minutes
```

`RetentionRoundtripIntegrationTests` — unmodified — stayed green throughout,
confirming the fix doesn't disturb the existing retention path.

(First attempt at this independent re-run failed on `MSB3027`/`MSB3021`: a
stale `testhost.exe` process from an earlier run held a file lock on
`xunit.abstractions.dll`. Confirmed as a build-lock artefact, not a test or
code failure, by the exact PID named in the MSBuild error; killed the process
and reran cleanly.)

## (b) The actual `DroppedChunk` log line, read from the running stack

From the same run's Aspire resource logs (`audit-observability` resource),
via the console logger attached to the test run rather than the dashboard —
`"audit-observability"` is not in `AspireFixture.TailedResources`, so this is
the log as the resource itself emitted it, captured by the test host:

```
info: SmartSentinelEye.AuditObservability.Infrastructure.Persistence.TimescaleAuditChunkInventory[662514334]
      Dropped audit chunk 8d3adb0b-b137-4718-3e08-402e4bd94df9 (_timescaledb_internal._hyper_1_1_chunk) from TimescaleDB.
info: SmartSentinelEye.AuditObservability.Application.Retention.AuditRetentionHostedService[852359904]
      Archived chunk 8d3adb0b-b137-4718-3e08-402e4bd94df9 (1 rows, already-archived=False) to fab=_unscoped/year=2026/month=02/chunk-8d3adb0bb13747183e08402e4bd94df9.ndjson.gz.
...
info: SmartSentinelEye.AuditObservability.Infrastructure.Persistence.TimescaleAuditChunkInventory[662514334]
      Dropped audit chunk ab13f279-e649-3326-dcd3-e8f49af4ac40 (_timescaledb_internal._hyper_1_2_chunk) from TimescaleDB.
```

Both lines name a real chunk (schema-qualified name, as `drop_chunks` itself
returns it) and carry no `-1` — the observability defect that made #2425
invisible is confirmed fixed, not just unit-tested.

## (c) Direct `drop_chunks` observation, against the stack's own Postgres

The log above **is** that observation: it is the production code path
(`TimescaleAuditChunkInventory.DropChunkAsync`, called from
`AuditRetentionHostedService`'s real nightly-sweep logic) running against
the fixture's own `audit-db` Postgres/TimescaleDB container, not a throwaway
container. Each call dropped exactly one chunk. This corroborates, on the
actual stack rather than a standalone probe, what `infra-reviewer`
separately verified live against `timescale/timescaledb:2.27.1-pg17` in
phase 6 (see below) — including the adjacent-chunk-boundary case, which the
architect's `spec.md` figures came from a throwaway container rather than
this stack.

## (d) Compressed chunks — explicitly not covered by this run

Both integration test runs above dropped **uncompressed** chunks — the
fixture's `audit_events` hypertable has no compression job old enough to
have run by the time the test seeds and drops. The compressed case (that
`drop_chunks` on a compressed chunk also cleans up its `compress_hyper_*`
companion via Timescale's own DDL event triggers) rests on the architect's
separate live probe recorded in `spec.md` §*Verified behaviour*, and was
independently re-confirmed by `infra-reviewer` in phase 6 against a
throwaway `timescale/timescaledb:2.27.1-pg17` container (compress → drop →
`orphan_compressed = 0`), not on this integration-suite run.

Latency: **N/A** — nightly background retention sweep, not on any
constitution §IV leg.

## Phase 6 — review findings and disposition

Two reviewers ran in parallel (`backend-reviewer`, `infra-reviewer`), per
`tasks.md` T013/T014. **Neither found a blocker.**

`infra-reviewer` booted its own real `timescale/timescaledb:2.27.1-pg17`
container and independently re-verified, live, all of the architect's SQL
semantics claims — including running the exact production statement through
EF Core against three adjacent chunks, reproducing both the original defect
and the fix through the real code path (not just prose):

- `newer_than => X` tests `range_start >= X` (confirmed inclusive).
- `older_than => Y` tests `range_end <= Y` (confirmed inclusive).
- The adjacent-boundary case (`A.range_end == B.range_start`) drops exactly
  the target chunk; confirmed for the oldest chunk, the newest chunk, and a
  compressed chunk.
- `TIMESTAMPTZ`/time-zone handling is safe (verified under a `Asia/Tokyo`
  session).
- `ExecuteSqlInterpolatedAsync` genuinely returns `-1` for the old
  set-returning call; `SqlQuery<DroppedChunkRow>` genuinely reads 0/1/2 rows
  correctly.
- No SQL-identifier interpolation — both bounds are bound parameters
  (`@p0`/`@p1`), confirmed from the emitted SQL.
- No other call site in the repo calls `drop_chunks`/`show_chunks` with the
  same single-bound assumption (the answer to `plan.md`'s Question 3).

**Should-fix items applied by the orchestrator before opening the PR**
(both reviewers independently converged on the first two, which is why they
were treated as load-bearing rather than optional):

1. **This verification note itself** — neither reviewer could find a
   phase-5 artefact at review time; it didn't exist yet.
2. **The replacement code comment overclaimed** ("only this chunk can ever
   match") without stating why. Restated to name the actual premise —
   `audit_events` has a single time dimension and chunk ranges never
   overlap — and to note that a space dimension would break it, which the
   `>1` branch exists to catch. `infra-reviewer` demonstrated the failure
   mode live: on a **multi-dimensional** (space-partitioned) hypertable, the
   same window matched *two* chunks sharing an identical time range. Not a
   live defect (`audit_events` is time-only), but the exact shape of claim
   that produced #2425 in the first place.
3. **`IAuditChunkInventory.DropChunkAsync` had no documented precondition**
   that its `chunk` argument must come from `ListChunksOlderThanAsync`.
   Added an XML-doc `<summary>` stating it (doc-only, no signature change).
4. **The new `Error` log line had no runbook procedure.** Added a
   `### DropChunkAsync drops more than one chunk` section to
   `docs/runbooks/audit-observability.md` explaining what it means and that
   the extra chunks were dropped without being archived.

**Filed as a follow-up, not fixed here** (the autonomous lane may not amend
an ADR — ADR-0144): `infra-reviewer` found that `docs/adr/0101-timescaledb-for-audit.md:158-160`
still documents the single-bound `drop_chunks` call that produced #2425 —
the design document that produced the bug still teaches the broken version.
Filed as issue #2484 and added to Project #13.

**Nits not acted on** (both reviewers flagged as cosmetic, no behavioural
risk): a log-name spelling inconsistency (schema-qualified vs. unqualified
chunk name between the new `DroppedChunk` log and the pre-existing
`ChunkRow`/`DeterministicChunkIdentifier`); an undocumented Postgres
exception (rather than empty result) if `newer_than >= older_than`, not
reachable since a real chunk's own bounds always satisfy `start < end` — now
noted in the code comment as part of should-fix #2 above; `dotnet format`
non-compliance on the new test file's identifier casing, pre-existing in the
sibling `RetentionRoundtripIntegrationTests.cs` and not gated by CI.

## Deliberately out of scope (per the architect's plan, not an oversight)

`AuditRetentionHostedService.cs:141-160` publishes `AuditChunkArchivedV1`
and commits the outbox **before** calling `DropChunkAsync`. A drop failure
after a successful archive leaves an announced-but-undropped chunk that is
re-announced on the next sweep. Not data loss — the archiver is
ETag-idempotent — and left untouched here per the issue's explicit
instruction and the smallest-possible-change rule (ADR-0036).

## Build

`dotnet build -c Release` on both changed projects (`AuditObservability.Infrastructure`,
`AuditObservability.Application`): **0 errors** on both, after the should-fix
edits above. Remaining warnings are pre-existing SonarAnalyzer advisories in
unrelated files (`GetResourceTimelineQueryHandler`, `SearchAuditQueryHandler`,
etc.), none introduced by this change.
