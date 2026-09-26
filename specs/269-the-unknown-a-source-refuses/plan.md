# Plan 269 — The unknown a source refuses

**Phase:** 2 (Plan) · **Drafted:** 2026-09-26 · **Re-verified:** 2026-09-27 · **Spec:** `./spec.md`
**Issue:** #2324 · **Reads:** spec 143's registry (#1972, PR #2327, merged)
**Branch:** `feat/2324-strict-discovery-source-mode` · **Tree read at:** `2f477474`

**ADRs:**
- ADR-0000 decision 018
- ADR-0092 / 0093 (Domain and Application layout)
- ADR-0038 / 0046 / 0066 (value objects); ADR-0039 / 0090 (Guid v7 + `Identifier`
  suffix); ADR-0091 / 0094 (naming)
- ADR-0047 / 0089 (`Result` + `ApiError`); ADR-0105 (`Ensure.That`)
- ADR-0070 (minimal APIs); ADR-0042 / 0057 (hand-rolled handlers); ADR-0040 / 0073
  (domain events are not integration events)
- ADR-0043 / 0113 (two-layer concurrency); ADR-0114 (fab resolution); ADR-0142 /
  0143 (idempotency and retry safety)
- ADR-0067 (`MigrationRunner`); ADR-0051 (per-context DI)
- ADR-0103 (Aspire fixture); ADR-0065 (coverage); ADR-0084 (metrics); ADR-0109
  (disjoint files)
- ADR-0117 (§VII binds implemented legs); ADR-0139 / 0144 (red-first); ADR-0141
  (`Option<T>`); ADR-0036 (no speculative generality)

**Constitution:** §II, §III bullet 5, **§IV (engaged, §9 and spec §7)**, §VIII
(status correction, spec FR-016), §Testing.

---

## 0. Disposition of the pre-existing work (spec §0.4)

| Artefact | Disposition | Why |
|---|---|---|
| `src/EventIngestion/Domain/SourceMode/{SourceMode, SourceModeIdentifier, EventTypeMode, DeclaredAt, Declaration, ISourceModeRepository}.cs` and `Events/SourceMode{Declared,Changed}DomainEvent.cs` (staged) | **Kept as T001, unchanged** | Already keyed on `(FabIdentifier, Source)`, reusing `Domain/Event/Source.cs`; follows ADR-0092 (per-aggregate folder + `Events/`), ADR-0090 (Guid v7 `Identifier`), ADR-0066 (`IValueObject<T>`), ADR-0105 guards, ADR-0141 (`Option<SourceMode>` from the repository). Behaviour is withheld on purpose (§9). |
| `tests/Architecture.Tests/PrimitiveBoundaryTests.cs` (staged) | **Reshaped during the rebase** | The draft bumped 12 → 13; develop's spec 258 had meanwhile made it 13 with `Wall`. Resolved to develop's text plus `SourceMode`: **14** roots, doc comment "Thirteen aggregates" + `AuditEvent`. |
| Stash *"T002-in-progress"* (Application skeleton, `EventTypeAdmission*`, handler wiring, fakes) | **Not applied** | Phase-4 code; phase 3 does not write it. The engineer may consult it; it was written against `b23b9307` and must be re-checked against this plan, not trusted. |
| `specs/267-…` (in the same stash) | **Superseded by this directory** | Renumbered; content carried forward and re-verified. |

T001 is therefore **present but uncommitted** on the branch. Phase 4 verifies its
"Done when" (tasks T001) and commits it as T001's commit; it does not rewrite it.

## 1. Bounded context and layers

One context, EventIngestion. There is no cross-context reference and no
`Shared.Contracts` change, so **no `AllowedCrossContext` entry is needed and the
NetArchTest boundary rules are untouched**.

| Layer | Added | Changed |
|---|---|---|
| `Domain/SourceMode/` | aggregate, 4 VOs, repository interface, 2 domain events | — |
| `Application/Ingress/` | `EventTypeAdmission`, `EventTypeVerdicts`, `IEventTypeAdmissionSource` | — |
| `Application/Commands/` | declare + change commands, handlers, errors | `IngestEventErrors.cs` (one variant + one `Failures` factory); **both ingest handlers** |
| `Application/Queries/` | list query + handler + errors, `ISourceModeQuerySource` | — |
| `Application/DTOs/` | `SourceModeDto` | — |
| `Application/Log.cs` | — | 3 `[LoggerMessage]` entries |
| `Infrastructure/Persistence/` | configuration, repository, query source, admission source, 1 migration | `EventIngestionDbContext.cs` (one `DbSet`) |
| `Infrastructure/EventIngestionInfrastructureModule.cs` | — | registrations |
| `Api/` | `EventSourcesEndpoints.cs`, 2 request records | `Program.cs` (one map call) |

**Not changed, and a diff that changes them is a review question** (spec FR-009):
- `Api/EventsEndpoints.*.cs`
- `Infrastructure/Ingress/*` (including `PersistenceLoopHostedService.cs`)
- `src/ServiceDefaults/Authorization/Scope.cs`
- `src/AppHost/Realms/*.json`
- anything under `apps/`, `Shared.Contracts`, or §IV's table

**Reused unchanged:**
- From `Domain/Event/`: `Source`, `Kind`, `FabIdentifier`, `EventEnvelope`
- Kernel and shared: `OperatorIdentifier`, `AggregateVersion`, `IClock`, `Ensure`,
  `Result`, `Option`, `ApiError`
- Api helpers: `EventIngestionFabResolution`, `ConcurrencyHeaders`,
  `IdempotencyHeaders`, `IdempotentRequest`
- Idempotency storage: `IdempotencyStore<EventIngestionDbContext>` (registered) and
  the `idempotency_keys` table (migration `20260903094940_AddIdempotencyKey`)
- Exception handlers: `UniqueConstraintExceptionHandler`,
  `FabAuthorizationExceptionHandler`
- The spec 143 registry table and its partial index
  `ux_registered_event_types_fab_kind`

---

## 2. Entities and value objects

### `SourceMode` — `src/EventIngestion/Domain/SourceMode/SourceMode.cs`

```
AggregateRoot<SourceModeIdentifier>
  Fab          : FabIdentifier   (fixed at declaration)
  Source       : Source          (fixed at declaration; the existing closed VO)
  Mode         : EventTypeMode   (strict | discovery)
  Declaration  : Declaration     (DeclaredAt + DeclaredBy — the latest change)
  Version      : AggregateVersion (AggregateRoot / AggregateVersionInterceptor)
```

It follows `RegisteredEventType` exactly: a private parameterless constructor for
EF, `null!` initialisers, `Ensure.That(...)` guards first, `Raise(...)` last.

- `static SourceMode Declare(FabIdentifier fab, Source source, EventTypeMode mode,
  OperatorIdentifier declaredBy, IClock clock)`. It mints `Id` with
  `SourceModeIdentifier.New()` and raises `SourceModeDeclaredDomainEvent`.
- `void Change(EventTypeMode mode, OperatorIdentifier changedBy, IClock clock)`.
  **Idempotent by early return** when `mode == Mode`: no event, no field
  assignment, so the interceptor sees nothing modified and the version does not
  bump (spec FR-011). Otherwise it sets `Mode`, replaces `Declaration`, and raises
  `SourceModeChangedDomainEvent` carrying the previous and the new mode.

**Why a surrogate identifier and not a natural key.** A natural `(fab, source)` key
was considered first and **rejected for a mechanical reason, not a preference**:
- `AggregateRoot<TIdentifier>` constrains `TIdentifier : struct,
  IStronglyTypedId<Guid>`.
- `AggregateVersionInterceptor` bumps only `IVersionedAggregate` entities, so a
  non-aggregate settings row would have no version for `If-Match` to compare.

The natural key is enforced where it belongs, as a **total** unique index
(§4).

**No primitives on the model** (§II). `PrimitiveBoundaryTests` checks this.

### Value objects

| Type | File | Shape |
|---|---|---|
| `SourceModeIdentifier` | `SourceModeIdentifier.cs` | copy `RegisteredEventTypeIdentifier` |
| `EventTypeMode` | `EventTypeMode.cs` | `sealed record(string Value) : IValueObject<string>`; statics `Strict` (`"strict"`) and `Discovery` (`"discovery"`); a throwing `From`; `ToString()` returns `Value`. Copy `RegistrationState` in shape. |
| `DeclaredAt` | `DeclaredAt.cs` | copy `Domain/RegisteredEventType/RegisteredAt.cs` (UTC-normalised, `IComparable`, implicit → `DateTimeOffset`) |
| `Declaration` | `Declaration.cs` | `sealed record Declaration(DeclaredAt DeclaredAt, OperatorIdentifier DeclaredBy)` with `From(...)`; copy `Registration.cs` |

**`EventTypeMode` is lowercase and `RegistrationState` is PascalCase.** The mode is
also a **wire** token, in request bodies and in `GET` output, and it matches
`Source`'s lowercase tokens and decision 018's own words. `RegistrationState` is
never typed by a caller. Do not "fix" either toward the other.

### Repository — `ISourceModeRepository.cs`

```csharp
Task<Option<SourceMode>> GetAsync(FabIdentifier fab, Source source, CancellationToken cancellationToken);
void Add(SourceMode sourceMode);
Task SaveAsync(CancellationToken cancellationToken);
```

This is for the **write** side only (declare and change). The ingest path does
not use it (§6).

### Domain events — `Events/`

- `SourceModeDeclaredDomainEvent(SourceModeIdentifier Identifier, FabIdentifier
  Fab, Source Source, EventTypeMode Mode, DateTimeOffset DeclaredAt,
  OperatorIdentifier DeclaredBy)`
- `SourceModeChangedDomainEvent(SourceModeIdentifier Identifier, FabIdentifier Fab,
  Source Source, EventTypeMode From, EventTypeMode To, DateTimeOffset ChangedAt,
  OperatorIdentifier ChangedBy)`

**Both are unconsumed, on purpose** (spec FR-014; precedent spec 143 FR-012).

---

## 3. Invariants

1. **At most one declaration per `(fab, source)`.** This is enforced twice (spec
   086): the declare handler's `GetAsync`, and `ux_source_modes_fab_source`.
2. **An undeclared pair is discovery** (spec FR-003). No code path may treat
   absence as strict. Phase 6 counterfactual 3 proves this.
3. **Strict consults the registry with spec 143's definition of registered**:
   same fab, same kind, `State == Registered`.
4. **The admission check never costs one query per event in a batch** (spec
   FR-006, spec 020 FR-010).
5. **Precedence is redelivery, then future skew, then unregistered type**, and it
   is the same on both paths (spec FR-007).
6. **Discovery is byte-for-byte today's path.** Every existing ingest test passes
   with no assertion changed (§9, the characterisation half).
7. **No primitive reaches the domain model** (§II).

---

## 4. Messaging and persistence

**Messaging: none.** There is no integration event, no subscriber and no
`Shared.Contracts` change. The repository commits through `ITransactionalCommit`,
the same as `RegisteredEventTypeRepository`.

**Table `source_modes`**, EF Core, plain CRUD (ADR-0130):

```
source_mode_id   uuid          PK, ValueGeneratedNever
fab              varchar(FabIdentifier.MaximumLength)  NOT NULL
source           varchar(16)                            NOT NULL   (EventConfiguration.cs:45 uses 16)
mode             varchar(16)                            NOT NULL
declared_at      timestamptz                            NOT NULL
declared_by      uuid                                   NOT NULL
version          integer   IsConcurrencyToken()          NOT NULL

ux_source_modes_fab_source   UNIQUE (fab, source)     -- total: no lifecycle, no filter
```

- `Declaration` is mapped with `OwnsOne` + `Navigation(...).IsRequired()`. Copy
  `RegisteredEventTypeConfiguration`, which copies `VariableConfiguration.cs:86,98`.
- End with `builder.Ignore(x => x.PendingEvents)`.
- **No second index on `fab`.** The unique index leads with `fab`, so it serves
  the ingest lookup's `fab IN (…)`.

**One migration**: `dotnet ef migrations add SourceModes --project
src/EventIngestion/Infrastructure`.
- `.Designer.cs` and the snapshot are **generated, never hand-written**.
- `ArgumentNullException.ThrowIfNull(migrationBuilder)` at the top of `Up` and
  `Down`: the generated-migration house style, which ADR-0105 excepts.
- Nothing is back-filled (spec FR-003). No `IdempotencyKeyTable.Create`: the table
  exists.
- **Not partitioned**, and not added to `FabPartitionProvisioner`.

---

## 5. API surface

A new file, `src/EventIngestion/Api/EventSourcesEndpoints.cs`, mapped from
`Program.cs` next to `MapEventTypesEndpoints()`.

```csharp
RouteGroupBuilder group = app.MapGroup("/event-sources").WithTags("EventIngestion");
```

Scopes go on the **individual mappings**. The group carries two scopes, and
`EndpointScopeDeclarationTests` requires each mapping to declare its own `403`.

| Route | Scope | Codes declared in its own chain |
|---|---|---|
| `POST /event-sources` | `Scope.Sse.Events.TypesWrite` | 201, 400, 401, 403, 409 |
| `GET /event-sources` | `Scope.Sse.Events.Read` | 200, 400, 401, 403 |
| `PUT /event-sources/{source}/mode` | `Scope.Sse.Events.TypesWrite` | 200, 400, 401, 403, 404, 409, 428 |

**`Scope.Sse.Events.TypesWrite` is a flat constant**, not `Events.Types.Write`.
`Scope.cs:74-84` records why the nested shape from plan 143 was not used.

**Build-enforced rules** (`tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`):
1. Each `.WithSummary` contains the literal `Required scope: <exact string>`.
   `GET`'s summary also says *"an undeclared source is discovery"* (spec FR-012).
2. Each mapping has `.ProducesProblem(StatusCodes.Status403Forbidden)` in its own
   chain.
3. **Pinned counts.** They are `EndpointFileCount = 14` and
   `RouteHandlerMappingCount = 65` at `2f477474` (they were 13 / 60 when this plan
   was first drafted at `b23b9307`; spec 258's walls moved them — exactly the drift
   this note exists for), and this spec makes them **15** and **68**. **Re-read
   both constants when the task runs.** The bump is `+1` / `+3` on whatever is
   there.

**Handler bodies follow `EventTypesEndpoints`:**
- Parse `Source.From` and `EventTypeMode.From`, for both the body and the
  `{source}` route value, inside `try { } catch (ArgumentException ex)`. The catch
  answers `400 SOURCE_MODE_INVALID_INPUT` with `detail: ex.Message`.
- `POST` and `PUT` call `ResolveWriteFabAsync`. `GET` calls `ResolveReadFabsAsync`.
- `PUT` reads `ConcurrencyHeaders.TryReadExpectedVersion(...)` **before** the fab
  resolution and before any lookup, and returns its `428` (spec FR-011).
- `POST` uses `IdempotencyHeaders.TryRead` and `IdempotentRequest.ExecuteCreateAsync`
  with `IdempotencyScope.For(key, "POST /event-sources",
  user.ToOperatorIdentifier().Value.ToString())`. The `Location` is
  `/event-sources/{source}`.
- `PUT` answers `200` with the `SourceModeIdentifier` (bare `Guid`), mirroring
  `SetSystemVariableValue`.
- Every handler ends `result.Match<IResult>(onSuccess: …, onFailure: error =>
  error.ToProblem())`.
- Where the parameter count would exceed four (ADR-0084), use a `[AsParameters]`
  services record, as `EventTypesEndpoints` and `SystemVariableEndpoints` do.

**Request records** (primitives are correct on a wire shape):
- `Api/Requests/DeclareSourceModeRequest.cs`: `sealed record(string Source, string Mode)`
- `Api/Requests/ChangeSourceModeRequest.cs`: `sealed record(string Mode)`

---

## 6. Application layer

### 6.1 The shared ingest collaborator

**`Application/Ingress/IEventTypeAdmissionSource.cs`** is the read port. Its
Infrastructure implementation mirrors `IFabStorageReadiness` /
`CatalogFabStorageReadiness`:

```csharp
public interface IEventTypeAdmissionSource
{
    /// The (fab, source) pairs among these fabs that are declared strict.
    Task<IReadOnlySet<(FabIdentifier Fab, Source Source)>> StrictSourcesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken);

    /// Those of these kinds that are Registered (not Retired) for this fab.
    Task<IReadOnlySet<Kind>> RegisteredKindsAsync(
        FabIdentifier fab, IReadOnlyCollection<Kind> kinds, CancellationToken cancellationToken);
}
```

**`Application/Ingress/EventTypeAdmission.cs`** is a concrete `sealed class`
registered **scoped**. It is not an interface: the read port above is the test
seam, and a second seam would be speculative (ADR-0036).

```csharp
public sealed class EventTypeAdmission(IEventTypeAdmissionSource source)
{
    public Task<EventTypeVerdicts> AssessAsync(
        IReadOnlyCollection<EventEnvelope> envelopes, CancellationToken cancellationToken);
}
```

`AssessAsync` works in four steps:
1. An empty input returns `EventTypeVerdicts.AdmitAll`.
2. Call `StrictSourcesAsync` with the distinct fabs of the input. **If the result
   is empty, return `AdmitAll`.** This is the default case: one query.
3. Otherwise, group the envelopes whose `(Fab, Source)` is strict by fab. For each
   such fab, call `RegisteredKindsAsync` **once** with the distinct kinds.
4. Return the verdicts.

**`Application/Ingress/EventTypeVerdicts.cs`** is a `sealed class`:
- `static EventTypeVerdicts AdmitAll { get; }`
- `bool Refuses(EventEnvelope envelope)`, which is true iff the envelope's
  `(Fab, Source)` is strict **and** its `Kind` is not in that fab's registered set.

The verdict depends only on `(fab, source, kind)`, so duplicates in a batch get the
same answer.

**Why not `IRegisteredEventTypeRepository.GetRegisteredAsync`,** which #2324's
framing suggests:
- It returns a **tracked** `RegisteredEventType`, so every ingest would attach an
  entity to the scoped `DbContext` that the `EventRepository` then commits
  through.
- It is keyed on a single `(fab, kind)`, so the batch would pay one round trip per
  distinct kind. Spec 020 FR-010 forbids exactly that shape.

`RegisteredKindsAsync` reads the same table with the same predicate
(`State == RegistrationState.Registered`) as a no-tracking set query. Invariant 3
is what keeps the two in agreement, and a test pins it (tasks T003c.7).

### 6.2 The two insertion points

Both handlers gain a constructor dependency, `EventTypeAdmission admission`.

**`IngestEventCommandHandler.HandleAsync`**, in order:
1. The existing `ExistsAsync` redelivery check, unchanged and **still first**.
2. `EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], cancellationToken);`
3. The existing `EventAggregate.Ingest(...)` in `try`/`catch (ArgumentException)`.
   The future-skew refusal returns here, **before** the verdict is consulted
   (precedence, spec FR-007).
4. `if (verdicts.Refuses(envelope))`: log `UnregisteredEventTypeRefused`, then
   `return Failure(IngestEventFailures.EventTypeNotRegistered(envelope.Fab.Value,
   envelope.Source.Value, envelope.Kind.Value));`. The built aggregate is discarded
   unadded, so its pending `EventIngestedDomainEvent` is never dispatched.
5. `events.Add`, `SaveAsync`, `IngestVolume.Record`, all unchanged. A refusal is
   not counted, the same as a skew refusal.

**`IngestEventBatchCommandHandler.HandleAsync`**:
1. The existing `ExistingAsync` pre-pass is unchanged.
2. **Then one** `EventTypeVerdicts verdicts = await admission.AssessAsync(envelopes,
   cancellationToken);` for the whole batch, before the loop.
3. The loop is unchanged except that it calls `Build(envelope, verdicts)`.

`Build` stays **synchronous**. After the existing skew `try`/`catch`, it checks
`verdicts.Refuses(envelope)`. A refusal becomes
`Failure(IngestEventFailures.EventTypeNotRegistered(...))` and is logged through
the **existing** `BatchEnvelopeRejected(identifier, source, device, reason.Code)`
message. It flows out as a `RefusedEnvelope`, and the persistence loop
dead-letters it as it does a skew refusal today.

**Why the assessment runs before `Ingest` rather than after:**
- The batch needs it outside the synchronous per-envelope `Build`.
- Running it in the same place on both paths keeps the two paths' costs and
  failure behaviour identical.

The consequence for §IV is in §9.

**Failure of the lookup itself** (the database is unreachable) is **not caught**.
It throws out of the handler exactly as `ExistsAsync` would:
- HTTP: `EVENT_NOT_STORED` 503.
- Batch: fall back to singles.
- Singles: `Ending.Failed`, carried and retried.

Swallowing it and admitting would open a strict source during an outage. Answering
strict-refusal would dead-letter valid events for ever.

### 6.3 `IngestEventErrors.cs`

```csharp
public sealed record EventTypeNotRegistered(string Fab, string Source, string Kind)
    : IngestEventError(
        "EVENT_TYPE_NOT_REGISTERED",
        $"Event type '{Kind}' is not registered for fab '{Fab}', and source '{Source}' is strict.",
        HttpStatusCode.BadRequest);
```

It comes with a matching `IngestEventFailures.EventTypeNotRegistered(...)`. The
message is at most about 250 characters, under `RejectionReason.MaximumLength`
(512).

### 6.4 Commands and query

Per ADR-0093, each has a `Handlers/` subfolder and a paired `*Errors.cs` with a
`*Failures` static.
- **`DeclareSourceModeCommand(FabIdentifier Fab, Source Source, EventTypeMode Mode,
  OperatorIdentifier DeclaredBy)`** → `Result<SourceModeIdentifier,
  DeclareSourceModeError>`.
  1. Guard, then **deconstruct** (house rule).
  2. `GetAsync(fab, source)`: when it has a value, return
     `SourceModeAlreadyDeclared` (409).
  3. Otherwise `Declare`, `Add`, `SaveAsync`, log, and return `Success`.
- **`ChangeSourceModeCommand(FabIdentifier Fab, Source Source, EventTypeMode Mode,
  int ExpectedVersion, OperatorIdentifier ChangedBy)`** → `Result<SourceModeIdentifier,
  ChangeSourceModeError>`. The order is load-bearing and copied from
  `RetireEventTypeCommandHandler`:
  1. Lookup: `None` returns `SourceModeNotDeclared` (404).
  2. `Version != expectedVersion` returns `SourceModeStale` (409).
  3. `Change`, `SaveAsync`, `Success`.
- **`ListSourceModesQuery(IReadOnlyList<FabIdentifier> Fabs)`** →
  `Result<IReadOnlyList<SourceModeDto>, ListSourceModesError>` through
  `ISourceModeQuerySource`.
  - It filters `Fabs.Contains(x.Fab)` and orders by fab, then source.
  - **Compare against the value objects directly.** `.Value` member access does not
    translate (`ListEventsTranslationTests`).

| Code | Status | Produced by |
|---|---|---|
| `SOURCE_MODE_ALREADY_DECLARED` | 409 | declare handler |
| `SOURCE_MODE_NOT_DECLARED` | 404 | change handler |
| `SOURCE_MODE_STALE` | 409 | change handler |
| `SOURCE_MODE_INVALID_INPUT` | 400 | endpoint |
| `EVENT_FAB_REQUIRED` | 400 | endpoint (existing) |
| `EVENT_TYPE_NOT_REGISTERED` | 400 | both ingest handlers |

**DTO:** `SourceModeDto(Guid SourceModeId, string Fab, string Source, string Mode,
DateTimeOffset DeclaredAt, Guid DeclaredBy, int Version)`.

**Logging** (`Application/Log.cs`, ADR-0050), three entries, structured and with no
interpolation:
- `SourceModeDeclared`
- `SourceModeChanged`
- `UnregisteredEventTypeRefused(EventIdentifier, FabIdentifier, Source, Kind)`,
  for the single path only; the batch reuses `BatchEnvelopeRejected`

---

## 7. Infrastructure

- `Persistence/Configurations/SourceModeConfiguration.cs`: §4.
- `Persistence/SourceModeRepository.cs`: copy `RegisteredEventTypeRepository`,
  including its dispatch-then-commit `SaveAsync`.
- `Persistence/SourceModeQuerySource.cs`: no-tracking.
- `Persistence/EventTypeAdmissionSource.cs` implements `IEventTypeAdmissionSource`.
  Both queries are `AsNoTracking()`.
  - `StrictSourcesAsync` runs `SourceModes.Where(m => fabs.Contains(m.Fab) &&
    m.Mode == EventTypeMode.Strict).Select(m => new { m.Fab, m.Source })`, then
    materialises into a `HashSet`.
  - `RegisteredKindsAsync` runs `RegisteredEventTypes.Where(t => t.Fab == fab &&
    kinds.Contains(t.Kind) && t.State == RegistrationState.Registered).Select(t =>
    t.Kind)`.
- `EventIngestionDbContext.cs`: `DbSet<SourceMode> SourceModes`.
- `EventIngestionInfrastructureModule.cs`:
  - `ISourceModeRepository`, `ISourceModeQuerySource` and
    `IEventTypeAdmissionSource`, **scoped**, because they share the scoped
    `DbContext`
  - `EventTypeAdmission`, **scoped**
  - the three new handlers, as the concrete type and as the
    `ICommandHandler<,>` / `IQueryHandler<,>` binding

**A DI trap that is already in the test suite.** The ingest handlers are resolved
from a scope in `EventsEndpoints.Writes.cs` and `PersistenceLoopHostedService`.
Those follow the module automatically. **But
`tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs:494-495`
registers both ingest handlers by hand.** It needs `EventTypeAdmission` plus a fake
`IEventTypeAdmissionSource` that admits all, or every test in it fails at
resolution. That is a DI error, not a red. The prelude owns this edit (tasks T002).

**The same trap in the Application tests** (re-counted at `2f477474`):
`IngestEventCommandHandlerTests` constructs the handler in its `Handler(...)`
factory (line 31) **and directly at lines 39, 57 and 77**;
`IngestEventBatchCommandHandlerTests` has `Handler(...)` (line 35) and
`SingleHandler(...)` (line 39). Every one of those constructor calls gains the
admission argument in T002, and nothing else in those files changes.

---

## 8. File ownership and collisions (ADR-0109)

**Shared files. Every task touching one is serial with every other task touching
it:**

| File | Touched by |
|---|---|
| `Application/Commands/Handlers/IngestEventCommandHandler.cs` | prelude (constructor), wiring |
| `Application/Commands/Handlers/IngestEventBatchCommandHandler.cs` | prelude (constructor), wiring |
| `Application/Commands/IngestEventErrors.cs` | prelude (the variant is data) |
| `Application/Log.cs` | logging |
| `Infrastructure/Persistence/EventIngestionDbContext.cs` | `DbSet` |
| `…/Migrations/EventIngestionDbContextModelSnapshot.cs` | migration (generated) |
| `Infrastructure/EventIngestionInfrastructureModule.cs` | prelude (admission + stub source), persistence, handlers |
| `Api/Program.cs` | one map call |
| `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs` | pinned counts |
| `tests/Integration.Tests/ci-shards/shard-N.filter` | one entry per new integration test class |
| `tests/EventIngestion.Application.Tests/Commands/IngestEvent*HandlerTests.cs` | prelude: **constructor argument only** |
| `tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs` | prelude: DI registration only |
| `.specify/memory/constitution.md` | FR-016 |

**Genuinely disjoint, and therefore `[P]`:** the Domain folder (all new) and each
new Phase B test file.

**This is a single-engineer slice.** The `[P]` markers name file ownership more
than they promise speed.

---

## 9. Phase 4a colour: **RED**, with a characterisation net underneath

### Declaration

**Behaviour-changing, so RED.** An unregistered kind on a strict source is now
refused where it was always accepted, and three endpoints and a table are new.
ADR-0144's ambiguity rule is not needed.

**The characterisation half is the existing ingest suite.** Discovery is the
default (spec FR-003), so **every existing test in
`IngestEventCommandHandlerTests`, `IngestEventBatchCommandHandlerTests`,
`PersistenceLoopHostedServiceTests` and the EventIngestion integration tests must
pass with no assertion edited.**
- The only permitted edit to any of them is the prelude's constructor or DI line.
- An assertion that has to change is evidence the default moved, and blocks the PR.
- The 4a note quotes this suite green **after the prelude and before Phase C**,
  and again after Phase C.

### The prelude and why it exists

A test referencing `SourceMode` before it exists fails `CS0246`, and **a compile
error is not a red test** (spec 061). A handler constructor that takes a
dependency DI cannot resolve makes every ingest integration test fail at
resolution, and that is not a red either. The prelude (tasks T001, T002) therefore
lands:
- Domain types with the behaviour absent:
  - `Declare` leaves `Mode` and `Declaration` at `null!` and raises nothing.
  - `Change` is its guards and nothing else.
  - `EventTypeMode.From` returns `Discovery` unconditionally.
- Application skeletons that answer without acting:
  - declare returns `Success(SourceModeIdentifier.New())` without touching a
    repository;
  - change returns `SourceModeNotDeclared`;
  - list returns `[]`.
- **`EventTypeAdmission.AssessAsync` returns `AdmitAll` without calling its source**,
  and `EventTypeVerdicts.Refuses` returns `false`.
- The **fully wired** handler call sites (steps 2 and 4 in §6.2), because with
  `AdmitAll` they change nothing observable.
- The `EventTypeNotRegistered` variant, which is data.
- An `EventTypeAdmissionSource` in Infrastructure whose methods return empty sets
  without querying, plus its DI registration.

After the prelude **the characterisation suite is green and nothing new is
correct.**

### The written prediction

Phase 4a must quote its **actual** output against this list:
- **Domain.** `Declare_sets_the_declared_mode` fails comparing `null` to `strict`.
  `Declare_raises_a_SourceModeDeclaredDomainEvent` fails on empty `PendingEvents`.
  `Change_to_a_different_mode_flips_it_and_raises` fails.
  `EventTypeMode_From_refuses_an_unknown_mode` fails with no exception thrown.
- **Admission.** Every `strict`-and-unregistered case fails expecting `Refuses` =
  `true` and getting `false`. `The_default_case_costs_one_query` fails because the
  fake source records **zero** calls, not one.
- **Ingest handlers.** `A_strict_source_refuses_an_unregistered_kind` fails
  expecting `EVENT_TYPE_NOT_REGISTERED` and getting `Success`, on both handlers.
- **Integration.** Every `/event-sources` request returns `404` because the group
  is unmapped. The strict-ingest tests fail at their arrange step
  (`POST /event-sources` → 404), **and each must assert that status first with a
  message**, so the quoted red names the real cause.

**Expected green on arrival, and documented as such:**
- the guard tests on `Declare` and `Change`
- every "undeclared/discovery source admits" case, which *is* the characterisation
- the spec §4 default scenario

Anything else green on first run is a phase-4 failure.

### Counterfactuals: phase 6, each expected to turn exactly the named test red

1. Key `StrictSourcesAsync` on fab only, dropping `source`:
   `Strict_on_one_source_leaves_the_fabs_other_sources_open` goes red.
2. Drop `State == Registered` from `RegisteredKindsAsync`:
   `A_retired_kind_is_refused_under_strict` goes red.
3. Treat an absent row as strict:
   `An_undeclared_source_admits_an_unregistered_kind` goes red, along with the
   characterisation suite. **That is the loudest one, and it should be.**
4. Remove the `verdicts.Refuses` check from `Build` only: the MQTT fast-path
   integration test goes red while the HTTP ones stay green. This proves both
   insertion points are covered independently.
5. Move the admission check ahead of the `ExistsAsync` check:
   `A_redelivery_is_still_a_redelivery_under_strict` goes red.
6. Delete the `expectedVersion` comparison: the stale-`PUT` test goes red.
7. Swap `TypesWrite` for `Events.Write` on `POST /event-sources`: the planted-client
   test goes 403 → 201. **Use a planted event-source client**, per spec 143
   FR-010's testing note. A `management-web` token carries every `sse.*` scope and
   cannot show the negative.

---

## 10. Latency (§IV): what the plan commits to, and what phase 5 owes

**Leg:** `Event → overlay state`, ≤ 200 ms. **Engaged.**

**Added work, restating spec §7:**
- Default: +1 no-tracking indexed query per HTTP event, and per MQTT batch.
- Strict: +1 more per strict fab.

**Expectation:** well under 1–2 ms per query on the dev stack, against tables of at
most a few rows per fab. **This is not a figure.**

**Why no cache in this slice (spec A7), stated so phase 5 can overturn it:**
- **Correctness first.** FR-015 says a change applies to the next event stored.
  A per-process cache makes that "the next event after the cache notices", and
  with more than one `event-ingestion` replica it needs an invalidation message —
  a new integration event and a subscriber, i.e. messaging this slice otherwise
  has none of (§4).
- **The cost is one round trip of a shape the path already pays.** Ingest
  already does `ExistsAsync` + insert per event and `ExistingAsync` per batch;
  the default adds one no-tracking index probe. On the batch path, which carries
  the volume, the cost is amortised over the batch.
- **The fallback is named and has a precedent** — an in-memory projection seeded
  at start-up and refreshed on the domain events, the `RuleCacheSeederHostedService`
  shape — and is a follow-up issue filed only on a measured figure (ADR-0036).

**Why the leg histogram cannot show it.** `LatencySegment.EventToOverlayState` is
measured from `IngestedAt`, which `Event.Ingest` stamps. The assessment runs
before that stamp on both paths (§6.2), as `ExistsAsync` already does. **A
flat histogram after this change is not evidence.**

**Phase 5 obligation (tasks T015):**
1. `IngestThroughputMeasurementTests` (category `Measurement`, run locally, not in
   CI), **twice on `develop`, twice on the branch** with the measured pair
   undeclared, and **twice on the branch** with it declared strict and its kinds
   registered. Quote arrival-to-visible p50/p95 and throughput for all six runs.
2. The admission queries' own duration from the ingest trace in the Aspire
   dashboard: the Npgsql/EF span under the ingest activity, over at least a few
   hundred events. **If no such span is emitted**, say so and rely on (1). Do not
   invent a timer for the purpose.
3. Write both into `verification.md` as figures **about this change**. **Do not
   edit §IV's table.** The row stays *recorded, not yet readable*.
4. **If the added p95 is more than a few milliseconds**, file the projection
   follow-up (spec §10) and say so in the PR. Do not build it in this PR.

---

## 11. Coverage, metrics, review

- **Coverage (ADR-0065):** Domain ≥ 90 %, Application ≥ 80 %.
  - `EventTypeAdmission` has five branches: empty input, no strict pairs, strict
    and registered, strict and unregistered, mixed fabs. All are covered by
    T003c.
- **Metrics (ADR-0084):** the risk is `IngestEventCommandHandler.HandleAsync`
  passing 30 LOC. If it does, extract the refusal into a private method. **Do not
  restructure the existing steps** to make room.
- **Review:** constitution §Code Review flags Event Ingestion for `ultrareview`. A
  `security-reviewer` pass is warranted for FR-013 (who may relax a source).

---

## 12. Risks

- **R1 — Shared-database test pollution.** The Aspire stack's database persists
  across runs, and all integration tests share one collection.
  - A test that declares a source strict and dies before restoring it breaks every
    later test that ingests an unregistered kind through that `(fab, source)`,
    permanently.
  - **Mitigation (tasks T003f):**
    - choose pairs no other integration test ingests through, and prove it by
      grep, not by recollection;
    - write a helper that **sets** a mode idempotently (GET → POST-or-PUT);
    - restore `discovery` in `finally`.
- **R2 — Wiring one path and not the other.** The HTTP tests cannot see the batch
  path. Counterfactual 4 and spec §6 step 8 exist for this.
- **R3 — A default that drifted to strict.** Invariant 2, counterfactual 3, and the
  unmodified characterisation suite.
- **R4 — The pinned endpoint counts are found late.** They fail
  `Architecture.Tests`, not the context's own suite. The bump is an explicit
  task.
- **R5 — FR-016.** If the gate rejects it, drop T014. If it is accepted, change
  only the strict/discovery clause.
- **R6 — `CLAUDE.md`'s idempotency count** (*"Nine of the ten creates and
  rotations have a key"*) will not match after this spec adds a keyed create.
  Spec 143 already added one, so the count may already be stale. **This spec does
  not edit `CLAUDE.md`.** It is flagged for a human, per the rule that agents do
  not change repository configuration.

---

## 13. Gate — phase 2

- [ ] Plan aligns with the constitution and the cited ADRs.
- [ ] No cross-context reference; no `Shared.Contracts` change; no ADR.
- [ ] §IV **engaged**. §10's measurement obligation is accepted as phase 5's, and
      §IV's table stays unedited.
- [ ] Phase 4a colour **RED**, with the characterisation half understood: the
      existing ingest suite may change a constructor line and nothing else.
- [ ] §8's shared-file list is the input to `tasks.md`'s `[P]` markers.
