# Plan 317 — The unknown a source holds

**Spec:** `specs/317-the-unknown-a-source-holds/spec.md` (phase-1 gate: Q1 answered,
option A) · **Issue:** #2325 · **Branch:** `feat/2325-quarantine-unknown-event-types`
**Phase:** 2 (Plan) · **Date:** 2026-10-08 · **Tree read at:** `ba37ca5a`
**ADRs:** as spec.md header. No new ADR. **Constitution:** §II, §III bullet 5, §IV
(engaged), §VIII (correction filed, not made — §10), §Testing.

---

## 1. Bounded context and layers

**Event Ingestion only.** No `Shared.Contracts`, no `apps/`, no other context, no
AppHost, no realm. Every file is under `src/EventIngestion/` or `tests/`.

| Layer | What changes |
|---|---|
| Domain | `DeadLetter` gains `Reason`, `Kind?`, `State`; two new VOs; `SourceMode.Undeclare` + event; `ISourceModeRepository.Remove`; `IDeadLetterRepository.PromoteHeldAsync` |
| Application | `EventTypeVerdicts` three-way; `IEventTypeAdmissionSource` returns declared modes; both ingest handlers write holds; `IngestEventError.EventTypeHeld`; `RegisterEventTypeCommandHandler` promotes; `ListDeadLettersQuery` filters; `UndeclareSourceModeCommand` + handler |
| Infrastructure | EF config + migration; `EventTypeAdmissionSource`; `DeadLetterRepository.PromoteHeldAsync`; `SourceModeRepository.Remove`; both dead-letter writers pass a reason; persistence-loop slow path treats `EventTypeHeld` as stored; DI registration |
| Api | write endpoints map `EventTypeHeld` → 202; dead-letters listing filters; `DELETE /event-sources/{source}` |

Boundary rules hold by construction: nothing new crosses a context, NetArchTest is
unaffected.

---

## 2. Entities and value objects

### 2.1 `DeadLetter` (existing aggregate, `Domain/DeadLetter/DeadLetter.cs`)

New state, in addition to `Topic`, `Fab?`, `RawPayload`, `Error`, `RejectedAt`:

| Property | Type | Column | Notes |
|---|---|---|---|
| `Reason` | `DeadLetterReason` | `reason text NOT NULL` | FR-001 |
| `Kind` | `Kind?` (existing `Domain/Event/Kind`) | `kind varchar(128) NULL` | FR-002; nullable reference VO, as `Fab` |
| `State` | `HoldState` | `state text NOT NULL DEFAULT 'Held'` | FR-003 |

`Capture` signature becomes
`Capture(DeliveryTopic topic, FabIdentifier? fab, RawPayload rawPayload, RejectionReason error, DeadLetterReason reason, Kind? kind, IClock clock)`
— seven parameters, over ADR-0084's advisory four (`S107` warning, carved out of
Release's warnings-as-errors; `SourceMode.Declare` is the precedent). An
alternative factory per reason (`CaptureParseFailure`, `CaptureRefusal`,
`CaptureHold`) would make FR-002's invariant a type fact rather than a guard, and
is **preferred if the engineer finds it reads better** — the three call shapes are
genuinely different (parse failures have no kind and maybe no fab; refusals and
holds always have both). Either way the invariant is:
`reason == UnknownEventType ⇒ fab != null && kind != null`, guarded with
`Ensure.That` and pinned by a domain test.

Nullable `Kind?` parameter in Domain: ADR-0141 prefers `Option<T>` and is advisory.
`Fab?` on the same method is the precedent; mirror it rather than mix the two.
If the per-reason factories are chosen the question disappears.

### 2.2 New value objects (`Domain/DeadLetter/`)

- **`DeadLetterReason`** — closed, static singletons `ParseFailure`, `Refused`,
  `UnknownEventType`; `From(string)` throws on anything else; PascalCase wire
  values (they are filter values in a query string and match `RegistrationState`'s
  `Registered`/`Retired` casing). Precedent: `RegistrationState.cs`.
- **`HoldState`** — closed, `Held`, `Promoted`, same pattern.

### 2.3 `SourceMode` (existing aggregate)

- **`Undeclare(OperatorIdentifier undeclaredBy, IClock clock)`** raises
  `SourceModeUndeclaredDomainEvent(Id, Fab, Source, Mode, OccurredAt, undeclaredBy)`.
  It changes no state; the handler then removes the row.
- **`ISourceModeRepository.Remove(SourceMode sourceMode)`**.
- **Domain events are dispatched on save?** Check before writing T008: if
  `DomainEventDispatcher` collects from tracked entities on `SaveChanges`, a
  removed entity's pending events must still be collected. If it does not, the
  event is raised and never dispatched — acceptable (nothing consumes it, spec 269
  FR-014's siblings are equally unconsumed), but the engineer records which.

### 2.4 Repository ports (Domain)

- `IDeadLetterRepository.PromoteHeldAsync(FabIdentifier fab, Kind kind, CancellationToken cancellationToken) : Task<int>`
  — returns rows promoted (logged, and asserted by tests).

---

## 3. Invariants

1. `UnknownEventType ⇒ fab and kind present` (domain guard).
2. Every captured row starts `Held` (domain).
3. Only `UnknownEventType ∧ Held` rows become `Promoted`, and only by
   `PromoteHeldAsync` (the `UPDATE`'s `WHERE`).
4. A held event never produces an `Event` row or an outbox message (handler; FR-010).
5. Precedence on every path: redelivery → future skew → strict refusal → hold.
6. An undeclared pair is always admitted (Q1); `RegisteredKindsAsync` is never
   called for a fab with no declared pair in the batch (FR-006).
7. `(fab, source)` has at most one `source_modes` row (existing total unique index);
   undeclare deletes it.

---

## 4. Messaging and persistence

**No integration event, no outbox change, no `Shared.Contracts` change** (FR-011).
Domain event `SourceModeUndeclaredDomainEvent` only, unconsumed.

**Migration** `AddDeadLetterHoldState` (EF-generated, with Designer + snapshot;
run by `MigrationRunner`, ADR-0067), in this order:

1. `ALTER TABLE dead_letters ADD reason text NULL, ADD kind varchar(128) NULL, ADD state text NOT NULL DEFAULT 'Held'`.
2. Back-fill: `UPDATE dead_letters SET reason = CASE WHEN topic LIKE 'event/%' THEN 'Refused' ELSE 'ParseFailure' END` (FR-004).
3. `ALTER COLUMN reason SET NOT NULL`.
4. `CREATE INDEX ix_dead_letters_fab_reason_state ON dead_letters (fab, reason, state)`.
   The existing `ix_dead_letters_fab` stays (it is a prefix of the new one; dropping
   it is a separate, measured clean-up — not here).
5. `DELETE FROM source_modes WHERE mode = 'discovery'` (FR-014). `migrationBuilder.Sql(...)`.
   **Check the stored literal first**: read `SourceModeConfiguration`'s conversion
   for `EventTypeMode` — the predicate must match what is stored (`'discovery'`
   lowercase per spec 269 FR-001), and the migration test (T005) asserts it against
   a seeded row rather than trusting the string.

`Down`: drop index and the three columns. The deleted `source_modes` rows are not
restored (spec FR-014 says so).

Steps 2 and 5 are hand-written `Sql` inside the generated migration. A claimed
migration is checked by `git diff --stat` on `Migrations/` (memory: a green build
never boots a database).

---

## 5. API surface

| Endpoint | Change |
|---|---|
| `POST /events/manual`, `POST /events/webhook/{integrationName}` | `EventTypeHeld` → `202 Accepted`, empty body, no `Location`. Add `.Produces(StatusCodes.Status202Accepted)`. Mapped in `StoreOrRefuseAsync`'s `onFailure` (the one shared helper both paths use, `EventsEndpoints.Writes.cs` ~425): `error is IngestEventError.EventTypeHeld ? Results.Accepted() : error.ToProblem()`. For the manual path inside `IdempotentRequest.ExecuteCreateAsync`, the 202 travels as the `Failure(IResult)` branch — the same branch a 4xx takes — so the key is not recorded as a completed create and a replay with the same key re-runs (and holds again, spec A5). **Verify** that `ExecuteCreateAsync` releases the reservation on a non-success result rather than persisting it; if it persists `IResult`s, record the finding and keep the behaviour (a replay answering 202 again is also truthful). |
| `GET /events/dead-letters` | `[FromQuery] string? reason`, `[FromQuery] string? state`. Parsed in the endpoint with `DeadLetterReason.From` / `HoldState.From` inside a `try` (the `SOURCE_MODE_INVALID_INPUT` pattern in `EventSourcesEndpoints`); failure → `400 DEAD_LETTER_INVALID_FILTER`. Passed as `Option<…>` (ADR-0141). Summary updated to name both filters. |
| `DELETE /event-sources/{source}` | **New.** `RequireScope(sse.events.types.write)`, `Required scope:` in summary. `If-Match` via `ConcurrencyHeaders.TryReadExpectedVersion` **first** → `428`; then `Source.From` → `400 SOURCE_MODE_INVALID_INPUT`; then `ResolveWriteFabAsync`; then handler. `204` / `404 SOURCE_MODE_NOT_DECLARED` / `409 SOURCE_MODE_STALE`. Mirror `Change` in the same file line for line. |
| `GET /event-sources` | Summary text: *"An undeclared source is open: unknown kinds are stored as they always were. A declared discovery source holds them; a declared strict source refuses them."* (replaces `EventSourcesEndpoints.cs:65-66`). Doc comment at `:23-24` likewise. |
| `POST /event-types` | Contract unchanged. Summary gains one sentence: registering promotes held rows of that kind. |

`EndpointScopeDeclarationTests`: `RouteHandlerMappingCount` 69 → **70**,
`EndpointFileCount` unchanged at 16, with a comment line in the existing history
block. **Re-read the constant on develop at phase 4**: another merge may have moved
it (the history block shows this has happened twice).

---

## 6. Application layer

### 6.1 Admission (`Application/Ingress/`)

- `IEventTypeAdmissionSource.StrictSourcesAsync` is **replaced** by
  `DeclaredSourceModesAsync(IReadOnlyCollection<FabIdentifier> fabs, CancellationToken)`
  returning `IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode>`
  — every declared pair with its mode, still one query. (A second method would add a
  query to every batch, breaking FR-006.)
- `EventTypeAdmission.AssessAsync`: empty → `AdmitAll`; declared map empty →
  `AdmitAll` (the default, unchanged cost); otherwise `RegisteredKindsAsync` once per
  fab with any declared pair present in the batch (strict **or** discovery).
- `EventTypeVerdicts`: keep `Refuses(envelope)` (strict ∧ unregistered), add
  `Holds(envelope)` (discovery ∧ unregistered). Two booleans rather than an enum:
  both handlers already branch on `Refuses`, and the change is one more branch, not
  a rewrite. (An `Admission` enum is acceptable if the engineer prefers; the tests
  are written against behaviour, not the shape.)

### 6.2 Errors (`Application/Commands/IngestEventErrors.cs`)

- New `IngestEventError.EventTypeHeld(string Fab, string Source, string Kind)`,
  code `EVENT_TYPE_HELD`, `HttpStatusCode.Accepted`, message naming fab, source and
  kind. Factory on `IngestEventFailures`.

### 6.3 The two insertion points

- **`IngestEventCommandHandler`** gains `IDeadLetterRepository deadLetters`. In
  `HandleAsync`, after `Build` succeeds: if `verdicts.Holds(envelope)`, capture the
  hold (`DeadLetterReason.UnknownEventType`, topic
  `event/{fab}/{source}/{device}`, `RawPayload.From(envelope.Payload.Value)`,
  `Error = RejectionReason` from the `EventTypeHeld` error text, kind), `SaveAsync`,
  log, return `Failure(EventTypeHeld)`. The `Event` aggregate built by `Build` is
  discarded unsaved — `Build` must still run first so future skew outranks the hold
  (FR-005 precedence). No `IngestVolume.Record`.
- **`IngestEventBatchCommandHandler`** gains `IDeadLetterRepository`. In
  `StoreOrRefuse`, a built envelope that `Holds` is captured via `deadLetters.Add`
  instead of `events.Add`, and is **not** added to `refused`. The single
  `events.SaveAsync` commits events and holds together (same scoped
  `EventIngestionDbContext` — **verify** both repositories resolve the same
  instance in one scope; they are both `AddScoped` over it). `IngestEventBatchResult`
  is unchanged.
- The topic string `event/{fab}/{source}/{device}` is now composed in three places
  (persistence loop + two handlers). Extract one `DeliveryTopic.ForEnvelope(fab,
  source, device)` factory in Domain and use it in all three — reuse, not a new
  abstraction.

### 6.4 Registration promotes (`RegisterEventTypeCommandHandler`)

Gains `IDeadLetterRepository deadLetters`. Both branches call
`await deadLetters.PromoteHeldAsync(fab, kind, cancellationToken)`:
- success: after `eventTypes.SaveAsync`;
- `EVENT_TYPE_ALREADY_REGISTERED`: before returning the failure (FR-007
  convergence).
Log the promoted count (`[LoggerMessage]`, structured `fab`, `kind`, `count`).

### 6.5 Undeclare (`Application/Commands/`)

`UndeclareSourceModeCommand(FabIdentifier Fab, Source Source, AggregateVersion ExpectedVersion, OperatorIdentifier UndeclaredBy)`,
`UndeclareSourceModeErrors.cs` (`SourceModeNotDeclared`, `SourceModeStale` — the
same codes as `ChangeSourceModeErrors`; generics are invariant, so a parallel
`*Failures` static, as spec 269 did), handler mirroring
`ChangeSourceModeCommandHandler`: get → 404; version mismatch → 409;
`sourceMode.Undeclare(...)`; `sourceModes.Remove(...)`; `SaveAsync`; log. Handler
destructures its command first (CLAUDE.md; `HandlerDeconstructionTests`).

### 6.6 Listing (`Application/Queries/`)

`ListDeadLettersQuery(IReadOnlyList<FabIdentifier> Fabs, int Limit, Option<DeadLetterReason> Reason, Option<HoldState> State)`.
Handler applies `Where` for each present option. `DeadLetterDto` appends
`string? Fab, string Reason, string? Kind, string State`. `ListDeadLettersQuery`'s
positional shape changes, so `HandlerDeconstructionTests` will check the handler's
new deconstruction — name locals after their fields.

---

## 7. Infrastructure

- `DeadLetterConfiguration`: three properties (converters as `Fab`'s, `kind`
  nullable with the `kind!` converter idiom), the composite index.
- `EventTypeAdmissionSource.DeclaredSourceModesAsync`: one no-tracking query over
  `SourceModes` for the fabs, projecting `(Fab, Source, Mode)`.
- `DeadLetterRepository.PromoteHeldAsync`: `ExecuteUpdateAsync(setters => setters
  .SetProperty(d => d.State, HoldState.Promoted).SetProperty(d => d.Version, d => d.Version + 1))`
  over `Where(fab, kind, reason UnknownEventType, state Held)`. **Check** EF can
  translate the VO-converted `SetProperty` and the version increment with the
  existing converters; if `Version + 1` does not translate through
  `AggregateVersion`'s converter, set `State` only and record that held-row
  versions do not move (nothing reads a dead letter's version — it has no
  `If-Match` endpoint). Do not add raw SQL to make it work.
- `SourceModeRepository.Remove`.
- `MqttSubscriberHostedService.CaptureDeadLetterAsync` → `ParseFailure`, no kind.
- `PersistenceLoopHostedService.RecordRejectionAsync` → `Refused`, envelope kind,
  topic via `DeliveryTopic.ForEnvelope`. `StoreOneAsync`: the
  `EventAlreadyIngested ? Stored : Rejected` test becomes
  `EventAlreadyIngested or EventTypeHeld ? Stored : Rejected`.
- DI: `UndeclareSourceModeCommandHandler` registered beside its siblings.

---

## 8. File ownership and collisions (ADR-0109)

One backend engineer, one PR. Parallelism inside phase 4 is limited because the
Domain prelude and the migration are shared. After **T002–T005** (foundational),
three tracks own disjoint files:

| Track | Files |
|---|---|
| **A — ingest holds** | `Application/Ingress/*Admission*`, `EventTypeVerdicts.cs`, `IngestEventErrors.cs`, both `IngestEvent*CommandHandler.cs`, `Infrastructure/Persistence/EventTypeAdmissionSource.cs`, `Infrastructure/Ingress/PersistenceLoopHostedService.cs`, `MqttSubscriberHostedService.cs`, `Api/EventsEndpoints.Writes.cs` |
| **B — listing + promotion** | `ListDeadLetters*`, `DeadLetterDto.cs`, `DeadLetterQuerySource.cs`, `RegisterEventTypeCommandHandler.cs`, `DeadLetterRepository.cs`, `Api/EventsEndpoints.Reads.cs`, `Api/EventTypesEndpoints.cs` (summary only) |
| **C — undeclare** | `Domain/SourceMode/*` (Undeclare + event + repo port), `UndeclareSourceMode*`, `SourceModeRepository.cs`, `Api/EventSourcesEndpoints.cs`, `EventSourceModeApi.cs` + its call sites |

**Contention files** (one owner each, edited last): `EventIngestionInfrastructureModule.cs`
(track C registers its handler; A and B add no registrations — the handlers'
new constructor parameters resolve from existing registrations),
`EndpointScopeDeclarationTests.cs` (C), `ci-shards/shard-N.filter` (every new
integration test class needs an entry — memory: a missing entry fails
deterministically).

---

## 9. Phase 4a colour: **RED**, with a characterisation net

**Declaration: behaviour-changing → red.** Every task in tasks.md marked `[RED]`
must be observed failing before its implementation, and the failure quoted in the
PR body (ADR-0139). A red test that arrives green is a phase-4 failure.

**The characterisation net** is the existing EventIngestion unit + integration
suite for the **undeclared** population and for **strict**. It must pass
**unmodified** except for exactly these, each named in the PR body:

| Edit | Why it is not a weakened gate |
|---|---|
| `EventTypeAdmissionTests.A_discovery_source_admits_an_unregistered_kind` → inverted to assert `Holds` (and renamed) | The user changed this behaviour (Q1). Its own doc comment records the old equivalence. |
| `StrictSourceIngestIntegrationTests` "discovery declared explicitly" case (line ~156) → asserts `202` + a held row | Same. |
| `InMemoryEventTypeAdmissionSource` / both `AdmitAllEventTypeAdmissionSource` fakes → implement `DeclaredSourceModesAsync`; `StrictSourcesCalls` → `DeclaredSourceModesCalls` | Port signature change. The **assertions** on call counts (`The_default_case_costs_one_query`, `An_empty_batch_queries_nothing`, `A_batch_costs_one_registry_query_per_strict_fab_not_per_envelope`) keep their numbers. |
| `EventSourceModeApi.RestoreDiscoveryAsync` → `UndeclareAsync`, and its call sites | FR-015: test hygiene for the shared stack, not an assertion. |
| `DeadLetter.Capture` call sites in `DeadLetterTests`, `PersistenceLoopHostedServiceTests`, `ListDeadLettersQueryHandlerTests`, `DtoSmokeTests` | Signature/DTO shape change; assertions on existing fields unchanged. |
| `EndpointScopeDeclarationTests` mapping count | Pinned denominator, bumped per its own convention. |

Anything else that needs editing to pass is evidence of an unintended behaviour
change: **stop and report**, do not adjust.

**Counterfactuals for phase 6** (each must turn exactly the named test red):
- Make `Holds` return true for undeclared pairs → the undeclared-default unit test
  and `UndeclaredSourceIngestIsUnchanged` integration test fail (option B by accident).
- Drop the hold branch from the batch handler → the MQTT-fast-path integration test
  fails; the HTTP tests stay green (spec §6 "if step 7 stores the event").
- Drop the 409-path promotion → the convergence integration test fails.
- Remove `state = 'Held'` from the `UPDATE`'s `WHERE` → no test should fail *except*
  the predicate test that seeds a `Refused` row with the same `(fab, kind)` and
  asserts it stays `Held`.
- Skip FR-014's `DELETE` → the migration test fails.

---

## 10. Constitution §VIII — the correction, drafted, **not applied** (FR-012)

ADR-0144: the lane does not amend the constitution, and the Q1 decision did not
sign this text. T021 files it as a `documentation` issue containing the draft
below. Proposed replacement for the sentences from *"A per-`(fab, Source)`
strict/discovery admission mode now exists too"* to *"not as a description of
today"*:

> A per-`(fab, Source)` admission mode exists too (spec 269), with three states
> since spec 317 (issue 2325): an **undeclared** pair is open — an event of an
> unregistered type is stored and fanned out as it always was; a pair declared
> **`discovery`** holds such an event in the dead-letter table, reason
> `UnknownEventType`, audit-only and never fanned out; a pair declared **`strict`**
> refuses it at the door. Registering a type promotes that fab's held rows of it.
> The undeclared state is a deliberate departure from decision 018's two flags
> (user decision, 2026-10-08, on #2325), so that no deployment changed behaviour
> when quarantine shipped. Schema validation of known types (issue 2326) does not
> exist.

---

## 11. Latency (§IV) — what phase 5 owes

Spec §7. Leg `Event → overlay state`, engaged. The default path's query count is
unchanged by construction (§6.1) and a unit test pins it
(`The_default_case_costs_one_query` keeps its number). Phase 5:
`IngestThroughputMeasurementTests` ×2 on `develop`, ×2 on the branch with the
measured pair undeclared, ×2 with it declared discovery and its kinds registered;
plus the admission query's span. Recorded in the verification note as a figure
*about this change*; §IV's table is not edited.

---

## 12. Coverage, metrics, review

- Coverage gates (ADR-0065): Domain ≥ 90 %, Application ≥ 80 % — every new branch
  has a unit test in tasks.md.
- ADR-0084 (advisory): `Capture`'s parameter count (§2.1), `StoreOrRefuse`'s
  complexity. Prefer the per-reason factories if `S107` fires.
- Reviewers: backend-reviewer; security-reviewer for the promotion path (an event
  source must not reach it — spec §4 auth block) and the `DELETE` scope.

---

## 13. Risks

| Risk | Mitigation |
|---|---|
| A test elsewhere in the shared stack ingests through a pair spec 269's tests left declared `discovery` in a persistent dev DB | FR-014's migration deletes those rows; FR-015 stops new ones |
| `ExecuteUpdateAsync` and the VO converters do not translate | §7 fallback (state only); no raw SQL |
| `ExecuteCreateAsync` persists a 202 as a replayable answer | §5: verify; either outcome is truthful, record which |
| The batch fast path commits holds but a later throw sends the batch to singles | Nothing is committed on a throw (one `SaveAsync`); singles re-assess and hold each — tested by the existing fallback test shape |
| A held-row storm (chatty unknown kind) grows `dead_letters` unboundedly | Pre-existing for parse failures; retention is out of scope; noted for #2780's inspector |

---

## 14. Gate — phase 2

- [ ] Plan aligns with the constitution and ADRs; no new ADR; no cross-context reference.
- [ ] §6.3's "handler writes the hold" shape accepted (no change to `IngestEventBatchResult`).
- [ ] §4 step 5 / §9: FR-014 migration and the named test inversions accepted.
- [ ] §10: constitution correction filed, not applied.
- [ ] Phase 4a colour **RED**, characterisation net as §9.
