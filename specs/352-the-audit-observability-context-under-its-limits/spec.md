# Spec 352: The audit observability context under its limits

**Feature Branch**: `refactor/2894-audit-observability-metrics` (cut from `origin/develop` at `8b1840bd`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Five judgement calls
are marked explicitly (§6).

**Input**: Issue [#2894](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2894),
*Clear the ADR-0084 metric warnings in Audit Observability's Domain, Application and Infrastructure (11)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 352. Checked on 2026-10-14 after `git fetch`: `origin/develop` holds up to 349;
`git ls-tree` over every remote head finds 350 only on `origin/refactor/2892-overlay-designer-metrics`
(open PR #2907); 351 exists unpushed in the `sse-2893` worktree (#2893); the other open PRs
(#2811, #2837) carry no spec. Highest visible is 351. Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S138 <= 30 lines, S107 <= 4 parameters,
S1541 <= 10, S134 <= 3, advisory `warning`; migrations out of the count), ADR-0144 (autonomous
lane; phase 4a colours; the lane may not weaken a gate), ADR-0036 (smallest change), ADR-0050
(`[LoggerMessage]` fields come from the template), ADR-0049 (cancellation tokens; one declared
exception, J4), ADR-0101 / ADR-0155 (audit chunks archived to Azure Blob), ADR-0115 (fab-neutral
events), ADR-0141 (`Option<T>` advisory in Application).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written: `src/AuditObservability` holds no project file. The
equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/AuditObservability/Infrastructure/SmartSentinelEye.AuditObservability.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep AuditObservability
```

Result on `8b1840bd`: 0 errors, **11 hits, exactly the 11 the issue lists, at the same lines. None
is stale.** (The same build prints 11 `ServiceDefaults` hits; out of scope, filtered by the last
`grep`.)

## 2. Decision per hit

| # | File:line | Member | Rule | Decision |
|---|---|---|---|---|
| 1 | `Application/EventHandlers/AuditingMessageHandler.cs:33` | primary constructor (5) | S107 | **Leave**: DI constructor on the per-event ingest path; five test sites construct it with five arguments (`AuditingMessageHandlerTests` x3, `AuditMeasurementSwitchTests.Build`, `IntegrationEventAuditHandlerTests`), so a parameter object changes tests that must stay unmodified, and the DI graph |
| 2 | `Application/Log.cs:11` | `Audited` `[LoggerMessage]` (5) | S107 | **Leave**: parameters are the structured log template's fields (ADR-0050) |
| 3 | `Application/Log.cs:23` | `ArchivedChunk` `[LoggerMessage]` (5) | S107 | **Leave**: same reason |
| 4 | `Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs:15` | `HandleAsync` | S138 | **Clear** by extraction (plan §3.2) |
| 5 | same | `HandleAsync` | S1541 | **Clear** by the same extraction |
| 6 | `Application/Queries/Handlers/SearchAuditQueryHandler.cs:16` | `HandleAsync` | S138 | **Clear** by extraction (plan §3.1) |
| 7 | same | `HandleAsync` | S1541 | **Clear** by the same extraction |
| 8 | `Infrastructure/Archive/AzureBlobAuditChunkArchiver.cs:40` | `ArchiveChunkAsync` | S138 | **Clear** by extraction (plan §3.5), **conditional on C6** (§6 J3) |
| 9 | `Infrastructure/Persistence/AuditEventRepository.cs:28` | `SaveAsync` | S138 | **Clear** by extraction (plan §3.4), **conditional on C5** (§6 J3) |
| 10 | `Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs:17` | `Configure` | S138 | **Clear** by extraction (plan §3.3) |
| 11 | `Infrastructure/Persistence/Migrations/20260529124335_InitialAuditObservability.cs:12` | generated `Up` | S138 | **Leave**: generated, already applied; the issue forbids touching migrations |

**Cleared: 7** (hits 4-10). **Left: 4** (hits 1, 2, 3, 11). Expected after-count: **4**, every one
named in the PR body with its reason. If C5 or C6 proves infeasible (J3), hit 9 or 8 moves to
"Leave" and the after-count becomes 5 or 6; that is a declared outcome, not a failure.

## 3. User story

### US1 (P1): AuditObservability's non-Api layers meet ADR-0084 wherever that is mechanical

As a maintainer, I want the 7 clearable hits gone, each by an extraction, **without** any change
an HTTP caller, an operator scoped to a fab, the audit table, the archive blob, a log reader, or the
ingest and search latency could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file, no DI registration.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing AuditObservability test passes unmodified (happy path)
  Given AuditObservability.Domain.Tests (95), AuditObservability.Application.Tests (84) and Architecture.Tests (759)
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: A search is refused in today's order, with today's codes (bad request)
  Given a search whose page size, resource kind and cursor are all invalid
  Then the failure is PageSizeOutOfRange; with page size valid it is InvalidResourceKind; then InvalidCursor
  And a timeline with the same three invalid inputs fails UnknownResourceKind first, then PageSizeOutOfRange, then InvalidCursor
  And an empty-string cursor is refused as InvalidCursor("") by both

Scenario: A search is scoped to fabs exactly as today (auth)
  Given rows in "munich", in "berlin", fab-null rows attributed Unresolved and NotApplicable
  When the caller names "munich" and also claims "berlin"
  Then only "munich" rows return (no null-fab row, no "berlin" row)
  When the caller names no fab and claims "munich" and a malformed group
  Then "munich" rows and every null-fab row return, "berlin" rows do not
  When the caller names no fab and claims nothing usable
  Then only null-fab rows return

Scenario: Paging walks ties without overlap or gap, with today's cursors (conflict: concurrent inserts)
  Given rows sharing one OccurredAt and a page size that splits them
  Then search pages newest-first and the timeline oldest-first, ties broken by audit identifier
  And each next cursor equals the captured literal, the boundary row appears on exactly one page
  And a page that is exactly full carries no next cursor
  And since is inclusive, until is exclusive

Scenario: A search parse that throws still throws, in today's order
  Given an unparseable named fab together with an over-long actor username
  Then the ArgumentException from the fab parse propagates (type and full message pinned)
  Given an over-long actor username together with an over-long event kind
  Then an empty page returns and the event kind is never parsed

Scenario: Ingest writes the same statement with the same values
  Given two pending rows with distinct values, one with every nullable column null
  When SaveAsync runs
  Then two commands are issued in Add order, each with today's exact SQL text and parameter values
  And a SaveAsync with nothing pending issues none; a failed insert propagates and leaves the rows pending

Scenario: Archiving keeps today's branches (destructive path)
  Given a chunk of two rows
  Then a missing blob is uploaded with today's key, content type, Content-MD5 and NDJSON body
  And a blob whose stored hash matches is not re-uploaded and the result says AlreadyArchived
  And a blob whose stored hash differs is re-uploaded
  And a non-404 probe failure or an upload failure propagates, with no success log and no result

Scenario: The EF model is unchanged
  Then AuditObservabilityDbContext's logical model equals the captured table
  And `dotnet ef migrations has-pending-model-changes` reports no changes
```

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 4 "Leave" rows after the change.
3. `dotnet ef migrations has-pending-model-changes` (plan §5): "No changes have been made to the
   model since the last migration." before and after.
4. CI's Integration.Tests (`AuditObservability/*`: `EndToEndIngestionIntegrationTests`,
   `RetentionRoundtripIntegrationTests`, `DropChunkIdentityIntegrationTests`,
   `CrossFabReadGuardIntegrationTests`, `NeutralFabRetentionRowIntegrationTests`,
   `UnresolvedFabAuditRowIntegrationTests`, `NFR002_AuditSearchLatencyTests`) green: they boot the
   real stack and cover the raw insert, the archiver against Azurite, EF translation of both query
   handlers and the fab guard against TimescaleDB. Docker-only, so observed in CI.

## 4. Functional requirements

- **FR-001** The 7 "Clear" hits of §2 report nothing after the change. A hit an extraction leaves is
  reported with its measured value; it is never suppressed.
- **FR-002** No public signature changes: both query handlers' `HandleAsync` and constants,
  `AuditCursor`, `AuditRowMapper`, `AuditEventRepository.Add`/`SaveAsync`,
  `AzureBlobAuditChunkArchiver.ArchiveChunkAsync` and constructor, `AuditEventConfiguration.Configure`,
  every error record, `Shared.Contracts`, both `Log.cs` files.
- **FR-003** For every input: every `Result` (success page: rows, order, next cursor; failure: type,
  code, message, status), every thrown exception (type, message), every SQL command (text,
  parameters, order), every blob call (order, arguments, headers, bytes), every log call and every EF
  model element is identical. Statements move verbatim; no "while I'm here" edits.
- **FR-004** Per-event ingest path (spec 009 NFR-001): `AuditEventRepository.SaveAsync` gains **no
  heap allocation, no I/O, no lock and no `await`** per row. The extracted insert is a non-`async`
  method returning EF's `Task<int>` directly (no new state machine).
- **FR-005** Query helpers take and return `IQueryable<AuditEvent>` (never `IEnumerable`), so every
  predicate stays server-side; lambda bodies are moved verbatim.
- **FR-006** No new primitive on a Domain type (constitution §II): Domain is not changed.
- **FR-007** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change (the
  existing `#pragma warning disable CA5351, S4790` around the MD5 call stays where it is, unmoved);
  no guard edited; no file under `Api/`, `Domain/` or `Migrations/` touched; `AuditingMessageHandler`,
  `AuditRetentionHostedService` and both `Log.cs` untouched.
- **FR-008** The query handlers keep their `(var a, var b, ...) = query;` deconstruction form, which
  `HandlerDeconstructionTests` does not scan (plan §2); converting it is out of scope.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 4 "Leave" rows (or 5/6 per J3).
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted, plus each group's named mutations observed red and reverted (plan §4).
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the two
  touched projects; coverage gates hold (Domain >= 90 %, Application >= 80 %).
- **SC-004** Latency in the PR body: §IV legs **none**; spec 009 NFR-002 (search p99 <= 200 ms) quoted
  from `NFR002_AuditSearchLatencyTests`' output line in the last green `develop` CI run's TRX (before)
  and from this PR's run and one re-run (after); NFR-001 (ingest) stated as **held by construction,
  no CI figure exists** (its test is `Category=Measurement`, excluded from CI).

## 6. Judgement calls (marked)

- **J1. Cursor, window and page helpers are private per handler, not shared.** Search pages
  descending (strict `<`), the timeline ascending (strict `>`); the predicates differ, and a shared
  helper would join two handlers and widen the change. `ToPage` and the cursor-refusal helper are
  textually identical in both files; that duplication existed inline before and is not a measured
  rule.
- **J2. The empty-page early return for an unparseable actor username stays in `HandleAsync`** behind
  a `Try*` helper, so the actor-then-username order and "the actor filter above" comment hold.
- **J3. Two Infrastructure clears depend on a seam the test-writer must prove first.** No
  `AuditObservability.Infrastructure.Tests` project exists. The insert (C5) and the archiver (C6) are
  characterised in `Architecture.Tests` against a real `AuditObservabilityDbContext` on Npgsql with
  EF interceptors that suppress the connection open and the command (precedent:
  `ServiceDefaults.Tests/OutboxBacklogHealthCheckTests` intercepts Npgsql without a database;
  #2907 placed its model characterisation in `Architecture.Tests/Persistence`). If either seam cannot
  be made to work, the matching hit is **left**, with that reason, rather than refactored on CI-only
  evidence: the archiver gates a destructive drop and the insert is the per-event write. A new
  Integration test was considered and rejected: one S138 does not justify a Docker-only test, a
  shard-filter entry and a CI-only red/green observation.
- **J4. The extracted NDJSON writer takes no `CancellationToken`** (ADR-0049 deviation, declared).
  Today's `WriteLineAsync(string)` calls are not cancellable; passing a token that is used would
  change cancellation behaviour, and one that is unused is dead.
- **J5. The brief's premises corrected, not acted on.** (a) The retention order is archive (with
  hash verify) -> publish to outbox -> commit -> drop -> log, in `AuditRetentionHostedService`, which
  this spec does not touch; its order is pinned by the existing `AuditRetentionHostedServiceTests`.
  (b) Fab-null rows attributed `Unresolved` **are** visible today to every fab-scoped search and
  timeline caller (deliberate per #1300 / #2076 comments in the code). Pinned as is.

No defect is fixed here. Oddities recorded and **pinned, not changed** (each a separate issue if
thought wrong): (a) search validates page size before resource kind, the timeline the reverse;
(b) search parses `fab`, `actor`, `eventKind`, `resourceIdentifier` unguarded, so an unparseable
value throws `ArgumentException` (the Api pre-validates `fab`; the others would surface as a 500);
(c) a named `fab` makes the handler ignore `callerFabs` entirely (authorisation is the Api's fab
guard); (d) fab-null `Unresolved` rows are visible to every scoped caller (J5b); (e) a failed insert
leaves every pending row pending, so the next `SaveAsync` re-issues the whole batch (absorbed by
`ON CONFLICT`).

## 7. Latency budget impact (constitution §IV)

| Member | Runs | §IV leg / NFR |
|---|---|---|
| `AuditEventRepository.SaveAsync` | per integration event | none of §IV's six legs (audit observes the bus; it is not on event -> overlay). Spec 009 **NFR-001** (ingest p99 <= 50 ms; #1956, #2127) |
| `SearchAuditQueryHandler`, `GetResourceTimelineQueryHandler` | per HTTP query | none; spec 009 **NFR-002** (search p99 <= 200 ms) |
| `AzureBlobAuditChunkArchiver.ArchiveChunkAsync` | per retention chunk | none |
| `AuditEventConfiguration.Configure` | once per model build | none |

NFR-001 is held by construction (FR-004). `NFR001_AuditIngestLatencyTests` is
`[Trait("Category","Measurement")]` and never runs in CI, so **no before/after CI figure exists**;
and its figure (`received_at - occurred_at`) is stamped before `SaveAsync` anyway. A local run is
optional, only when no other Aspire stack runs on the machine, and run twice. NFR-002's figure
comes from CI (§5 SC-004).

## 8. Out of scope

Every `Api/` file, migrations, tests' own metrics, the `ServiceDefaults` hits the same build prints,
both `Log.cs` files, `AuditingMessageHandler`, `AuditRetentionHostedService`, `AuditCursor`, DI
modules, and the oddities in §6.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052), sentence-style names (ADR-0053), `Ensure.That`
guards (ADR-0105), `[NotNullWhen]` on `Try*`/`Is*` helpers rather than `!`, `var` wherever it
compiles, collection expressions, one parameter per line when wrapping, no leading underscores
(CLAUDE.md house rules). No Testcontainers (ADR-0103).
