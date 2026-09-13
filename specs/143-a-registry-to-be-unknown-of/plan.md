# Plan 143 — A registry to be unknown of

**Phase:** 2 (Plan) · **Date:** 2026-09-13 · **Spec:** `./spec.md`
**Issue:** #1972 · **Branch:** `feat/1972-event-registration-registry`
**ADRs:** ADR-0000 decision 018, ADR-0092 (per-aggregate Domain folder), ADR-0093
(per-message-kind Application folder), ADR-0038/0046/0066 (value objects),
ADR-0039/0090 (Guid v7 + `Identifier` suffix), ADR-0091/0094 (no shortcuts;
noun-named identifier properties), ADR-0047/0089 (`Result<T, Error>` + `ApiError`),
ADR-0105 (`Ensure.That`), ADR-0070 (minimal APIs), ADR-0042/0057 (hand-rolled
handlers + Wolverine dispatcher), ADR-0040/0073 (domain ≠ integration events),
ADR-0043/0113 (two-layer optimistic concurrency), ADR-0142/0143 (idempotency,
retry safety), ADR-0067 (MigrationRunner), ADR-0051 (per-context DI extensions),
ADR-0088 (Wolverine per-module defaults), ADR-0103 (Aspire fixture), ADR-0065
(coverage gates), ADR-0084 (code metrics), ADR-0109 (disjoint files), ADR-0139/0144
(red-first; the lane's limits), ADR-0141 (`Option<T>` in Domain/Application).
**Constitution:** §II, §III, §IV (not engaged — spec §7), §VIII, §Testing.

---

## 1. Bounded context and layers

One context, four layers, no cross-context reach. EventIngestion already declares
itself self-contained (`specs/006-event-ingestion/spec.md`, *Cross-Context
Reach*), and this spec adds nothing to `Shared.Contracts`, so **no
`AllowedCrossContext` entry is needed and the NetArchTest boundary rules are
untouched**.

| Layer | Files added | What it owns |
|---|---|---|
| `Domain/RegisteredEventType/` | aggregate, 4 VOs, repository interface, 2 domain events | The membership invariant and the two-state lifecycle |
| `Application/Commands/` + `Queries/` | 2 commands + handlers + errors, 1 query + handler + errors, query-source interface, 1 DTO | Uniqueness lookup, version gate, fab filtering |
| `Infrastructure/Persistence/` | EF configuration, repository, query source, 1 migration | The table, the partial unique index, the concurrency token |
| `Api/` | 1 endpoint file, 1 request record | Scope, fab resolution, `If-Match`, `Idempotency-Key`, `Result` → HTTP |
| `ServiceDefaults/Authorization/` | 2 lines in `Scope.cs` | The new write scope |
| `AppHost/Realms/` | 1 client scope + 1 grant | The scope's existence in Keycloak |

Reused unchanged: `Kind`, `FabIdentifier` (both from `Domain/Event/`),
`OperatorIdentifier`, `AggregateVersion`, `IClock`, `Ensure`, `Result`, `Option`,
`ApiError`, `EventIngestionFabResolution`, `ConcurrencyHeaders`,
`IdempotencyHeaders`, `IdempotentRequest`, `IdempotencyStore<EventIngestionDbContext>`
(already registered), `UniqueConstraintExceptionHandler`,
`FabAuthorizationExceptionHandler`.

**`Kind` and `FabIdentifier` live in the `…Domain.Event` namespace, not a shared
one.** That is the existing arrangement — `WebhookIntegration` imports both the
same way. Do not move them, and do not introduce a second `Kind`.

---

## 2. Entities and value objects

### `RegisteredEventType` — `src/EventIngestion/Domain/RegisteredEventType/RegisteredEventType.cs`

```
AggregateRoot<RegisteredEventTypeIdentifier>
  Fab           : FabIdentifier        (fixed at registration, never reassigned)
  Kind          : Kind                 (the event type being declared)
  State         : RegistrationState    (Registered | Retired)
  Registration  : Registration         (RegisteredAt + RegisteredBy)
  Version       : AggregateVersion     (from AggregateRoot / the EF interceptor)
```

Factory and mutator, both following `WebhookIntegration` exactly — private
parameterless constructor for EF, `null!` initialisers, `Ensure.That(...)` guards
first, `Raise(...)` last:

- `static RegisteredEventType Register(FabIdentifier fab, Kind kind, OperatorIdentifier registeredBy, IClock clock)`
  → mints `Id` with `RegisteredEventTypeIdentifier.New()`, `State =
  RegistrationState.Registered`, raises `EventTypeRegisteredDomainEvent`.
- `void Retire(OperatorIdentifier retiredBy, IClock clock)` → **idempotent by
  early return** when already `Retired` (mirrors `Variable.Archive` and
  `WebhookIntegration.Revoke`); otherwise flips `State` and raises
  `EventTypeRetiredDomainEvent`.

**No primitives on the model** (constitution §II). Every member above is a value
object or an aggregate identifier. `PrimitiveBoundaryTests` fails the build
otherwise, so this is checked rather than remembered.

### Value objects

| Type | File | Shape |
|---|---|---|
| `RegisteredEventTypeIdentifier` | `RegisteredEventTypeIdentifier.cs` | `readonly record struct`, `Guid.CreateVersion7()`, `New()` / `From(Guid)`, `IComparable`, `IStronglyTypedId` — copy `WebhookIntegrationIdentifier` |
| `RegistrationState` | `RegistrationState.cs` | `sealed record(string Value) : IValueObject<string>` with static `Registered` / `Retired` and a throwing `From` — copy `VariableState.cs` verbatim in shape |
| `RegisteredAt` | `RegisteredAt.cs` | `record(DateTimeOffset)`, UTC-normalised, `IComparable`, implicit → `DateTimeOffset` — copy `Domain/WebhookIntegration/RegisteredAt.cs` |
| `Registration` | `Registration.cs` | `sealed record Registration(RegisteredAt RegisteredAt, OperatorIdentifier RegisteredBy)` with `From(...)` — mirrors `SystemVariables`' `Creation` (spec 058: properties that travel together travel as one type) |

**`RegisteredAt` deliberately shares a name with
`Domain/WebhookIntegration/RegisteredAt.cs`.** Different namespaces, different
aggregates, same concept — ADR-0092's per-aggregate folder is exactly the
arrangement that makes this correct rather than a collision. Do not extract a
shared one.

### Repository — `IRegisteredEventTypeRepository.cs`

```csharp
Task<Option<RegisteredEventType>> GetRegisteredAsync(FabIdentifier fab, Kind kind, CancellationToken cancellationToken);
Task<Option<RegisteredEventType>> GetRegisteredAsync(IReadOnlyList<FabIdentifier> fabs, Kind kind, CancellationToken cancellationToken);
void Add(RegisteredEventType eventType);
Task SaveAsync(CancellationToken cancellationToken);
```

`Option<T>` for lookups per ADR-0141/ADR-0048, matching
`IWebhookIntegrationRepository.GetByNameAsync`. The plural-fabs overload is what
makes FR-005's *"cross-fab is a 404"* fall out of the lookup rather than out of a
branch: the retire handler asks only about the caller's fabs, so a row it cannot
see is genuinely absent.

### Domain events — `Events/`

`EventTypeRegisteredDomainEvent(RegisteredEventTypeIdentifier Identifier,
FabIdentifier Fab, Kind Kind, DateTimeOffset RegisteredAt, OperatorIdentifier
RegisteredBy)` and `EventTypeRetiredDomainEvent(RegisteredEventTypeIdentifier
Identifier, FabIdentifier Fab, Kind Kind, DateTimeOffset RetiredAt,
OperatorIdentifier RetiredBy)`.

**Both are unconsumed, on purpose** (spec FR-012). `WebhookIntegrationRegisteredDomainEvent`
and `WebhookIntegrationRevokedDomainEvent` are the precedent in this same context
— grep-verified as having no handler anywhere in `src/` or `tests/` beyond their
own domain assertions. A reviewer who reads an unconsumed domain event as dead
code should read this paragraph and FR-012 first.

---

## 3. Invariants the change must preserve

1. **One registered entry per `(fab, kind)`.** Enforced twice (spec FR-004, spec
   086's rule): the handler's `GetRegisteredAsync` lookup, and
   `ux_registered_event_types_fab_kind` — a **partial** unique index with
   `HasFilter("state <> 'Retired'")`, the same construct as
   `ux_system_variables_*` / `ux_cameras_*` / `ux_layouts_*`.
2. **A fab is never taken from the request unchecked.** All three endpoints go
   through `EventIngestionFabResolution`; none reads `fabId` directly into a
   `FabIdentifier`.
3. **Retirement releases the name.** The index filter is what guarantees it; the
   re-registration path in the acceptance scenarios is what proves it.
4. **Ingest behaviour is byte-for-byte unchanged** (spec FR-013). No file on the
   ingest path is opened.
5. **No primitive reaches the domain model** (§II, `PrimitiveBoundaryTests`).

---

## 4. Messaging and persistence

**Messaging: none.** No integration event, no Wolverine subscriber, no
`Shared.Contracts` change. The repository commits through the same
`ITransactionalCommit` seam `WebhookIntegrationRepository` uses, which dispatches
the outbox; with no integration event raised there is nothing to dispatch, and
that is the same position `WebhookIntegration`'s register/revoke are already in.

**Persistence.** Table `registered_event_types`, EF Core, plain CRUD (ADR-0130 —
Marten is permitted and unused, and nothing here justifies it).

```
registered_event_type_id  uuid    PK, ValueGeneratedNever
fab                       varchar(FabIdentifier.MaximumLength)  NOT NULL
kind                      varchar(Kind.MaximumLength)           NOT NULL
state                     varchar(16)                           NOT NULL
registered_at             timestamptz                           NOT NULL
registered_by             uuid                                  NOT NULL
version                   integer  IsConcurrencyToken()          NOT NULL

ux_registered_event_types_fab_kind  UNIQUE (fab, kind) WHERE state <> 'Retired'
ix_registered_event_types_fab       (fab)
```

`Registration` is mapped with `builder.OwnsOne(eventType => eventType.Registration,
registration => { … })` followed by
`builder.Navigation(eventType => eventType.Registration).IsRequired();` — verified
as the existing shape at `VariableConfiguration.cs:86` and `:98`, where `Creation`
is mapped exactly that way. Copy it; do not invent a second spelling. Finish with
`builder.Ignore(x => x.PendingEvents)`, as every configuration in this assembly
does.

**No `DbSet` discovery problem:** `EventIngestionDbContext.OnModelCreating` calls
`ApplyConfigurationsFromAssembly`, so the configuration is picked up
automatically. One `DbSet<RegisteredEventType> RegisteredEventTypes` line is
still needed for the repository and query source to reach it.

**Migration:** one new EF migration in
`src/EventIngestion/Infrastructure/Persistence/Migrations/`, `Up` creating the
table and both indexes, `Down` dropping them, `ArgumentNullException.ThrowIfNull(
migrationBuilder)` at the top of each (the house style in generated migrations,
which ADR-0105 explicitly excepts). Run by `MigrationRunner` (ADR-0067); no
AppHost change. **The `.Designer.cs` and the model snapshot must be regenerated
by `dotnet ef`, not hand-written.**

**The table is not partitioned.** `events` is partitioned by fab and
`FabPartitionProvisioner` provisions per-fab partitions on demand; the registry
is tens of rows and must **not** be added to that machinery. A reviewer expecting
symmetry with `events` should read this line.

---

## 5. API surface

One new file, `src/EventIngestion/Api/EventTypesEndpoints.cs`, mapped from
`Api/Program.cs` alongside the existing endpoint groups.

```csharp
RouteGroupBuilder eventTypes = app.MapGroup("/event-types").WithTags("EventIngestion");
```

Scopes go on the **individual mappings**, not the group — the group carries two
different scopes (FR-010 write, FR-011 read), and
`EndpointScopeDeclarationTests.No_route_group_declares_the_refusal_its_members_must_declare`
requires each mapping to declare its own `403` regardless.

| Route | Scope | Codes declared in its own chain |
|---|---|---|
| `POST /event-types` | `Scope.Sse.Events.Types.Write` | 201, 400, 401, 403, 409 |
| `GET /event-types` | `Scope.Sse.Events.Read` | 200, 400, 401, 403 |
| `DELETE /event-types/{kind}` | `Scope.Sse.Events.Types.Write` | 200, 400, 401, 403, 404, 409, 428 |

**Three declaration rules are build-enforced** (`tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`),
and half-doing them is red:

1. Each `.WithSummary` must contain the literal `Required scope: <the exact scope
   string>`.
2. Each mapping must carry `.ProducesProblem(StatusCodes.Status403Forbidden)` in
   its own chain.
3. **`EndpointFileCount` and `RouteHandlerMappingCount` are pinned constants**
   (`:345`, `:347`) — currently `12` and `56`. This spec makes them `13` and
   `59`. Bumping them is part of the implementation task, not a fix-up.

Handler bodies follow `WebhookIntegrationsEndpoints` exactly:

- Parse `Kind.From(...)` inside `try { } catch (ArgumentException ex)` →
  `Results.Problem(title: "EVENT_TYPE_INVALID_INPUT", detail: ex.Message,
  statusCode: 400)`. This covers both the body on `POST` and the **route value**
  on `DELETE` (spec 091's rule: a malformed route value declares its 400).
- `POST` → `ResolveWriteFabAsync`; `GET` and `DELETE` → `ResolveReadFabsAsync`
  (plural — `DELETE` addresses an existing row, so it uses the read resolution,
  exactly as `Revoke` does).
- `DELETE` reads the precondition with
  `ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion,
  out IResult? precondition)` and returns `precondition` when it fails.
- `POST` wraps the whole thing in `IdempotentRequest.ExecuteCreateAsync` with
  `IdempotencyScope.For(suppliedKey, "POST /event-types",
  user.ToOperatorIdentifier().Value.ToString())`. The caller is in the scope
  because keys are strings callers invent.
- Every handler finishes `result.Match<IResult>(onSuccess: …, onFailure: error =>
  error.ToProblem())`.

**Request record:** `Api/Requests/RegisterEventTypeRequest.cs` — `sealed record
RegisterEventTypeRequest(string Kind)`. A primitive here is correct: it is a wire
shape, and §II's scope is the domain model.

---

## 6. Application layer

Per ADR-0093, one folder per message kind with a `Handlers/` subfolder and a
paired `*Errors.cs`.

**`RegisterEventTypeCommand(FabIdentifier Fab, Kind Kind, OperatorIdentifier
RegisteredBy)`** → `Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>`.
Handler: guard, **destructure the command into locals as the first statement
after the guard** (house rule; three fields read, so deconstruction applies and
`HandlerDeconstructionTests` checks the local names), `GetRegisteredAsync(fab,
kind)` → `HasValue` ⇒ `Failure(EventTypeAlreadyRegistered)`, else `Register`,
`Add`, `SaveAsync`, log, `Success`.

**`RetireEventTypeCommand(IReadOnlyList<FabIdentifier> Fabs, Kind Kind, int
ExpectedVersion)`** → `Result<RegisteredEventTypeIdentifier,
RetireEventTypeError>`, plus the acting operator. Handler order, copied from
`RevokeWebhookIntegrationCommandHandler` because the order is load-bearing:

1. lookup over the caller's fabs → `None` ⇒ `Failure(EventTypeNotFound)` (**404,
   and this is also the already-retired and the cross-fab answer**);
2. `if (eventType.Version != expectedVersion)` ⇒ `Failure(EventTypeStaleVersion)`;
3. `Retire(...)`, `SaveAsync`, `Success`.

**`ListEventTypesQuery(IReadOnlyList<FabIdentifier> Fabs)`** →
`Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError>` through
`IRegisteredEventTypeQuerySource`. Filters `Fabs.Contains(x.Fab)` and `x.State ==
RegistrationState.Registered`, ordered by `Kind`. **Write the EF comparison
against the value objects directly** — `.Value` member access does not translate,
which `ListEventsQueryHandler` documents and `ListEventsTranslationTests` guards.

**Errors** (ADR-0047 shape, each with a `*Failures` static because generics are
invariant):

| Code | Status |
|---|---|
| `EVENT_TYPE_ALREADY_REGISTERED` | 409 |
| `EVENT_TYPE_NOT_FOUND` | 404 |
| `EVENT_TYPE_STALE` | 409 |

`EVENT_TYPE_INVALID_INPUT` (400) and `EVENT_FAB_REQUIRED` (400) are produced at
the endpoint, not by a handler — the same split `WebhookIntegrationsEndpoints`
uses.

**DTO:** `RegisteredEventTypeDto(Guid EventTypeId, string Fab, string Kind,
string State, DateTimeOffset RegisteredAt, Guid RegisteredBy, int Version)` in
`Application/DTOs/`. Primitives are correct here — it is a serialization contract.

**Logging:** two `[LoggerMessage]` entries added to
`Application/Log.cs` (registered / retired), structured fields, no string
interpolation (ADR-0050).

---

## 7. The scope, and the realm edit that is easy to half-do

`src/ServiceDefaults/Authorization/Scope.cs`:

```csharp
public static class Events
{
    public const string Read = "sse.events.read";
    public const string Write = "sse.events.write";
    public const string Publish = "sse.events.publish";

    public static class Types
    {
        public const string Write = "sse.events.types.write";
    }
}
```

**Two edits, not one.** The constant, *and* the entry in `Scope.All` — a scope
missing from `All` never gets a policy registered by `AddScopePolicies`, and the
failure is silent at startup and a refusal at runtime. `ScopeTests` checks
uniqueness and that every scope has `>= 3` dot-separated parts beginning `sse`;
a four-part scope is already precedent (`sse.identity.devices.read`).

`src/AppHost/Realms/smart-sentinel-eye-realm.json`: add a `clientScopes` entry
(copy the object shape used by `sse.webhooks.write`) **and** add it to
`management-web`'s `defaultClientScopes`. A scope granted to a client but not
defined is discarded on import with a warning only; `RealmIdentityTests` is what
catches it. Do **not** add it to `kiosk-web` — `KioskScopeParityTests` compares
the kiosk bundle against the realm as a set in both directions and will go red.

**Two traps, both previously paid for:**

- **A realm edit does not take effect on a restart.** Keycloak keeps the imported
  realm in its volume. The dev volume must be deleted for the new scope to
  appear, and the stack looks perfectly healthy while serving the old realm.
- **A green integration suite does not prove the realm edit landed.** The legacy
  `sse.management` bundle satisfies every `sse.*` policy except
  `sse.events.publish`, so the seeded operators pass the new policy with or
  without the grant. `RealmIdentityTests` is the test that actually checks it;
  the integration tests are not.

---

## 8. File ownership and collisions (ADR-0109)

**Shared files — every task touching one is serial with every other task
touching it:**

| File | Touched by |
|---|---|
| `src/EventIngestion/Infrastructure/Persistence/EventIngestionDbContext.cs` | the `DbSet` line |
| `…/EventIngestionDbContextModelSnapshot.cs` | the migration (generated) |
| `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs` | repository + query source + 3 handler registrations |
| `src/EventIngestion/Application/Log.cs` | 2 logger messages |
| `src/EventIngestion/Api/Program.cs` | the `MapEventTypesEndpoints()` call |
| `src/ServiceDefaults/Authorization/Scope.cs` | the new scope |
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | the client scope + grant |
| `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs` | the two pinned counts |
| `.specify/memory/constitution.md` | FR-014 |

**Genuinely disjoint, and therefore `[P]`:** the Domain files (8 new files, one
new folder, no existing file opened), and the three test files in
`tests/EventIngestion.Domain.Tests/RegisteredEventType/`.

**This is a single-engineer slice.** The `[P]` markers in `tasks.md` mark tasks
that *could* run concurrently, but the realistic parallelism here is small and
the shared-file list above is long; the value of the markers is that they name
the files each task owns, which is what stops two tasks racing on
`EventIngestionInfrastructureModule.cs`.

---

## 9. Phase 4a colour: **RED**, and how red is obtained honestly

**RED. Every task in this spec is new behaviour** — a new aggregate, new
endpoints, a new table. Nothing is being preserved, so characterisation does not
apply and ADR-0144's ambiguity rule does not need to be invoked.

### The trap

A test that references `RegisteredEventType` before the type exists fails with
`CS0246`, and **a compile error is not a red test** (spec 061, `24e6fc4c`). If
the whole domain is written in the first commit, the "red" that gets quoted in
the PR body is a build failure, which proves nothing about the assertions.

### The sequence that avoids it

1. **T001 (prelude)** introduces the Domain *types* with the members present and
   the behaviour absent: `Register` returns an aggregate whose `State` is left at
   its default, `Retire` is a body of `Ensure.That(...)` and nothing else, and no
   event is raised. The file compiles; nothing is correct. The guard bodies
   survive unchanged into the implementation, so this is not scaffolding to be
   deleted.
2. **Phase 4a** writes the tests against those signatures. They compile, they run
   and they fail on **assertions** — `State` is not `Registered`, no
   `EventTypeRegisteredDomainEvent` is pending, `Retire` did not flip anything.
   The verbatim output goes into the engineer's brief and into the PR body.
3. The engineer fills the bodies and may not edit the tests.

Integration tests against the endpoints do not have this problem — an endpoint
that is not mapped answers `404`, which is a perfectly good red.

### The written prediction

Phase 4a must quote its **actual** output against this list. Expect, at the
domain level: `Register_puts_the_type_in_the_Registered_state` fails comparing
`null` (or the default) to `Registered`;
`Register_raises_an_EventTypeRegisteredDomainEvent` fails on an empty
`PendingEvents`; `Retire_flips_the_state_to_Retired` and
`Retire_is_idempotent_on_an_already_retired_type` fail on the state comparison.
At the integration level: every request returns `404 Not Found` because the group
is not mapped, and `Registering_the_same_kind_twice_in_one_fab_is_refused` fails
expecting `409` and receiving `404`.

**If any 4a test passes on its first run, that is a phase-4 failure**, not a
shortcut — the test is asserting something that was already true, and it must be
made to bite before the implementation starts.

### Counterfactuals — run at phase 6, each expected to turn exactly one test red

1. Change the index filter to `HasFilter(null)` (make the unique index total) and
   re-run: the re-register-after-retire integration test must fail.
2. Delete the `expectedVersion` comparison from `RetireEventTypeCommandHandler`:
   the stale-version test must fail and nothing else.
3. Change the retire lookup from the plural-fabs overload to the single-fab one
   with the row's own fab: the cross-fab-404 test must fail.
4. Swap `Scope.Sse.Events.Types.Write` for `Scope.Sse.Events.Write` on the `POST`
   mapping: `EventTypeRegistryAuthorizationIntegrationTests`'s
   `An_event_source_token_can_neither_declare_nor_retire_an_event_type` must go
   from 403 to 201. **Not** the primary suite's original
   `A_caller_holding_only_sse_events_write_is_refused_with_403`, which phase 4a
   found and removed as structurally unable to fail either way — every token
   this repo's test clients mint carries the full `sse.*` bundle regardless of
   the `scope` requested (FR-010's testing-gotcha note), so that test could
   never distinguish a working gate from a missing one. The planted-client
   test is the one this counterfactual actually exercises.

Counterfactual 4 is the one that matters most — FR-010 is a security claim, and
a security claim with no test that would notice its removal is a claim, not a
guard.

---

## 10. Coverage, metrics, review

- **Coverage (ADR-0065):** Domain ≥ 90 %, Application ≥ 80 %. The aggregate has
  two methods and four VOs; the domain tests listed in `tasks.md` cover every
  branch including the idempotent early return. The three handlers have five
  branches between them, all covered.
- **Metrics (ADR-0084):** ≤ 300 LOC/file, ≤ 30 LOC/method, ≤ 4 params, complexity
  ≤ 10, depth ≤ 3. **The endpoint handlers are the risk**: `Register` in
  `WebhookIntegrationsEndpoints` is already near the parameter ceiling. Follow its
  shape — a `[FromServices]` services record where the count would otherwise
  exceed four.
- **Review:** `ultrareview` runs on PRs touching Event Ingestion (constitution
  §Code Review). A `security-reviewer` pass is warranted for FR-010 specifically.

---

## 11. Risks

- **R1 — The engineer "finishes" decision 018.** The most likely failure mode of
  this spec is a phase-4 agent reading the issue rather than the spec and wiring
  the registry into `IngestEventCommandHandler`. FR-013 and the quiet-case
  acceptance scenario exist to catch it; `tasks.md` repeats the prohibition in
  its gate.
- **R2 — The realm edit is made and not observed.** §7's second trap. Mitigation:
  the verification procedure's step 11 uses a token that does *not* hold
  `sse.management`.
- **R3 — `EndpointScopeDeclarationTests`' pinned counts are discovered late.**
  They fail the build in `Architecture.Tests`, not in the context's own suite, so
  a local `dotnet test` on `EventIngestion.*` looks green. The bump is an explicit
  task.
- **R4 — The partial-index syntax.** `HasFilter("state <> 'Retired'")` is raw SQL
  against the **column** name and the **stored** value, not the property or the
  VO. Copy `VariableConfiguration.cs:116` and check the generated migration reads
  `filter: "state <> 'Retired'"`.
- **R5 — FR-014's constitution edit.** If the phase-1 gate rejects it, drop the
  task; nothing depends on it. If it is accepted, it must change *only* the
  "no event-type registry" clause — the other two clauses of that bullet are
  still true and must stay.

---

## 12. Gate — phase 2

- [ ] Plan aligns with the constitution and the cited ADRs.
- [ ] No cross-context reference; no `Shared.Contracts` change; no
      `AllowedCrossContext` entry.
- [ ] §IV is not engaged, and the plan adds no work to any latency leg.
- [ ] Phase 4a colour **RED**, with §9's prelude-first sequence understood.
- [ ] The shared-file list in §8 is the input to `tasks.md`'s `[P]` markers.
