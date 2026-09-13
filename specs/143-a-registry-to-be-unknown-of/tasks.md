# Tasks 143 — A registry to be unknown of

**Spec:** `./spec.md` · **Plan:** `./plan.md`
**Phase:** 3 (Tasks) · **Date:** 2026-09-13
**Issue:** #1972 — **partially** addressed; see the gate, it must not be closed
outright
**Engineer:** `backend-engineer` (single engineer; no frontend, no infra agent —
the two AppHost/ServiceDefaults edits are three lines and belong with the
endpoint that needs them). **Phase 4a colour: RED** — see `plan.md` §9.

Legend: `[ID] [P?] [Story]`. `[P]` = disjoint files, safe to run concurrently.

---

## The prohibition that governs this whole list

**No file on the ingest path is opened.** Not
`Application/Commands/Handlers/IngestEvent*`, not `Application/Ingress/`, not
`Infrastructure/Ingress/`, not `Api/EventsEndpoints.*`. Nothing reads the registry
at ingest time. Spec FR-013, plan R1.

Decision 018 has three parts and this is the first (spec §0.3). An implementation
that also wires the mode in has not finished the issue early — it has built an
undecided design, which is the one thing ADR-0144 forbids outright.

---

## The ordering constraint that is the point of Phase A

**T001 and T002 must land before any 4a test exists as a runnable test.** Without
them the tests fail `CS0246`, and **a compile error is not a red test** (spec
061, `24e6fc4c`). Phase A introduces the types and signatures with the *behaviour
absent*, so the 4a failures are assertions about state and outcomes.

Phase A code is **not scaffolding to be deleted**: every guard written in it
survives unchanged into Phase C.

---

## Phase A — Prelude (blocks everything)

- [ ] **T001 [US1]** `src/EventIngestion/Domain/RegisteredEventType/` — **the
  types, with no behaviour**. Eight new files, one new folder, **no existing file
  opened**.
  - `RegisteredEventTypeIdentifier.cs` — `readonly record struct`,
    `IStronglyTypedId`, `IComparable`; `New()` ⇒ `Guid.CreateVersion7()`,
    `From(Guid)`. *Fully implemented* — copy `WebhookIntegrationIdentifier`; it
    has no behaviour to withhold.
  - `RegistrationState.cs` — `sealed record(string Value) : IValueObject<string>`
    with statics `Registered` / `Retired` and `ToString()`. **`From(string)`
    returns `Registered` unconditionally** — no `switch`, no throw.
  - `RegisteredAt.cs` — `record(DateTimeOffset Value)`, `IComparable`, implicit
    conversion to `DateTimeOffset`. **`From` performs the `Ensure` guard and
    returns the value unnormalised** — no `.ToUniversalTime()`.
  - `Registration.cs` — `sealed record Registration(RegisteredAt RegisteredAt,
    OperatorIdentifier RegisteredBy)`; **`From` is the guards and the
    construction**, nothing else.
  - `Events/EventTypeRegisteredDomainEvent.cs`,
    `Events/EventTypeRetiredDomainEvent.cs` — `IDomainEvent` records, shapes in
    `plan.md` §2. *Fully implemented*; they are data.
  - `IRegisteredEventTypeRepository.cs` — the four members in `plan.md` §2.
    *Interface only.*
  - `RegisteredEventType.cs` — `AggregateRoot<RegisteredEventTypeIdentifier>`
    with the five members, a private parameterless constructor, and:
    - `Register(...)` — guards, then constructs with `Id` set and **`State`,
      `Registration` left at their `null!` defaults**; raises nothing.
    - `Retire(...)` — body is `Ensure.That(...)` guards and **nothing else**.
  - **Done when:** `dotnet build` is clean, `PrimitiveBoundaryTests` is green
    (no primitive-typed state on the model), and not one assertion in Phase B
    would pass.

  *Commit:* `feat(event-ingestion): a registered-event-type aggregate with no behaviour`

- [ ] **T002 [US1]** Application skeleton — `plan.md` §6's shapes, bodies
  withheld. Files:
  `Application/Commands/RegisterEventTypeCommand.cs`,
  `RegisterEventTypeErrors.cs`,
  `Handlers/RegisterEventTypeCommandHandler.cs`,
  `Commands/RetireEventTypeCommand.cs`, `RetireEventTypeErrors.cs`,
  `Handlers/RetireEventTypeCommandHandler.cs`,
  `Queries/ListEventTypesQuery.cs`, `ListEventTypesErrors.cs`,
  `Queries/Handlers/ListEventTypesQueryHandler.cs`,
  `Queries/IRegisteredEventTypeQuerySource.cs`,
  `DTOs/RegisteredEventTypeDto.cs`.
  - Error records and their `*Failures` statics are **fully written** — they are
    data, and the 4a tests assert on their `Code`s.
  - Handler bodies are the guard, the deconstruction (house rule: two-or-more
    fields ⇒ destructure into locals as the first statement after the guard;
    `HandlerDeconstructionTests` checks the local names), then:
    - register ⇒ `return Success(RegisteredEventTypeIdentifier.New());` —
      **touches no repository**;
    - retire ⇒ `return Failure(RetireEventTypeFailures.EventTypeNotFound(...));`;
    - list ⇒ `return Success((IReadOnlyList<RegisteredEventTypeDto>)[]);`.
  - **No `NotImplementedException` anywhere.** A throw makes a 4a test *error*
    rather than *fail*, and an error is not the red this gate wants.
  - **Done when:** `dotnet build` is clean; every handler answers; nothing is
    stored, read or refused for the right reason.

  *Commit:* `feat(event-ingestion): event-type registry handlers that answer nothing`

---

## Phase B — Tests first, observed RED (T002 → all of B)

Owner: **`test-writer`**. Written, run, and the **verbatim output** carried into
the engineer's brief and quoted in the PR body (ADR-0139, ADR-0144). The engineer
may not edit these.

Naming is sentence-style with underscores (ADR-0053); data comes from
hand-written builders (ADR-0054); assertions are Shouldly (ADR-0052).

- [ ] **T003a [P] [US1] [US2]**
  `tests/EventIngestion.Domain.Tests/RegisteredEventType/RegisteredEventTypeTests.cs`
  and `RegisteredEventTypeBuilder.cs` — the aggregate.
  1. `Register_puts_the_type_in_the_Registered_state`
  2. `Register_records_who_registered_it_and_when`
  3. `Register_raises_an_EventTypeRegisteredDomainEvent_naming_the_fab_and_kind`
  4. `Retire_flips_the_state_to_Retired`
  5. `Retire_raises_an_EventTypeRetiredDomainEvent`
  6. `Retire_is_idempotent_on_an_already_retired_type` — no second event
  7. `Register_refuses_a_null_fab` / `_kind` — the guards T001 already wrote;
     **these two are expected to pass on the first run**, and that is fine
     because they assert T001's behaviour, not T004's. Say so in the 4a note.

- [ ] **T003b [P] [US1]**
  `tests/EventIngestion.Domain.Tests/RegisteredEventType/RegisteredEventTypeValueObjectTests.cs`
  — the four value objects.
  1. `RegistrationState_From_round_trips_each_known_state`
  2. `RegistrationState_From_refuses_an_unknown_state`
  3. `RegisteredAt_stores_the_instant_in_UTC` — a non-UTC input comes back UTC
  4. `RegisteredAt_orders_by_instant`
  5. `Registration_refuses_a_null_registeredAt` / `_registeredBy`
  6. `RegisteredEventTypeIdentifier_New_is_a_version_7_guid`

- [ ] **T003c [P] [US1] [US2]**
  `tests/EventIngestion.Application.Tests/Commands/EventTypeCommandHandlerTests.cs`
  plus `tests/EventIngestion.Application.Tests/Fakes/InMemoryRegisteredEventTypeRepository.cs`
  (hand-written, mirroring `InMemoryWebhookIntegrationRepository` — **no mocking
  framework**).
  1. `Registering_a_kind_stores_it_against_the_resolved_fab`
  2. `Registering_a_kind_already_registered_in_that_fab_is_refused` ⇒
     `EVENT_TYPE_ALREADY_REGISTERED`, 409
  3. `Registering_a_kind_another_fab_already_has_is_allowed`
  4. `Registering_a_kind_a_fab_has_retired_is_allowed`
  5. `Retiring_a_registered_type_flips_it_and_leaves_the_row`
  6. `Retiring_a_type_outside_the_callers_fabs_reports_it_as_not_found` ⇒
     `EVENT_TYPE_NOT_FOUND`, 404
  7. `Retiring_an_already_retired_type_reports_it_as_not_found`
  8. `Retiring_with_a_stale_expected_version_is_refused` ⇒
     `EVENT_TYPE_STALE`, 409
  9. `The_version_gate_runs_after_the_lookup` — a stale version against a row in
     another fab answers 404, not 409 (order is load-bearing; `plan.md` §6)

- [ ] **T003d [P] [US1]**
  `tests/EventIngestion.Application.Tests/Queries/ListEventTypesQueryHandlerTests.cs`
  1. `Listing_returns_the_registered_types_of_the_callers_fabs`
  2. `Listing_excludes_retired_types`
  3. `Listing_excludes_types_of_fabs_the_caller_does_not_hold`
  4. `Listing_orders_by_kind`
  5. `Listing_carries_the_version_of_each_entry`

- [ ] **T003e [P] [US1] [US2]**
  `tests/Integration.Tests/EventIngestion/EventTypeRegistryIntegrationTests.cs` —
  `[Collection(AspireCollection.Name)]`, `AspireFixture` by primary constructor,
  resource `"event-ingestion"`, personas `op-dresden@dresden.test` (single fab)
  and `op-multi@smart-sentinel-eye.test` (multi-fab), password `Operator1234`.
  Run-unique kinds built from `Guid.NewGuid():N` — **the database is shared
  across runs**. Every status assertion carries a `BodyAsync(response)` message
  that appends `aspire.RecentLogs("event-ingestion")`.
  1. `A_registered_event_type_is_listed_back`
  2. `The_same_kind_twice_in_one_fab_is_refused_with_409`
  3. `The_same_kind_in_two_fabs_is_two_entries`
  4. `A_malformed_kind_is_refused_with_400` (body, and the route value on DELETE)
  5. `A_multi_fab_caller_that_names_no_fab_is_refused_with_400`
  6. `Naming_a_fab_the_caller_does_not_hold_registers_nothing` ⇒ 403, **and the
     other fab's list is asserted unchanged** — a refusal must be shown to have
     written nothing, not merely to have answered
  7. `An_anonymous_caller_is_refused_with_401`
  8. `Retiring_without_If_Match_is_refused_with_428`
  9. `Retiring_with_a_stale_If_Match_is_refused_with_409`
  10. `Retiring_a_type_of_another_fab_is_reported_as_404`
  11. `A_retired_kind_can_be_registered_again` — FR-004's released name
  12. `A_repeated_register_with_the_same_idempotency_key_replays_the_same_201`
  13. **`An_unknown_kind_is_still_ingested`** — spec §4's quiet case and FR-013's
      guard. `POST /events/manual` with a kind in no registry, read it back from
      `GET /events`. **This test must be written even though it passes on its
      first run**: it is the only thing in the suite that would notice a phase-4
      agent wiring the registry into ingest.

- [ ] **T003f [US1]** Run all of B, capture the **verbatim** output, check it
  against `plan.md` §9's written prediction, and hand it to the engineer.
  - **Any assertion that passes unexpectedly is a phase-4 failure**, not a
    shortcut — except the three named above (T003a.7, T003e.13), which are
    documented as green-on-arrival with their reasons.
  - Serial: it runs what T003a–e wrote.

  *Commit:* `test(event-ingestion): an event-type registry nothing has built yet` (red)

---

## Phase C — Implementation (T003f → C)

Owner: **`backend-engineer`**. **May not edit any file under `tests/` written in
Phase B.**

- [ ] **T004 [P] [US1] [US2]** `src/EventIngestion/Domain/RegisteredEventType/`
  — fill the behaviour withheld by T001: `RegistrationState.From`'s `switch` and
  throw, `RegisteredAt.From`'s UTC normalisation, `Register`'s state and
  `Registration` assignment plus its `Raise`, `Retire`'s idempotent early return,
  flip and `Raise`.
  **Done when:** T003a and T003b are green and nothing in `tests/` changed.

  *Commit:* `feat(event-ingestion): an event type is registered and retired`

- [ ] **T005 [P] [US1] [US2]** the three Application handlers — `plan.md` §6.
  The retire handler's order is load-bearing: lookup over the caller's fabs
  first, version gate second.
  **Done when:** T003c and T003d are green.

  *Commit:* `feat(event-ingestion): registry handlers enforce uniqueness and the version gate`

- [ ] **T006 [US1]** Infrastructure persistence — four files plus two shared:
  - `Persistence/Configurations/RegisteredEventTypeConfiguration.cs` — columns
    per `plan.md` §4, every property `.HasColumnName(snake_case)` +
    `.HasConversion(vo => vo.Value, value => Vo.From(value))`, `Version` as
    `.IsConcurrencyToken()`, the partial unique index
    `.HasFilter("state <> 'Retired'")` (**raw SQL against the column name and the
    stored value** — plan R4; copy `VariableConfiguration.cs:116`),
    `Registration` mapped with `OwnsOne` + `Navigation(...).IsRequired()`
    (`VariableConfiguration.cs:86,98` is the shape), and
    `builder.Ignore(x => x.PendingEvents)`.
  - `Persistence/RegisteredEventTypeRepository.cs` — commits through the shared
    `ITransactionalCommit` seam, as `WebhookIntegrationRepository` does.
  - `Persistence/RegisteredEventTypeQuerySource.cs` — comparisons written
    **against the value objects directly**; `.Value` member access does not
    translate (`ListEventsTranslationTests` guards this).
  - `Persistence/EventIngestionDbContext.cs` — **shared file**, one `DbSet` line.
  - `EventIngestionInfrastructureModule.cs` — **shared file**: the repository,
    the query source, and the three handlers registered both as their concrete
    type and as their `ICommandHandler<,>` / `IQueryHandler<,>` binding
    (ADR-0042/0057, ADR-0051).
  - **No new configuration registration line** — `OnModelCreating` calls
    `ApplyConfigurationsFromAssembly`.

  *Commit:* `feat(event-ingestion): persist the event-type registry`

- [ ] **T007 [US1]** the migration — `dotnet ef migrations add
  RegisteredEventTypes --project src/EventIngestion/Infrastructure`. `Up` creates
  the table and both indexes; `Down` drops them;
  `ArgumentNullException.ThrowIfNull(migrationBuilder)` at the top of each (the
  house style in generated migrations, which ADR-0105 excepts).
  **The `.Designer.cs` and `EventIngestionDbContextModelSnapshot.cs` are
  generated, never hand-written.** Verify the generated `Up` reads
  `filter: "state <> 'Retired'"`.
  **No `IdempotencyKeyTable.Create` call** — `20260903094940_AddIdempotencyKey`
  already created it for this context.
  **Done when:** `MigrationRunner` applies it against a fresh database and the
  index exists with its filter.
  Depends on T006.

  *Commit:* `feat(event-ingestion): a registered_event_types table`

- [ ] **T008 [US1]** the scope — **two shared files, and half-doing either is a
  silent failure** (`plan.md` §7).
  - `src/ServiceDefaults/Authorization/Scope.cs`: the nested
    `Events.Types.Write = "sse.events.types.write"` **and** its entry in
    `Scope.All`. Omitting the `All` entry registers no policy and fails only at
    runtime.
  - `src/AppHost/Realms/smart-sentinel-eye-realm.json`: a `clientScopes` entry
    (copy `sse.webhooks.write`'s object shape) **and** the grant in
    `management-web`'s `defaultClientScopes`. **Not** `kiosk-web` —
    `KioskScopeParityTests` compares that bundle as a set in both directions.
  - **Do not skip the realm half because the tests are green without it.** The
    legacy `sse.management` bundle satisfies every `sse.*` policy but
    `sse.events.publish`, so the seeded operators pass either way.
    `RealmIdentityTests` is what actually checks it.
  - **Locally, the realm edit needs the Keycloak volume deleted** — restarting
    keeps the imported realm and the stack looks perfectly healthy while serving
    the old one.

  *Commit:* `feat(identity): a scope for writing the event-type registry`

- [ ] **T009 [US1] [US2]** the endpoints — `src/EventIngestion/Api/EventTypesEndpoints.cs`
  (new), `Api/Requests/RegisterEventTypeRequest.cs` (new), `Api/Program.cs`
  (shared, one mapping call). Shapes, scopes and status codes in `plan.md` §5.
  - Scopes on the **individual mappings**, not the group.
  - Each `.WithSummary` contains the literal `Required scope: <exact string>`;
    each mapping carries `.ProducesProblem(StatusCodes.Status403Forbidden)` in
    its own chain.
  - `Kind.From` inside `try`/`catch (ArgumentException)` ⇒ 400
    `EVENT_TYPE_INVALID_INPUT`, for the body **and** the `{kind}` route value.
  - `POST` ⇒ `ResolveWriteFabAsync`; `GET` and `DELETE` ⇒ `ResolveReadFabsAsync`.
  - `DELETE` ⇒ `ConcurrencyHeaders.TryReadExpectedVersion`.
  - `POST` ⇒ `IdempotencyHeaders.TryRead` + `IdempotentRequest.ExecuteCreateAsync`
    with `IdempotencyScope.For(key, "POST /event-types",
    user.ToOperatorIdentifier().Value.ToString())` — **the caller is in the
    scope**.
  - Watch the ≤ 4-parameter metric limit (ADR-0084): use a `[FromServices]`
    services record where the count would exceed it, as
    `EventsEndpoints.Writes.cs` does.
  Depends on T005, T008.

  *Commit:* `feat(event-ingestion): register, list and retire event types over HTTP`

- [ ] **T010 [US1]** `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`
  — bump the pinned `EndpointFileCount` 12 → **13** and
  `RouteHandlerMappingCount` 56 → **59**. These live in `Architecture.Tests`, so
  a green `dotnet test` on the `EventIngestion.*` projects does not catch them
  (plan R3). Depends on T009.

  *Commit:* `test(architecture): three more mappings in one more endpoint file`

- [ ] **T011 [P] [US1] [US2]** `src/EventIngestion/Application/Log.cs` — two
  `[LoggerMessage]` source-generated entries (registered, retired), structured
  fields, no interpolation (ADR-0050). Shared file; `[P]` only against tasks that
  do not touch it.

  *Commit:* `feat(event-ingestion): log registry writes`

- [ ] **T012 [US1]** Run the full suite. **Done when:** T003a–e are green, the
  full solution builds Release-clean (analyzers included), and coverage holds
  (Domain ≥ 90 %, Application ≥ 80 % — ADR-0065).

---

## Phase D — the constitution's factual correction

- [ ] **T013 [US1]** `.specify/memory/constitution.md` §VIII, first bullet —
  remove **only** the clause *"no event-type registry,"*. The other two clauses
  ("no per-source strict/discovery mode and no promotion path") are still true
  and stay, with the follow-up issue numbers added beside them. The sentence that
  follows — *"The intended guarantee stands as a requirement … not as a
  description of today"* — also stays; it is now true of two thirds of the
  decision rather than three.
  - **This is a status correction, not an amendment** (spec FR-014): no
    principle, rule or obligation moves. It is the same class of edit §IV demands
    for its leg table, and CLAUDE.md records what happens when such a record is
    left stale.
  - **If the phase-1 gate rejected FR-014, skip this task entirely** and say so
    in the PR body. Nothing else depends on it.

  *Commit:* `docs(constitution): the event-type registry exists`

---

## Phase E — Verify (phase 5; T012 → E)

- [ ] **T014 [US1] [US2]** Run `spec.md` §6 end to end on a real stack and write
  `specs/143-a-registry-to-be-unknown-of/verification.md`.
  - **Step 6 (the restart) and step 10 (the quiet step) are the two that cannot
    be skipped.** Step 6 is the only one that distinguishes a registry from a
    cache; step 10 is the only one that observes FR-013 rather than asserting it.
  - **Step 11 must use a token that does not hold `sse.management`** (plan R2),
    or it proves nothing about FR-010.
  - Stop any running AppHost before building — a running stack holds the service
    binaries and MSB3027 reads as a broken build.
  - **Latency:** cite §IV as **not engaged**; no leg is touched, no cell of §IV's
    table moves, and no figure is produced. A figure here would be a discharge
    nobody earned.

---

## Phase F — Review remediation (phase 6)

- [ ] **T015 [US1]** Run `plan.md` §9's four counterfactuals, each expected to
  turn **exactly one** test red, and record the output in `verification.md`.
  Counterfactual 4 (swapping the write scope for `sse.events.write`) is the
  one that proves FR-010 is a guard rather than a claim.
- [ ] **T016 [US1]** `/code-review`, plus `/security-review` — this touches an
  auth boundary in Event Ingestion, which constitution §Code Review flags for
  `ultrareview`. Address or accept every finding in writing.

---

## Dependency graph

```
T001 (Domain skeleton — blocks everything)
  └─ T002 (Application skeleton)
       ├─ T003a [P] ─┐
       ├─ T003b [P] ─┤
       ├─ T003c [P] ─┼─ T003f (run; quote the red)
       ├─ T003d [P] ─┤        │
       └─ T003e [P] ─┘        │
                              ├─ T004 [P] ─┐
                              ├─ T005 [P] ─┼─ T006 ── T007 ─┐
                              ├─ T008 ─────┘                │
                              └─ T011 [P]                   │
                                          T005 + T008 ── T009 ── T010 ─┐
                                                                       │
                                                    T007 + T010 ── T012 ── T013
                                                                             │
                                                                     T014 ── T015 ── T016
```

**Foundational — blocks the fan-out:** T001, then T002. Nothing in Phase B can
compile before both land, and nothing in Phase C is meaningful before T003f has
captured the red.

**The realistic parallelism is small**, and the `[P]` markers earn their keep by
naming file ownership rather than by promising speed. The long shared-file list
is in `plan.md` §8; the tasks that touch one of those files (T006, T008, T009,
T010, T011, T013) are serial against each other on that file.

---

## Board (ADR-0037 phase-3 gate)

**Feature-level, not per-task.** `/speckit-taskstoissues` is **not** run: per-task
`[TNNN]` issues stopped at spec 028 and specs 029–044 created none. The gate is
that **#1972 is on Project #13**:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/1972
```

(`item-add` prints nothing on success; `item-list` defaults to 30, so verify with
`--limit 2000` or a `content.url` query.)

---

## Gate — phase 3

- [ ] Tasks are atomic and each names the files it writes.
- [ ] **#1972 is on Project #13** (feature-level; no per-task issues created).
- [ ] **Phase 4a colour declared: RED**, with the three documented
      green-on-arrival exceptions named (T003a.7, T003e.13) and no others
      permitted.
- [ ] The prelude-first ordering is understood: a `CS0246` is not a red test.
- [ ] **The ingest path stays shut** (FR-013). An implementation that wires the
      registry into ingest is a blocked outcome, not early delivery.
- [ ] **#1972 is not closed by this PR.** The PR says which third of decision 018
      it delivers, and the three follow-ups from `spec.md` §10 are filed — two of
      them (the per-source mode, and schema validation) flagged as likely needing
      an ADR first, which this lane may not write.
