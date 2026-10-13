# Plan 346: The ingest context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2888.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 12 of 26 hits by shape only; the 14 left are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | No Domain file changes. The one new type (J1) is private, in Infrastructure, and holds value objects only. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| §IV (latency) | Event → overlay state leg touched, not changed: no new I/O, `await`, lock or per-event heap allocation (spec §7). |
| ADR-0050 | `Log.cs` files untouched; every log call keeps method, arguments and order. |
| ADR-0051 | `AddEventIngestionInfrastructure` keeps its signature; extractions are private static. |
| ADR-0141 | No new nullable parameter in Application; extracted helpers take the query's existing `Option<T>` fields as they are. |
| House rules | Fluent DI chains in extracted registration methods; `var`; one parameter per line when wrapping; `ListDeadLettersQueryHandler` keeps its deconstruction line as written (`HandlerDeconstructionTests`). |

**Concurrency with #2887 and #2896.** #2887 (`D:\Github\sse-2887`, spec 345, unpushed) touches only
`src/LayoutComposition/**`, `tests/LayoutComposition.*` and one new file
`tests/Architecture.Tests/Persistence/LayoutCompositionRegistrationCharacterisationTests.cs`.
#2896 (spec 341) is merged and touched only `src/*/Api/*.cs`. This plan touches only
`src/EventIngestion/{Application,Infrastructure}` and adds differently named test files. No overlap.

## 2. Shape

One bounded context, two layers (Application, Infrastructure). No new public type, no new project
reference, no migration, no messaging change, no AppHost change.

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (CancellationToken counts), <= 30 lines, complexity
<= 10**, verified with the grep in §5 after each commit. Statements move verbatim; comments move
with the code they explain. Names below are suggestions; behaviour and order are binding.

### 3.1 `ListDeadLettersQueryHandler.HandleAsync` (hit 8)

Keep the guard, the deconstruction line `var (fabs, rawLimit, reason, state) = query;` and the limit
clamp in `HandleAsync`. Extract:

- `private static IQueryable<DeadLetter> Filter(IQueryable<DeadLetter> deadLetters, IReadOnlyList<FabIdentifier> fabs, Option<DeadLetterReason> reason, Option<HoldState> state)`:
  the fab `Where`, then reason, then state, with the `reasonValue`/`stateValue` locals kept (the EF
  closure shape must not change), comments included.
- `private static DeadLetterDto ToDto(DeadLetter deadLetter)`: the nine-field constructor call,
  **argument order unchanged**.

The projection stays **after** `ToListAsync` (in memory). Moving it into the query would change
what EF translates; that is not this spec.

### 3.2 `ListEventsQueryHandler.BuildPagedQuery` (hit 9)

Public static signature unchanged. Keep the fab `Where` (with its FR-001 comment), the cursor block
and the ordering/`Take` in `BuildPagedQuery`. Extract:

- `private static IQueryable<EventAggregate> FilterByOrigin(IQueryable<EventAggregate> source, ListEventsQuery query)`:
  Source, Device, Kind, in that order.
- `private static IQueryable<EventAggregate> FilterByTime(IQueryable<EventAggregate> source, ListEventsQuery query)`:
  OccurredAfter, OccurredBefore, IngestedAfter, IngestedBefore, in that order, each pattern
  (`is { } x` capture vs `query.X` member access) kept exactly as written, because it decides what
  EF parameterises.

### 3.3 `EventIngestionInfrastructureModule.AddEventIngestionInfrastructure` (hit 13)

Body becomes a sequence of private static calls in the existing order, then the Wolverine call and
`return builder;`. Suggested groups (each `IHostApplicationBuilder` or `IServiceCollection`
parameter, one or two):

1. `AddPersistence(builder)`: `AddEventIngestionPersistence`, `EnrichPostgresDbContext`.
2. `AddRepositoriesAndQuerySources(services)`: lines 46-57.
3. `AddCrossCuttingServices(services)`: dispatcher, `IClock`, idempotency store + sweep,
   `TimeProvider`, `IFabStorageReadiness` (comments kept).
4. `AddDomainEventHandlers(services)`.
5. `AddCommandHandlers(services)`: today ~53 lines, so split further, e.g.
   `AddIngestAndWebhookCommandHandlers` and `AddEventTypeAndSourceModeCommandHandlers`.
6. `AddQueryHandlers(services)`.
7. `AddIngestChannel(services)`: `BoundedIngestChannel`, the `IDisposable` gauge factory, the
   `IIngestChannel` factory, `IngestWriteLimiter`, with the #2863 and spec 020/175 comments.
8. `AddIngestTelemetryAndOptions(builder)`: OpenTelemetry meters, `IngestRetryOptions`,
   `IngestWriteOptions`.
9. `AddMqttIngress(builder)`: `ResolveBroker`/`ResolveKeycloak` (still fail-fast at composition,
   same point in the sequence), `MosquittoOptions` post-configure, HTTP client
   `.RetryEveryMethod()` with its ADR-0143 comment, `MqttTokenProvider`, `MosquittoConnectionFactory`,
   then **`MqttSubscriberHostedService` before `PersistenceLoopHostedService`** (hosted-service start
   order).

Registration **order, lifetimes and factory bodies are unchanged**. Converting a column of
`services.AddX<...>();` into one fluent chain is allowed (house rule) and changes no order. The
lambda factories move with their statements; nothing depends on their compiler-generated names.

### 3.4 `MqttSubscriberHostedService.TryParseEnvelope` (hit 14)

Internal static signature unchanged. Introduce (J1)
`private readonly record struct DeliveryAddress(FabIdentifier Fab, Source Source, DeviceIdentifier Device)`.
`TryParseEnvelope` keeps the topic-shape check and the first `try` (address segments **then**
`JsonSerializer.Deserialize`, one `catch`, message prefix `envelope parse failed: `), builds
`new DeliveryAddress(Fab: fab, Source: source, Device: device)` **by name**, and returns
`ToEnvelope(address, payload)`. Extract:

- `private static ParseResult ToEnvelope(DeliveryAddress address, MqttIngressPayload payload)`:
  the `Payload.From` block (`payload rejected: `) then the envelope block (`envelope rejected: `),
  with the FR-015 comment. If it is still over 30 lines, extract the envelope block to
  `private static ParseResult BuildEnvelope(DeliveryAddress address, MqttIngressPayload payload, Payload payloadValue)`.

The `EventEnvelope` constructor call keeps its argument order (`EventIdentifier.From`, fab, source,
device, `Kind.From`, `OccurredAt.From`, payload), which is also the order the value objects throw
in. File LOC rises by about ten from 186; S104 stays far away.

### 3.5 `PersistenceLoopHostedService` (hit 15, S104)

`public sealed class` becomes `public sealed partial class` (primary constructor and attributes stay
in the original file). Move the nested `private enum Outcome` and `private readonly record struct
Ending`, with their doc comments, **verbatim** to a new
`src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.Ending.cs` (same namespace,
`public sealed partial class PersistenceLoopHostedService { ... }`, only the `using`s it needs).
Expected LOC of the original file: about 285. No method body changes. If the measured LOC is still
over 300, move `IsMissingPartition` too; do not split any method.

### 3.6 `CatalogFabStorageReadiness.ReadProvisionedFabsAsync` (hit 17)

Keep the context creation, `OpenConnectionAsync`, the `try/finally` with `CloseConnectionAsync`, and
the disposal order (`await using` context, then command) in `ReadProvisionedFabsAsync`. Extract:

- `private static DbCommand CreateProvisionedFabsCommand(EventIngestionDbContext context, DateTimeOffset now)`:
  the `const string sql` (verbatim, with the grandchildren comment), `CommandText`, the
  `monthSuffix` parameter (name `monthSuffix`, value `$"_{now:yyyyMM}"`). The caller keeps
  `await using var command = CreateProvisionedFabsCommand(context, now);`.
- `private static async Task<FrozenSet<string>> ReadFabNamesAsync(DbDataReader reader, CancellationToken cancellationToken)`
  for the read loop, if still needed to get under 30. The `events_` prefix check and ordinal
  comparer are unchanged.

Uses `context.Database.GetDbConnection().CreateCommand()` exactly as today.

### 3.7 The five EF configurations (hits 18-22)

Each `Configure` keeps `Ensure.That(builder).IsNotNull();`, `ToTable` and `HasKey` as its first
statements (so `EventConfiguration`'s `HasKey` stays at line 30, see spec §6), then calls private
static methods in the existing order, e.g. identity/scalar columns, owned type, version, indexes,
`Ignore(PendingEvents)` last. Every builder call keeps its order and its comment. Each file keeps
its own helpers; no shared helper across files.

### 3.8 `EventRepository.SaveAsync` (hit 23)

The dispatch loop and both `await`s **stay in `SaveAsync`**. Extract, synchronously:

- `private List<IDomainEvent> DrainPendingEvents()`: the `tracked` query, the collect loop with
  `ClearPendingEvents()`, and the "collected first" comment.
- `private static AggregateException NotCommitted(List<Exception> failures, int pendingCount)`:
  builds the exception with the identical message string; `SaveAsync` does
  `if (failures.Count > 0) { throw NotCommitted(failures, pending.Count); }` with the "not
  committed, not rolled back" comment kept beside it.

Order binding: drain, dispatch each (collect non-cancellation exceptions), throw if any, commit.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests |
|---|---|
| `ListDeadLettersQueryHandler` | `ListDeadLettersQueryHandlerTests` (10: order, cap at max, fab scoping x3, reason, state, both, none, DTO fab/reason/kind/state) |
| `ListEventsQueryHandler.BuildPagedQuery` | `ListEventsQueryHandlerTests` (2, paging refusals only); `Integration.Tests/EventIngestion/ListEventsTranslationTests` (offline, `FixtureLogic`, runnable locally); `ListEventsFilteringTests` (CI) |
| Infrastructure module | `IngestVolumeRegistrationTests` (3), `IngestWriteConcurrencyRegistrationTests` (2), `Architecture.Tests` `IdempotencyReservationSweepRegistrationTests` and the `PostgresComposition` suites |
| `TryParseEnvelope` | `MqttEnvelopeParsingTests` (5: well-formed, absent payload, JSON-null payload, string payload, > 64 KB) |
| `PersistenceLoopHostedService` | `PersistenceLoopHostedServiceTests` (15) |
| `CatalogFabStorageReadiness` | `CatalogFabStorageReadinessTests` (4, via the internal seam; do not reach `ReadProvisionedFabsAsync`); CI: `FabStorageRefusalIntegrationTests`, `FabPartitionProvisioningIntegrationTests` |
| EF configurations | none in unit tests; Integration.Tests (CI only) |
| `EventRepository.SaveAsync` | `EventRepositoryOutboxTests` (2: dispatch-before-commit, failed capture commits nothing) |

Gaps, pinned by **new files only** (no existing test edited):

| ID | File | Pins |
|---|---|---|
| C1 | `tests/EventIngestion.Application.Tests/Queries/ListDeadLettersMappingCharacterisationTests.cs` | Limit 0 and -5 -> 100 of 150 rows; every one of the nine DTO fields from distinct values (Id, Topic, RawPayload, Error, RejectedAt, Fab, Reason, Kind, State); an unattributed row's Fab and a parse failure's Kind map to `null` |
| C2 | `tests/EventIngestion.Application.Tests/Queries/ListEventsPagedQueryCharacterisationTests.cs` | `BuildPagedQuery` over an in-memory `AsQueryable()`: fab scoping; each of Source, Device, Kind alone; each of the four time bounds alone and **strict** (an event exactly at the bound is excluded); cursor strict on IngestedAt; order IngestedAt desc then Id desc (two events at one IngestedAt); `pageSize + 1` rows taken |
| C3 | `tests/EventIngestion.Infrastructure.Tests/ListEventsQueryTranslationCharacterisationTests.cs` | With **every** filter and a cursor set, `BuildPagedQuery(context.Events, ...).ToQueryString()` succeeds offline (`UseNpgsql("Host=unused;...")`, no connection) and names `fab_id`, `source`, `device_id`, `kind`, `occurred_at`, `ingested_at`, `ORDER BY` and `LIMIT`. Fragments only, never the full SQL text (an EF patch bump would turn a full-text pin into a standing false failure) |
| C4 | `tests/EventIngestion.Infrastructure.Tests/MqttEnvelopeRefusalCharacterisationTests.cs` | Exact `Error` text (`ShouldBe`, computed from the same value-object call where the inner message is the VO's) for: wrong segment count; first segment not `fab`; illegal fab, source, device segment; malformed JSON; a body that is JSON `null` (`envelope parse failed: payload is null`); a payload the `Payload` rule rejects; empty event id; illegal kind. Precedence: illegal fab **and** malformed JSON -> the fab message; rejected payload **and** illegal kind -> `payload rejected: `. Well-formed with distinct values: each `EventEnvelope` property equals its own input |
| C5 | `tests/EventIngestion.Infrastructure.Tests/EventRepositorySaveCharacterisationTests.cs` | Reusing the `EventRepositoryOutboxTests` harness shape (its fakes are private there, so copy minimal fakes into the new file): three pending events across two aggregates are dispatched **one per call, in order**; an aggregate with no pending events is not dispatched; pending events are cleared after save; second dispatch throws -> all three still offered, `AggregateException` message exactly `"1 of 3 domain event(s) could not be captured; this unit of work is not committed and must be discarded with its scope."`, `InnerExceptions` is the one thrown, commit not called; an `OperationCanceledException` from dispatch propagates as itself, not aggregated |
| C6 | `tests/Architecture.Tests/Persistence/EventIngestionRegistrationCharacterisationTests.cs` | Via `PostgresComposition.Compose("event-ingestion", builder => captured = [.. builder.Services])` (the shape of #2887's `LayoutCompositionRegistrationCharacterisationTests`): the **ordered** list of descriptors whose service type, implementation type (or any generic argument, recursively) is declared in a `SmartSentinelEye.EventIngestion.*` assembly, **or** whose `ImplementationFactory.Method.DeclaringType` is, rendered `"{Lifetime} {ServiceType} -> {ImplementationType | factory | instance}"` (never the factory's compiler-generated method name, which moves with the extraction), equals a literal list captured on `d259668e`. This catches the `IDisposable` gauge factory, the `IIngestChannel` and `IngestWriteLimiter` factories, `IdempotencyStore<EventIngestionDbContext>` and both hosted services in order. Additionally assert, with lifetime: `IClock` -> `SystemClock` singleton, `IDomainEventDispatcher` scoped, `TimeProvider` singleton instance present. Do **not** assert the total descriptor count |
| C7 | `tests/EventIngestion.Infrastructure.Tests/EventIngestionModelCharacterisationTests.cs` | The **logical shape** technique of #2887's `tests/LayoutComposition.Infrastructure.Tests/LayoutCompositionModelCharacterisationTests.cs` (copy its describer: design-time model from `new EventIngestionDbContext(UseNpgsql("Host=unused;..."))`, entities ordered by display name, per property column, store type, CLR type, nullability, max length, converter provider type, explicit default, value generation, concurrency token, shadow; then FKs, indexes with filter, navigations), equal to a literal `ExpectedModel` captured on `d259668e`. **Not** `Model.ToDebugString()`: it embeds provider internals that move on a patch bump |

A characterisation test that arrives red on the unrefactored code is a test defect and is fixed in
the test, never by a production change in this spec. None of these classes is in
`Integration.Tests`, so no `ci-shards/*.filter` entry is needed. `PersistenceLoopHostedService` gets
no new test: its split changes no method body, and its 15 existing tests stay the net.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/EventIngestion/Infrastructure/SmartSentinelEye.EventIngestion.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep EventIngestion
```

Before (`d259668e`): 26 lines (spec §2). After: the 14 "Leave" rows exactly. Line numbers of left
hits do not move (no left hit shares a file with a cleared one).

```sh
dotnet test tests/EventIngestion.Domain.Tests -c Release          # 185 passed on d259668e
dotnet test tests/EventIngestion.Application.Tests -c Release     # 150 passed on d259668e
dotnet test tests/EventIngestion.Infrastructure.Tests -c Release  # 72 passed on d259668e
dotnet test tests/Architecture.Tests -c Release                   # PrimitiveBoundaryTests, HandlerDeconstructionTests, BoundaryTests, PostgresComposition suites
dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~ListEventsTranslationTests"  # offline, no Docker
dotnet format src/EventIngestion/Application/SmartSentinelEye.EventIngestion.Application.csproj --verify-no-changes
dotnet format src/EventIngestion/Infrastructure/SmartSentinelEye.EventIngestion.Infrastructure.csproj --verify-no-changes
```

Integration.Tests `EventIngestion/*`: CI.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A moved `Where` changes what EF parameterises or translates | §3.1/§3.2 keep each closure's capture pattern; C2 semantics + C3 translation + existing `ListEventsTranslationTests` |
| Address/body refusal precedence flips in `TryParseEnvelope` | §3.4 keeps one `try` for segments + JSON; C4 precedence pins |
| Fab/source/device transposed through `DeliveryAddress` | Named-argument construction; C4 distinct-value mapping pin |
| A new `await` or allocation on the ingest hot path | §3.8 keeps dispatch awaits in `SaveAsync`; §3.5 is layout only; spec §7 |
| Dispatch-before-commit order (spec 021) inverted | Existing `EventRepositoryOutboxTests` + C5 |
| DI order change (hosted-service start order, last-wins) | C6 ordered list |
| EF config reorder altering the model | C7 logical shape |
| Catalog read broken with no local test | J3: SQL and parameter moved verbatim; CI `FabStorageRefusalIntegrationTests` quoted green before merge |
| Collision with #2887 | Disjoint `src/` contexts; distinct test file names |
