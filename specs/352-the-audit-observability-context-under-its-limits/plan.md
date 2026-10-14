# Plan 352: The audit observability context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2894.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 7 of 11 hits by shape only; the 4 left are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | Domain is not touched. No new type is introduced. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| §IV (latency) | No leg touched. NFR-001 (ingest) held by construction (FR-004); NFR-002 (search) evidenced from CI. |
| ADR-0050 | Both `Log.cs` untouched; every log call keeps method, arguments and order. |
| ADR-0049 | One declared exception: the NDJSON writer helper has no token (spec J4). |
| House rules | `var`; one parameter per line when wrapping; `[NotNullWhen]` instead of `!`; static where no instance state. |

**Concurrency.** #2907 (spec 350, `sse-2892`) touches `src/OverlayDesigner/*` and adds
`OverlayDesigner*` test files incl. `tests/Architecture.Tests/Persistence/OverlayDesigner{Model,Registration}CharacterisationTests.cs`.
#2893 (spec 351, `sse-2893`) touches `src/StreamDistribution/*` and adds `StreamDistribution*` /
`*Whep*` / `*Stream*` test files incl. `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs`.
The merged siblings (341-349) touched other contexts and `src/*/Api/*.cs`. This plan touches only
`src/AuditObservability/{Application,Infrastructure}` (no `Api/`, no module, no `.slnx`, no
`.csproj`) and adds `Audit*`-named test files. **No file overlap.**

## 2. Guards that read these files, and the shapes they tolerate

| Guard | What it reads | This plan |
|---|---|---|
| `HandlerDeconstructionTests` | `public async Task ... Handle/HandleAsync(<Type> <param>,` then the first `var (...) = <param>;` | Both query handlers match the signature but use `(var a, ...) = query;`, which the `Deconstruction` regex does not match, so they are skipped today. **Keep that form verbatim** (FR-008). |
| `EventMetadataFabDeclarationTests` | `new EventMetadata(` / `Metadata: new(` in handler files | None in any touched file (`AuditRetentionHostedService`, which has one, is not touched). |
| `PrimitiveBoundaryTests` | Domain model state | Domain untouched. |
| `StaleCodeConventionTests` | every `src/**/*.cs` for `"UPPER_SNAKE"` literals | No such literal is added. |
| `BoundaryTests`, `OutboxCommitTests` | assembly references; outbox commit sites | Unchanged. |
| `AuditObservabilityHealthCheckRegistrationTests`, `PostgresComposition` | the DI module | Module not touched. |

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (`out` and `CancellationToken` count), <= 30 lines,
complexity <= 10, nesting <= 3**, verified with the §5 grep after each commit. Statements move
verbatim; comments move with the code they explain (list in §3.6).

### 3.1 `SearchAuditQueryHandler.HandleAsync` (hits 6, 7)

`HandleAsync` keeps, in today's order: guard; the `(var fab, ...) = query;` line; page-size default
and `PageSizeOutOfRange`; the `rk` check and `InvalidResourceKind`; then

```csharp
if (IsUndecodableCursor(rawCursor, out var cursor))
{
    return Failure(SearchAuditFailures.InvalidCursor(rawCursor));
}

var source = ScopeToFabs(events.AuditEvents, fab, callerFabs);
if (!TryFilterByActor(source, actor, actorUsername, out var actorFiltered))
{
    return Success(new AuditPageDto([], null));
}

source = FilterByKindAndResource(actorFiltered, eventKind, resourceKind, resourceIdentifier);
source = FilterByWindowAndCursor(source, since, until, cursor);
var rows = await source.OrderByDescending(...).ThenByDescending(...).Take(pageSize + 1).ToListAsync(cancellationToken);
return Success(ToPage(rows, pageSize));
```

Private static helpers (all synchronous, all `IQueryable<AuditEventEntity>` in and out — FR-005):

- `IsUndecodableCursor([NotNullWhen(true)] string? rawCursor, out (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor)`:
  `cursor = null; if (rawCursor is not null) { cursor = AuditCursor.TryDecode(rawCursor); return cursor is null; } return false;`
  (empty string -> refused, as today; `[NotNullWhen(true)]` removes any need for `!`).
- `ScopeToFabs(source, string? fab, IReadOnlyList<string> callerFabs)`: the whole `if (fab is not null) ... else ...`
  block verbatim, calling the existing `ParseUsableFabs`.
- `TryFilterByActor(source, Guid? actor, string? actorUsername, out IQueryable<AuditEventEntity> filtered)`:
  `filtered = source;` then the actor block, then the username block whose `catch (ArgumentException)`
  now `return false;` (the caller returns the empty page). **Actor parse precedes username parse**,
  as today; the username comment's "as the actor filter above does" stays true.
- `FilterByKindAndResource(source, string? eventKind, string? resourceKind, string? resourceIdentifier)`:
  the three blocks verbatim, in that order.
- `FilterByWindowAndCursor(source, DateTimeOffset? since, DateTimeOffset? until, (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor)`:
  since (`>=`), until (`<`), cursor (strict `<` tuple) verbatim with the cursor comment.
- `ToPage(List<AuditEventEntity> rows, int pageSize)` returning `AuditPageDto`: the next-cursor block and
  `dtos` verbatim (reads `rows.Count` before reassigning, so S1226 does not apply).

Exception order is preserved: fab parse (in `ScopeToFabs`) -> actor -> username (empty page) ->
event kind -> resource kind -> resource identifier. The ordering may move into a helper only if
`HandleAsync` still exceeds 30 lines; it must stay `OrderByDescending(OccurredAt).ThenByDescending(Id)`.

### 3.2 `GetResourceTimelineQueryHandler.HandleAsync` (hits 4, 5)

Keeps, in today's order: guard; `(var resourceKind, ...) = query;`; `UnknownResourceKind`; page size and
`PageSizeOutOfRange`; then `if (IsUndecodableCursor(rawCursor, out var cursor)) { return Failure(GetResourceTimelineFailures.InvalidCursor(rawCursor)); }`;
the three filter locals with their "Compare value objects directly" comment; the `source` chain of
three `.Where` **verbatim, including the long fab-neutral comment, in place**; then
`source = FilterByWindowAndCursor(source, since, until, cursor);` (ascending: strict `>` tuple, the
"Ascending order" comment moves with it); `OrderBy(OccurredAt).ThenBy(Id).Take(pageSize + 1).ToListAsync`;
`return Success(ToPage(rows, pageSize));`. Own private copies of `IsUndecodableCursor`,
`FilterByWindowAndCursor` and `ToPage` (spec J1).

### 3.3 `AuditEventConfiguration.Configure` (hit 10)

`Configure` keeps the guard and calls, in today's order, private static methods each taking
`EntityTypeBuilder<AuditEventEntity> builder`:

1. `ConfigureTableKeyAndTimestamps` — `ToTable`, `HasKey` (TS103 comment), `Id`, `OccurredAt`, `ReceivedAt`,
   `HandlerEnteredAt` (spec 053 comment), `WrittenAt` (clock_timestamp comment).
2. `ConfigureFabAndEventKindColumns` — `Fab`, `FabAttribution` (spec 306 comment), `EventKind`.
3. `ConfigureResourceActorAndEventIdentifierColumns` — `ResourceKind`, `ResourceIdentifier`, `Actor`,
   `ActorUsername`, `EventIdentifier`.
4. `ConfigurePayloadAndSchemaVersion` — `OwnsOne(Payload)` (owned-reference comment), `Navigation(Payload).IsRequired()`, `SchemaVersion`.
5. `ConfigureIndexes` — the five indexes in order, each comment with its index ("Cross-cutting search."
   stays above the actor index; the double blank line before the fab index moves as it is).

Builder calls keep their order and arguments.

### 3.4 `AuditEventRepository.SaveAsync` (hit 9) — per-event path, conditional on C5

```csharp
foreach (var row in pending)
{
    await InsertUnlessDuplicateAsync(row, cancellationToken);
}
```

`private Task<int> InsertUnlessDuplicateAsync(AuditEventEntity row, CancellationToken cancellationToken)`
— **not `async`**: the three locals (`fab`, `resourceKind`, `resourceIdentifier`) then
`return dbContext.Database.ExecuteSqlInterpolatedAsync($"""...""", cancellationToken);` with the raw
string moved verbatim (uniform re-indent keeps the content identical: the closing `"""` sets the
margin). The interpolated literal stays the **direct argument** of `ExecuteSqlInterpolatedAsync`
(never hoisted into a `string`, never `ExecuteSqlRaw*`). Early return on an empty `pending` and
`pending.Clear()` after the loop stay in `SaveAsync`. Measured: 28 lines in the helper. If it
reports > 30, stop and report; do not reformat the SQL (C5 pins its text).

### 3.5 `AzureBlobAuditChunkArchiver.ArchiveChunkAsync` (hit 8) — destructive gate, conditional on C6

`ArchiveChunkAsync` keeps, in order: guard; `opts`; container; `CreateIfNotExistsAsync`; object key;
blob client; the `await using var context` (its lifetime unchanged: it still ends with the method);
the rows query; `using MemoryStream payload = new();`; then

- `await WriteNdjsonAsync(payload, rows);` — `private static async Task WriteNdjsonAsync(MemoryStream payload, List<AuditEventEntity> rows)`
  holding the two `await using` (`GZipStream` with `leaveOpen: true`, `StreamWriter`) and the loop
  verbatim. No token (spec J4).
- `payload.Position = 0;`, the `#pragma`-wrapped MD5 line **unmoved**, `contentMd5`, the idempotency
  comment, the probe, the short-circuit with `ChunkAlreadyArchived` and its `return`, the second
  `payload.Position = 0;`.
- `await UploadPayloadAsync(blob, payload, md5Bytes, cancellationToken);` — `private static Task UploadPayloadAsync(BlobClient blob, MemoryStream payload, byte[] md5Bytes, CancellationToken cancellationToken)`,
  **not `async`**, returning `blob.UploadAsync(payload, new BlobUploadOptions { ... }, cancellationToken)` verbatim.
- `ArchivedAuditChunk` log, then the result.

Upload is still awaited before the log and the return, so the archiver can only return after the
blob holds the content or its stored hash matched — the precondition `AuditRetentionHostedService`
relies on before it drops (spec J5a).

### 3.6 Comments that move or whose reason depends on shape (reviewer re-reads each)

| File | Comment | After |
|---|---|---|
| Search | "Compare value objects directly (not their `.Value`) ..." | Moves to directly above `ScopeToFabs`; still true for every predicate helper. |
| Search | e9afe03e malformed-claim, #1300 cross-fab, #1300 no-membership | Move with their code into `ScopeToFabs`. |
| Search | username "Parsing it unguarded — as the actor filter above does —" | **Depends on shape**: true only while the actor block precedes it in the same method (`TryFilterByActor`). |
| Search | "Strict 'less than' for descending order ..." | Moves into `FilterByWindowAndCursor`; the descending `OrderBy` it relies on is now in `HandleAsync` — still true, no longer adjacent. |
| Timeline | "Ascending order — strict 'greater than'" | Same, ascending. |
| Timeline | "Compare value objects directly so EF translates ..." and the fab-neutral block | Stay in place. |
| Archiver | "Idempotency: a previous successful run leaves the blob in place ..." | Stays above the probe. |
| Configuration | every comment | Moves with its builder call; spec 306's "Written explicitly by AuditEventRepository's raw insert" stays true (C5 pins the insert). |
| `AuditCursor` summary | "predicate is strict-greater/less-than on the composite" | Untouched; still true (pinned by C2/C3). |

### 3.7 Analyzer-forced or convention-forced shapes (declare in the PR)

- `WriteNdjsonAsync` without `CancellationToken` (ADR-0049; spec J4).
- `[NotNullWhen(true)]` on `IsUndecodableCursor`'s `rawCursor` so `InvalidCursor(rawCursor)` compiles without `!`.
- `out` parameters on `IsUndecodableCursor` / `TryFilterByActor` (existing `Try*` pattern in siblings).
- CA1859 may require concrete parameter types on private helpers (`List<AuditEventEntity>`, `MemoryStream`); take them.
- CA1822: every helper that reads no instance state is `static`; `InsertUnlessDuplicateAsync` reads `dbContext` and is not.
- If IDE0046 (conditional expression) or S1226 fires on a helper, record the measured shape in the PR rather than suppressing.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Existing coverage relied on (must pass unmodified):

| Member | Existing tests |
|---|---|
| `SearchAuditQueryHandler` | `SearchAuditQueryHandlerTests` (12) |
| `GetResourceTimelineQueryHandler` | `GetResourceTimelineQueryHandlerTests` (8) |
| Retention order (untouched) | `AuditRetentionHostedServiceTests` (9) |
| `AuditEventConfiguration`, `AuditEventRepository`, `AzureBlobAuditChunkArchiver` | **none in a unit project**; Integration.Tests only (CI) |

Gaps are pinned by **new files only**; no existing test or shared fake is edited; fakes are
test-local nested classes. Every expected value is a **hand-written literal** captured on
unrefactored `8b1840bd`, never computed by the code under test, with **one declared exception**: gzip
bytes are runtime-dependent, so C6 pins the MD5 as a *relation* (the uploaded `ContentHash` equals
`MD5.HashData` of the uploaded bytes, and `ContentMd5` equals its lowercase hex) and pins the
**decompressed** NDJSON as a literal. Every log is pinned by level and **full formatted text** with
a **distinct value in every slot** (distinct Guids, row counts != 1, distinct keys). Exceptions are
pinned by **type and full message**. Error results by type, `Code`, `Message` and `Status`.

| ID | File | Pins | Mutations the test-writer must show RED |
|---|---|---|---|
| C1 | `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditRefusalAndScopeCharacterisationTests.cs` | **Refusal precedence**: page 201 + bad kind + bad cursor -> `PageSizeOutOfRange(201,1,200)`; page ok -> `InvalidResourceKind`; then `InvalidCursor`; `""` cursor -> `InvalidCursor("")`; page 0 and -1 -> 50 rows max, 200 accepted. **Throw precedence**: bad named fab + over-long username -> `ArgumentException` from `FabIdentifier.From` (type + message); over-long username + over-long event kind -> empty page, no throw. **Fab scope**: rows in munich, berlin, fab-null `Unresolved`, fab-null `NotApplicable`; named munich + claimed berlin -> munich only; unnamed + [munich, malformed] -> munich + both null-fab rows; unnamed + [] and unnamed + [malformed] -> both null-fab rows only. Result rows asserted by audit identifier literals, in order. | (a) swap the page-size and resource-kind checks; (b) in `ScopeToFabs`' named branch add `\|\| auditEvent.Fab == null`; (c) drop `auditEvent.Fab == null \|\|` in the claimed branch; (d) parse `eventKind` before `actorUsername` |
| C2 | `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditPagingCharacterisationTests.cs` | Five rows, three sharing one `OccurredAt` with literal Guids chosen so `Guid.CompareTo` orders them; page size 2: page 1 rows + next-cursor **literal** (base64 of `<ticks D19>.<guid N>`), page 2 exactly the boundary's successor, page 3 the last row with `null` cursor; exactly 2 rows at page size 2 -> `null` cursor; `since` = a row's instant includes it, `until` = a row's instant excludes it; each of actor, username, event kind, resource kind, resource identifier narrows to the literal row set. | (a) `< 0` -> `<= 0` in the tie-break; (b) `OccurredAt < c.OccurredAt` -> `<=`; (c) `rows[pageSize - 1]` -> `rows[pageSize]`; (d) `rows.Count > pageSize` -> `>=`; (e) `>= sinceFrom` -> `> sinceFrom` |
| C3 | `tests/AuditObservability.Application.Tests/Queries/Handlers/GetResourceTimelineCharacterisationTests.cs` | **Refusal precedence**: bad kind + page 201 + bad cursor -> `UnknownResourceKind`; kind ok -> `PageSizeOutOfRange`; then `InvalidCursor`; `""` cursor refused; `"Camera"` (wrong case) unknown. **Scope**: own fab, other fab, fab-null `Unresolved` and `NotApplicable`, another resource -> own + both null-fab rows. **Paging** ascending with the same tie design as C2, cursor literals, exact-full page `null` cursor, since inclusive, until exclusive. | (a) swap the kind and page-size checks; (b) `> 0` -> `>= 0` in the tie-break; (c) drop `auditEvent.Fab == null \|\|`; (d) `< untilTo` -> `<= untilTo` |
| C4 | `tests/Architecture.Tests/Persistence/AuditObservabilityModelCharacterisationTests.cs` | Mirror `SystemVariablesModelCharacterisationTests`: `AuditObservabilityDbContext` with `UseNpgsql("Host=unused;...")`, design-time model via the **public metadata API** (table, schema, key properties in order; per property: column, store type, CLR type, provider type, nullability, max length, converter presence, value generation; owned `Payload` and its columns; navigation required-ness; indexes: name, properties in order, uniqueness, filter). **Not** `Model.ToDebugString()`. | (a) drop `.IsUnique()` on `ux_audit_event_identifier`; (b) `HasMaxLength(16)` -> `32` on `fab_attribution` |
| C5 | `tests/Architecture.Tests/Persistence/AuditEventInsertCharacterisationTests.cs` | Real `AuditEventRepository` over `AuditObservabilityDbContext` on Npgsql with a test-local `DbConnectionInterceptor` (suppress open) and `DbCommandInterceptor` (record `CommandText` and parameter values, suppress with result 1). Two rows built via `AuditEvent.From` with distinct values, one with fab, resource, username and handler-entered all null: exactly two commands in `Add` order; `CommandText` equals a literal; parameter values in order equal literals (incl. `"Unresolved"`/`"Resolved"`, payload JSON, size, schema version, nulls). Empty `SaveAsync` -> no command; a second `SaveAsync` after success -> no command; first command throwing -> exception propagates (type + message), second row not attempted, and a following `SaveAsync` issues both again. | (a) transpose `{resourceKind}` and `{resourceIdentifier}`; (b) move `pending.Clear()` before the loop |
| C6 | `tests/Architecture.Tests/Persistence/AuditChunkArchiverCharacterisationTests.cs` | Real `AzureBlobAuditChunkArchiver` with hand-written subclasses of `BlobServiceClient`/`BlobContainerClient`/`BlobClient` recording calls in order, rows fed by a `DbCommandInterceptor` returning a `DataTable.CreateDataReader()` from `ReaderExecutingAsync` (open suppressed), and a recording `ILogger<AzureBlobAuditChunkArchiver>`. Branches: 404 probe -> call order `GetBlobContainerClient("<literal>")`, `CreateIfNotExists`, `GetBlobClient("fab=_unscoped/year=2026/month=03/chunk-<N>.ndjson.gz")`, SELECT (command text literal, window `>=`/`<`), `GetProperties`, `Upload` with `ContentType "application/x-ndjson"`; decompressed body equals the two-line NDJSON literal (all twelve fields, nulls included); MD5 relation; result `(key, md5hex, 2, false)`; log text literal. Matching stored hash -> no upload, `AlreadyArchived` true, `ChunkAlreadyArchived` text. Different stored hash -> upload, `false`. Probe `RequestFailedException(500)` -> propagates (type, status, message), no upload, no log. Upload throws -> propagates, no `ArchivedAuditChunk` log. | (a) replace the hash comparison with `false` (always upload); (b) swap the two `logger.*` calls' result flags (`AlreadyArchived: true` on the upload path) |

Each mutation is applied locally, the owning test observed red (output quoted), and reverted —
never committed. A characterisation test that arrives red on unrefactored code is a test defect,
fixed in the test. **C5/C6 feasibility (spec J3):** if the interceptor seam cannot be made to work,
the test-writer stops that file, reports the verbatim failure, and the orchestrator moves hit 9 or 8
to "Leave"; the matching refactor task is dropped. None of these classes is in `Integration.Tests`,
so no `ci-shards/*.filter` entry is needed. No DI module is touched, so no registration
characterisation is needed.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/AuditObservability/Infrastructure/SmartSentinelEye.AuditObservability.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep AuditObservability
```

Before (`8b1840bd`): 11 lines (spec §2). After: the 4 "Leave" rows (`AuditingMessageHandler.cs:33`,
`Log.cs:11`, `Log.cs:23`, the migration). Compare by member.

```sh
dotnet test tests/AuditObservability.Domain.Tests -c Release       # 95 passed on 8b1840bd
dotnet test tests/AuditObservability.Application.Tests -c Release  # 84 passed on 8b1840bd
dotnet test tests/Architecture.Tests -c Release                    # 759 passed on 8b1840bd
dotnet ef migrations has-pending-model-changes --project src/AuditObservability/Infrastructure/SmartSentinelEye.AuditObservability.Infrastructure.csproj
#   -> "No changes have been made to the model since the last migration." on 8b1840bd
dotnet format src/AuditObservability/Application/SmartSentinelEye.AuditObservability.Application.csproj --verify-no-changes
dotnet format src/AuditObservability/Infrastructure/SmartSentinelEye.AuditObservability.Infrastructure.csproj --verify-no-changes
```

Stop any running AppHost before building. Integration.Tests (`AuditObservability/*`) run in CI.
Latency: NFR-002 — download the TRX of the last green `develop` CI run and quote
`NFR002_AuditSearchLatencyTests`' "GET /audit over a 24h window ... p50 = ..., p99 = ..." line as
*before*; quote it from this PR's run and one re-run as *after*. NFR-001 — state that no CI figure
exists (`Category=Measurement`); do not fabricate one.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A query helper typed `IEnumerable` moves filtering client-side | FR-005; signatures fixed in §3.1/§3.2; CI's `CrossFabReadGuardIntegrationTests` / `NFR002` exercise EF translation |
| Fab scope widened or narrowed in `ScopeToFabs` | C1 (b)/(c) mutations; security review (tasks T013) |
| Cursor boundary becomes inclusive or the next cursor moves one row | C2/C3 mutations |
| Exception vs empty-page precedence changes | C1 (d) |
| Raw SQL text or slot order changes, or the literal is hoisted into a `string` | C5 literal text and values; security review |
| Per-event allocation added on ingest | FR-004: helper not `async`; review checks |
| Archiver returns before the upload completes, or swallows a failure, before a drop | §3.5 keeps `await` before log/return; C6 failure branches |
| EF configuration reorder alters the model | C4; `has-pending-model-changes` |
| A moved comment's reason no longer holds | §3.6 table, re-read by the reviewer |
| Collision with #2907 / #2893 | No shared file (§1) |
