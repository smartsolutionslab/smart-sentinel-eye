# Tasks — Spec 199, the chunk a failed archive keeps

**Issue:** #2425 · **Spec:** `spec.md` · **Plan:** `plan.md`
**Phase-4a colour:** **RED** — a test that arrives green is a phase-4 failure.

## Parallelism (ADR-0109)

Almost none, and that is honest rather than a missed opportunity. Every
production task touches one of two files in one bounded context, and each
depends on the one before it. **Only the two phase-6 reviews are `[P]`** —
different reviewers, read-only, disjoint outputs.

There is no foundational `Shared.Kernel` / `Shared.Contracts` / AppHost task
here, so nothing in this slice blocks other issues. The parallelism available
to the orchestrator is between *this issue* and any issue not touching
`src/AuditObservability/` — and `spec.md` §*File contention* confirms there
are currently **no open PRs at all**, so every such issue is free.

## US1 — A chunk that was never archived is never dropped (P1)

### Phase 4a — RED (`test-writer`)

**T001 [US1]** — New file
`tests/Integration.Tests/AuditObservability/DropChunkIdentityIntegrationTests.cs`,
namespace `SmartSentinelEye.Integration.Tests.AuditObservability`,
`[Collection(AspireCollection.Name)]`, `AspireFixture` by primary constructor.
Copy the seeding shape from `RetentionRoundtripIntegrationTests.cs` in the same
folder — its `SeedBackdatedRowAsync(DateTimeOffset)` raw `INSERT` and its
`SqlQuery` read of `timescaledb_information.chunks`. Use a **bespoke
`event_kind`** (e.g. `DropIdentitySeedV1`) so the rows are identifiable: there
is no `ResetAuditObservabilityAsync` and no cleanup in this folder — isolation
here is by unique data.

Add the small test-local `IDbContextFactory<AuditObservabilityDbContext>` over
`App.GetConnectionStringAsync("audit-db")` that `TimescaleAuditChunkInventory`
needs (plan §*Where it goes*). Do **not** add a factory to `AspireFixture`.
*Depends on:* nothing.

**T002 [US1]** — Seed two rows at **`now - 85 days`** and **`now - 55 days`**
— exactly one 30-day chunk interval apart. Do not substitute other instants:
plan §*The seeding recipe* explains why each number is load-bearing (adjacent
contiguous chunks; both inside the 90-day window so the ambient 3-second
production worker cannot race the test; nothing aged past the boundary left
behind to break `RetentionRoundtripIntegrationTests`'s whole-hypertable
`ShouldBe(0)`).
*Depends on:* T001.

**T003 [US1]** — **Assert the premise before acting** (plan R1). Read
`timescaledb_information.chunks` for `hypertable_name = 'audit_events'`, locate
the two chunks the seeded instants fall in, and assert **both** that they are
two *distinct* chunks and that they are *contiguous*
(`older.RangeEnd == newer.RangeStart`). Shouldly messages must say which half
broke. This is an assertion, not a comment: without it a single-chunk outcome
passes on today's code and reads as "already fixed".
*Depends on:* T002.

**T004 [US1]** — The red. Build `TimescaleAuditChunkInventory` over the
test-local factory, construct an `AuditChunk` for the **newer** chunk from the
bounds read in T003 (never from bounds the test invented — plan R4), and call
`DropChunkAsync` for **that chunk only**. Then assert, re-reading state the
test has not written since seeding (plan R2):

- the **older** chunk's name is still present in
  `timescaledb_information.chunks`;
- the older chunk's seeded rows are still readable from `audit_events`;
- the **newer** chunk's name is gone.

Assert on the **captured chunk names**, never on a total chunk count (plan R3).
Today the first two assertions fail — the older chunk and its rows are gone.
That is the red.
*Depends on:* T003.

**T005 [US1]** — Name the test per ADR-0053 and give it a `<summary>` naming
#2425, spec 199 and SC-1, and saying **what a weaker version would fail to
distinguish** — as this context's tests already do. Suggested name:
`Dropping_a_chunk_leaves_the_older_unarchived_chunk_in_place`.
*Depends on:* T004.

**T006 [US1]** — Run the test against the real Aspire fixture (ADR-0103),
confirm it **fails**, and return the **verbatim** output. That output is the
phase-4 gate artefact and is quoted in the PR body (ADR-0139, ADR-0144).
Confirm in the same run that `RetentionRoundtripIntegrationTests` and
`AuditRetentionHostedServiceTests` still **pass** — the new test must not
disturb them.

**Do not touch any file under `src/`.** In particular, **do not modify
`FakeAuditChunkInventory`** (`tests/AuditObservability.Application.Tests/Fakes/FakeAuditChunkArchiver.cs`):
it already drops by identity, which is the intended contract and is exactly why
no unit test caught this. Making it drop by range would encode the bug as the
safety net.

*Depends on:* T005. **Blocks everything below.**

### Phase 4b — implement (`backend-engineer`)

**T007 [US1]** — In `TimescaleAuditChunkInventory.DropChunkAsync`, replace the
`ExecuteSqlInterpolatedAsync` call with the bounded, result-reading form from
`plan.md` §1: `SqlQuery<DroppedChunkRow>` over

```sql
SELECT drop_chunks(
    'audit_events',
    older_than => {chunk.OccurredUntil},
    newer_than => {chunk.OccurredFrom})::text AS "ChunkName"
```

plus `private sealed record DroppedChunkRow(string ChunkName);` alongside the
existing `ChunkRow`. Both bounds stay **parameters** — interpolate no
identifier. Mirror the file's own existing `SqlQuery<ChunkRow>` usage at
`:29-41` rather than introducing a second style. Keep the `Ensure.That(chunk).IsNotNull()`
guard (ADR-0105) and the `CancellationToken` as the last argument (ADR-0049).
*Depends on:* T006. **May not edit any assertion written in T001–T005.**

**T008 [US1]** — Replace `DroppedChunks` in
`src/AuditObservability/Infrastructure/Log.cs` with the three `[LoggerMessage]`
entries from `plan.md` §3 (ADR-0050): `DroppedChunk` at `Information` naming
the chunk, `ChunkAlreadyDropped` at `Warning` for the zero-row case,
`DroppedMoreThanOneChunk` at **`Error`** naming every chunk dropped. Branch on
`dropped.Count` in `DropChunkAsync`. The error branch **logs and does not
throw** — the drop has already committed and the sweep's `catch` would swallow
it (plan §3). `DroppedChunks` has exactly one call site, so it goes.
*Depends on:* T007.

**T009 [US1]** — Replace the comment at
`TimescaleAuditChunkInventory.cs:56-58`. It currently states the false
conclusion that caused #2425. The replacement must say what the two bounds do
**and** that `older_than` alone drops every older chunk — the fact whose
absence produced the bug. State the predicate as verified:
`range_start >= newer_than AND range_end <= older_than`.
*Depends on:* T008.

**T010 [US1]** — `docs/runbooks/audit-observability.md`: the claim at `:114`
that *"the next nightly sweep retries the same chunk"* is false today and true
after this change. Keep the wording and add a line to that section recording
that before this fix the retry could not happen, because the chunk had already
been dropped — an operator reading old incident notes needs to know which side
of the fix they were on.
*Depends on:* T009.

**T011 [US1]** — Green: run the new integration test plus
`AuditRetentionHostedServiceTests` and `RetentionRoundtripIntegrationTests`,
then the gates — `dotnet format --verify-no-changes` and a **Release** build
(analyzers are errors there).
*Depends on:* T010.

### Phase 5 — verify (`/verify`)

**T012 [US1]** — Verification note recording:
(a) the suite red → green with the T006 output quoted verbatim;
(b) the **actual `DroppedChunk` log line** read from the running stack — it
must name a chunk and carry no `-1`. `"audit-observability"` is not in
`AspireFixture.TailedResources`, so read it from the Aspire dashboard or
`ResourceDiagnosticsAsync`, not via `RecentLogs` (plan R8);
(c) the direct `drop_chunks` observation from `spec.md` §*Independent
end-to-end test procedure* item 2, repeated against **the stack's own
Postgres** rather than the throwaway container the spec's figures came from;
(d) explicitly, that the integration test ran against **uncompressed** chunks,
and that the compressed case rests on the live probe in `spec.md`, not on this
run (plan R5).
Latency: **N/A** — nightly background sweep, not on any constitution §IV leg.
State it rather than omit it.
*Depends on:* T011.

### Phase 6 — review

**T013 [P] [US1]** — `backend-reviewer` on the diff: EF Core raw-SQL usage,
`[LoggerMessage]` correctness, guards, `CancellationToken` placement, test
hygiene, and whether the error branch's decision not to throw is right.

**T014 [P] [US1]** — `infra-reviewer` on the diff, asked the three questions in
`plan.md` §*Roles*: (1) can any chunk other than the target fall inside
`[OccurredFrom, OccurredUntil)` under a changed `chunk_time_interval`, a
re-chunked hypertable, a compressed chunk, or a multi-dimensional hypertable?
(2) does the phase-4a red actually prove the defect, or could it pass for an
unrelated reason (plan R1, R3)? (3) **does anything else in the repository call
`drop_chunks` — or any other windowed Timescale function — with a single bound
and the same mistaken assumption?**

Question 3 is the one most worth asking: this slice fixes the call site the
issue names, and nobody has yet checked whether it is the only one.

*Both depend on:* T012. Genuinely parallel — different reviewers, read-only,
disjoint outputs (ADR-0109).

### Phase 7 — PR

**T015 [US1]** — PR to `develop` (`--base develop`), quoting the T006 failure
output, referencing #2425 with a closing keyword, and naming spec 199. The body
must carry:

- the **live TimescaleDB evidence** from `spec.md` §*Verified behaviour* — the
  two-row `drop_chunks` result that is the whole proof;
- the **one-line note on the publish-before-drop ordering**
  (`AuditRetentionHostedService.cs:136-160`) that the issue explicitly asked be
  flagged and not fixed here — `plan.md` §*Noted, not fixed*;
- whatever `infra-reviewer` answers to question 3, since a second affected call
  site would need its own issue.

*Depends on:* T013, T014.

## Dependency summary

```
T001 → T002 → T003 → T004 → T005 → T006 ──┐
                                          │
   ┌──────────────────────────────────────┘
   └→ T007 → T008 → T009 → T010 → T011 → T012 → {T013 [P], T014 [P]} → T015
```

T006 is the gate: nothing under `src/` may change before it returns a verbatim
red.

## Board

#2425 is on Project #13 (status **Todo**), verified with
`gh project item-list 13 --owner smartsolutionslab --limit 2000` filtered by
`content.url`. Feature-level issue only — no per-task issues (CLAUDE.md
§Workflow, since spec 028).
