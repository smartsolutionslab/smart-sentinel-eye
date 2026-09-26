# Tasks 269 — The unknown a source refuses

**Spec:** `./spec.md` · **Plan:** `./plan.md`
**Phase:** 3 (Tasks) · **Drafted:** 2026-09-26 · **Re-verified:** 2026-09-27 against `2f477474`
**Issue:** #2324. It closes on merge, and #2325 and #2326 stay open.
**Engineer:** `backend-engineer`, single engineer. There is no frontend, infra or
realm work: the scope is reused and `Scope.cs` and the realm are untouched.
**Phase 4a colour: RED** (behaviour-changing), with the existing ingest suite as a
characterisation net. See `plan.md` §9.

Legend: `[ID] [P?] [Story]`. `[P]` means disjoint files, safe to run concurrently.

---

## The two prohibitions that govern this list

1. **No quarantine.** Discovery admits exactly as today. Nothing is persisted for
   an admitted unknown event. That is #2325.
2. **No file on the ingest *edge* changes.** `Api/EventsEndpoints.*.cs` and
   `Infrastructure/Ingress/*` stay as they are. The refusal reaches HTTP through
   `ToProblem()` and MQTT through the existing `RefusedEnvelope` / `Because`
   dead-letter path (spec FR-009). **The two handlers are the only ingest files
   opened.**

---

## Phase A — Prelude (blocks everything)

**Why it exists:** without it, the 4a tests fail `CS0246` or fail at DI
resolution, and neither is a red (plan §9). Phase A code is **not scaffolding**:
every guard, signature and call site written here survives into Phase C.

- [ ] **T001 [US1]** `src/EventIngestion/Domain/SourceMode/`: **the types, with no
  behaviour**. The files are all new, and no existing file is opened.
  - `SourceModeIdentifier.cs`: fully implemented (copy
    `RegisteredEventTypeIdentifier`).
  - `EventTypeMode.cs`: statics `Strict` / `Discovery` and `ToString()`.
    **`From` returns `Discovery` unconditionally.**
  - `DeclaredAt.cs`: `From` guards and returns the value **unnormalised**.
  - `Declaration.cs`: guards and construction only.
  - `Events/SourceModeDeclaredDomainEvent.cs`,
    `Events/SourceModeChangedDomainEvent.cs`: fully implemented; they are data.
  - `ISourceModeRepository.cs`: interface only (plan §2).
  - `SourceMode.cs`: the members from plan §2.
    - `Declare` guards, then sets `Id`, `Fab` and `Source`, leaves `Mode` and
      `Declaration` at `null!`, and raises nothing.
    - `Change` is guards only.
  - `tests/Architecture.Tests/PrimitiveBoundaryTests.cs`: root count **13 → 14**
    (develop's 13 includes spec 258's `Wall`).
  - **Status at phase 3: already written, staged, uncommitted** (plan §0). The
    eight files and the root-count edit were rebased onto `2f477474` and the
    Domain project builds clean in Release. Phase 4 **verifies** the "Done when"
    below and commits; it does not rewrite. If verification fails, fix the file,
    do not discard the set.
  - **Done when:** `dotnet build -c Release` is clean, `PrimitiveBoundaryTests` is
    green (14 roots), and no Phase B domain assertion could pass.

  *Commit:* `feat(event-ingestion): a source-mode aggregate with no behaviour`

- [ ] **T002 [US1] [US2]** Application and ingest wiring, **with the behaviour
  withheld**.
  - **New files:**
    - `Application/Ingress/IEventTypeAdmissionSource.cs` (plan §6.1)
    - `Application/Ingress/EventTypeVerdicts.cs`: `AdmitAll`; **`Refuses` returns
      `false`**
    - `Application/Ingress/EventTypeAdmission.cs`: **`AssessAsync` returns
      `AdmitAll` without calling its source**
    - `Application/Commands/DeclareSourceModeCommand.cs`,
      `DeclareSourceModeErrors.cs`, `Handlers/DeclareSourceModeCommandHandler.cs`.
      The handler returns `Success(SourceModeIdentifier.New())` and touches no
      repository.
    - `Application/Commands/ChangeSourceModeCommand.cs`,
      `ChangeSourceModeErrors.cs`, `Handlers/ChangeSourceModeCommandHandler.cs`.
      The handler returns `SourceModeNotDeclared`.
    - `Application/Queries/ListSourceModesQuery.cs`, `ListSourceModesErrors.cs`,
      `Handlers/ListSourceModesQueryHandler.cs` (returns `[]`),
      `ISourceModeQuerySource.cs`
    - `Application/DTOs/SourceModeDto.cs`
    - `Infrastructure/Persistence/EventTypeAdmissionSource.cs`: both methods return
      **empty sets without querying**
  - **Shared files:**
    - `Application/Commands/IngestEventErrors.cs`: the `EventTypeNotRegistered`
      variant and its `Failures` factory, **fully written** (plan §6.3).
    - `IngestEventCommandHandler.cs` and `IngestEventBatchCommandHandler.cs`: take
      `EventTypeAdmission admission`, and have the **final** call sites and
      precedence from plan §6.2: assess after the existence check, then the skew
      rule, then `Refuses`. With `AdmitAll` this changes nothing observable.
    - `Infrastructure/EventIngestionInfrastructureModule.cs`: register
      `EventTypeAdmission` and `IEventTypeAdmissionSource` → `EventTypeAdmissionSource`
      (both scoped), and the three new handlers.
    - `tests/EventIngestion.Application.Tests/Commands/IngestEventCommandHandlerTests.cs`
      and `IngestEventBatchCommandHandlerTests.cs`: **constructor calls only** —
      the `Handler(...)` / `SingleHandler(...)` factories **and** the three direct
      `new(...)` constructions in `IngestEventCommandHandlerTests` (lines 39, 57,
      77 at `2f477474`). Pass an `EventTypeAdmission` over an admit-all fake
      source (`Fakes/AdmitAllEventTypeAdmissionSource.cs`). **No assertion
      changes.**
    - The stash *"T002-in-progress"* holds a draft of this task written against
      `b23b9307`. It may be consulted; it is not authoritative, and every file
      taken from it is re-read against this plan.
    - `tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs:494-495`:
      register `EventTypeAdmission` plus an admit-all fake source. **DI lines
      only.**
  - Error records are fully written, because the 4a tests assert on their `Code`s.
  - Handlers destructure their command first when they read two or more fields
    (house rule; `HandlerDeconstructionTests`).
  - **No `NotImplementedException` anywhere.** A throw errors a test instead of
    failing it.
  - **Done when:** Release build is clean, and **the whole existing EventIngestion
    suite (unit, infrastructure, and the EventIngestion integration tests) is
    green with no assertion edited.** Capture that output; it is the
    characterisation baseline for the PR.

  *Commit:* `feat(event-ingestion): an event-type admission that admits everything`

---

## Phase B — Tests first, observed RED (T002 → all of B)

Owner: **`test-writer`**. The tests are written and run, and the **verbatim output**
goes into the engineer's brief and the PR body (ADR-0139, ADR-0144). The engineer
may not edit them. Names are sentence-style (ADR-0053), data comes from
hand-written builders (ADR-0054), and assertions use Shouldly (ADR-0052).

- [ ] **T003a [P] [US1] [US2]**
  `tests/EventIngestion.Domain.Tests/SourceMode/SourceModeTests.cs` and
  `SourceModeBuilder.cs`:
  1. `Declare_sets_the_declared_mode`
  2. `Declare_records_who_declared_it_and_when`
  3. `Declare_raises_a_SourceModeDeclaredDomainEvent_naming_fab_source_and_mode`
  4. `Change_to_a_different_mode_flips_it_and_raises_a_SourceModeChangedDomainEvent`
     (asserting both the previous and the new mode)
  5. `Change_to_the_same_mode_raises_nothing_and_leaves_the_declaration`
  6. `Change_records_who_changed_it_and_when`
  7. `Declare_refuses_a_null_fab` / `_source` / `_mode`. **These are expected green
     on arrival**, because they assert T001's guards. Say so in the 4a note.

- [ ] **T003b [P] [US1]**
  `tests/EventIngestion.Domain.Tests/SourceMode/SourceModeValueObjectTests.cs`:
  1. `EventTypeMode_From_round_trips_strict_and_discovery`
  2. `EventTypeMode_From_refuses_an_unknown_mode`
  3. `EventTypeMode_From_is_case_sensitive`: `"Strict"` is refused
  4. `DeclaredAt_stores_the_instant_in_UTC`
  5. `SourceModeIdentifier_New_is_a_version_7_guid`

- [ ] **T003c [P] [US1]**
  `tests/EventIngestion.Application.Tests/Ingress/EventTypeAdmissionTests.cs` plus
  `tests/EventIngestion.Application.Tests/Fakes/InMemoryEventTypeAdmissionSource.cs`.
  The fake is hand-written and **counts its calls**; no mocking framework.
  1. `An_undeclared_source_admits_an_unregistered_kind`. **Green on arrival; this is
     the characterisation case.**
  2. `A_discovery_source_admits_an_unregistered_kind`. **Green on arrival.**
  3. `A_strict_source_refuses_an_unregistered_kind`
  4. `A_strict_source_admits_a_registered_kind`
  5. `Strict_on_one_source_leaves_the_fabs_other_sources_open`
  6. `Strict_in_one_fab_leaves_the_same_source_in_another_fab_open`
  7. `A_retired_kind_is_refused_under_strict`. The fake must model state, so that
     the fake and `GetRegisteredAsync` agree on what "registered" means (plan
     §6.1).
  8. `The_default_case_costs_one_query`: one `StrictSourcesAsync` call and **zero**
     `RegisteredKindsAsync` calls, for a batch of many envelopes
  9. `A_batch_costs_one_registry_query_per_strict_fab_not_per_envelope`: 200
     envelopes, 2 strict fabs, 5 kinds, so exactly **2** `RegisteredKindsAsync`
     calls
  10. `An_empty_batch_queries_nothing`

- [ ] **T003d [P] [US1]** Ingest-handler cases. They go in **new** files, so the
  existing ones stay characterisation-only:
  `tests/EventIngestion.Application.Tests/Commands/IngestEventStrictModeTests.cs`
  and `IngestEventBatchStrictModeTests.cs`.
  1. `A_strict_source_refuses_an_unregistered_kind` (single): the result is
     `EventTypeNotRegistered`, **nothing is added to the repository**, and no
     `IngestVolume` is recorded.
  2. `A_redelivery_is_still_a_redelivery_under_strict` (single): the result is
     `EventAlreadyIngested`.
  3. `Future_skew_outranks_an_unregistered_kind` (single and batch).
  4. `The_batch_refuses_only_the_unregistered_envelopes`: a mixed batch stores the
     registered ones and returns the others as `RefusedEnvelope` with an
     `EventTypeNotRegistered` reason.
  5. `The_batch_consults_admission_once`: the fake source's call count is checked.
  6. `The_refusal_names_fab_source_and_kind`: the `Message` contains all three.
  7. `The_refusal_fits_a_dead_letter_reason`: `Because(error)` is not truncated
     for a maximum-length `Kind` and fab. This one lives in
     `EventIngestion.Infrastructure.Tests`, which has `InternalsVisibleTo`, and is
     **expected green on arrival**.

- [ ] **T003e [P] [US1] [US2]**
  `tests/EventIngestion.Application.Tests/Commands/SourceModeCommandHandlerTests.cs`,
  `Queries/ListSourceModesQueryHandlerTests.cs`, and
  `Fakes/InMemorySourceModeRepository.cs`:
  1. `Declaring_a_mode_stores_it_against_the_resolved_fab`
  2. `Declaring_a_pair_already_declared_is_refused`: `SOURCE_MODE_ALREADY_DECLARED`, 409
  3. `Declaring_the_same_source_in_another_fab_is_allowed`
  4. `Changing_a_declared_mode_flips_it`
  5. `Changing_an_undeclared_pair_is_not_found`: `SOURCE_MODE_NOT_DECLARED`, 404
  6. `Changing_with_a_stale_expected_version_is_refused`: `SOURCE_MODE_STALE`, 409
  7. `The_version_gate_runs_after_the_lookup`: stale against undeclared is 404,
     not 409
  8. `Listing_returns_only_the_callers_fabs_ordered_by_fab_then_source`

- [ ] **T003f [US1] [US2]** Integration tests, in `tests/Integration.Tests/EventIngestion/`:
  - `EventSourceModeApi.cs`: the helper. It has `SetAsync(fab, source, mode)` that
    is **idempotent** (GET → POST if absent, else PUT with the listed version),
    and a `RestoreDiscoveryAsync` for `finally` blocks.
  - `EventSourceModeIntegrationTests.cs`
  - `StrictSourceIngestIntegrationTests.cs`
  - `EventSourceModeAuthorizationIntegrationTests.cs`

  **Before writing any test, choose the `(fab, source)` pairs and prove them
  unused.** Grep `tests/` for every other integration test that ingests through
  each candidate pair: HTTP `manual`/`webhook` per fab, and MQTT
  `fab/<fab>/<source>/`. Put the grep and its output in the 4a note (plan R1).
  - Candidates: `berlin` + `manual` for HTTP, `berlin` + `inference` for MQTT,
    persona `op-berlin@berlin.test`.
  - If the grep shows either is used, pick another and say why.

  **Each new integration test class is added to exactly one
  `tests/Integration.Tests/ci-shards/shard-N.filter`** (shared file — serial).
  A missing entry fails CI deterministically, not as a flake; develop's
  `7aab60de` is a fix for exactly this.

  **Every strict test restores `discovery` in `finally`.** Kinds are run-unique
  (`Guid.NewGuid():N`), and every status assertion appends
  `aspire.RecentLogs("event-ingestion")`.

  **Endpoint tests:**
  1. `A_declared_mode_is_listed_back`
  2. `Declaring_the_same_pair_twice_is_refused_with_409`
  3. `An_unknown_source_or_mode_is_refused_with_400`: the body, and `{source}` on
     `PUT`
  4. `A_multi_fab_caller_that_names_no_fab_is_refused_with_400`
  5. `Naming_a_fab_the_caller_does_not_hold_declares_nothing`: 403, **and the
     other fab's list is asserted unchanged**
  6. `An_anonymous_caller_is_refused_with_401`
  7. `Changing_without_If_Match_is_refused_with_428`
  8. `Changing_with_a_stale_If_Match_is_refused_with_409`
  9. `Changing_an_undeclared_pair_is_404`
  10. `Listing_never_shows_another_fabs_modes`
  11. `A_repeated_declare_with_the_same_idempotency_key_replays_the_same_201`

  **Ingest tests.** Each asserts its arrange step's status first, with a message:
  12. `A_strict_manual_source_refuses_an_unregistered_kind_with_400`: the problem
      title is `EVENT_TYPE_NOT_REGISTERED`, and `GET /events` does not return it
  13. `A_strict_manual_source_admits_a_registered_kind`
  14. `A_strict_inference_source_dead_letters_an_unregistered_kind_published_over_MQTT`:
      the reason starts `EVENT_TYPE_NOT_REGISTERED:`, and the event is not in
      `GET /events` (copy `DeadLetterReasonIntegrationTests.PublishAsync`)
  15. `Strict_on_inference_leaves_plc_in_the_same_fab_open` (MQTT)
  16. `Switching_back_to_discovery_admits_the_unknown_again`
  17. `An_undeclared_source_still_ingests_an_unknown_kind`. **Green on arrival**;
      it is spec §4's default scenario.

  **Auth**, with a **planted event-source client** holding only
  `sse.events.write`, created and torn down in the test. Copy
  `EventTypeRegistryAuthorizationIntegrationTests`. A `management-web` token holds
  every `sse.*` scope and cannot show the negative (spec 143 FR-010).
  18. `An_event_source_token_can_neither_declare_nor_change_a_source_mode`: 403
      on both

- [ ] **T003g [US1]** Run all of Phase B, capture the **verbatim** output, and
  check it against `plan.md` §9's written prediction.
  - **These are green on arrival and documented as such:** T003a.7, T003c.1–2,
    T003d.7, T003f.17. **Any other first-run pass is a phase-4 failure.**
  - Re-run the **characterisation suite** from T002 and quote it green.
  - Serial: this task runs what T003a–f wrote.

  *Commit:* `test(event-ingestion): a strict source nothing enforces yet` (red)

---

## Phase C — Implementation (T003g → C)

Owner: **`backend-engineer`**. **May not edit any file under `tests/`** except the
pinned counts in T010.

- [ ] **T004 [P] [US1] [US2]** `Domain/SourceMode/`: fill what T001 withheld.
  - `EventTypeMode.From`'s `switch` and throw
  - `DeclaredAt`'s UTC normalisation
  - `Declare`'s mode, declaration and `Raise`
  - `Change`'s early return, flip, new declaration and `Raise`
  - **Done when:** T003a and T003b are green.

  *Commit:* `feat(event-ingestion): a source mode is declared and changed`

- [ ] **T005 [P] [US1]** `Application/Ingress/EventTypeAdmission.cs` and
  `EventTypeVerdicts.cs`: plan §6.1's algorithm.
  - **Done when:** T003c and T003d are green, **including the query-count tests**.

  *Commit:* `feat(event-ingestion): a strict source refuses an unregistered event type`

- [ ] **T006 [P] [US1] [US2]** The three command and query handlers (plan §6.4).
  The change handler's order is load-bearing.
  - **Done when:** T003e is green.

  *Commit:* `feat(event-ingestion): source-mode handlers enforce uniqueness and the version gate`

- [ ] **T007 [US1]** Infrastructure persistence (plan §7).
  - New: `SourceModeConfiguration.cs`, `SourceModeRepository.cs`,
    `SourceModeQuerySource.cs`.
  - Fill `EventTypeAdmissionSource.cs` with both no-tracking queries, comparing
    against VOs directly.
  - Shared: `EventIngestionDbContext.cs` (`DbSet`) and
    `EventIngestionInfrastructureModule.cs` (the repository and query source).

  *Commit:* `feat(event-ingestion): persist source modes and read them at ingest`

- [ ] **T008 [US1]** The migration: `dotnet ef migrations add SourceModes --project
  src/EventIngestion/Infrastructure`.
  - Designer and snapshot are **generated**.
  - Verify the generated `Up` creates `ux_source_modes_fab_source` **with no
    filter**.
  - **Done when:** `MigrationRunner` applies it to a fresh database.
  - Depends on T007.

  *Commit:* `feat(event-ingestion): a source_modes table`

- [ ] **T009 [US1] [US2]** The endpoints (plan §5).
  - New: `Api/EventSourcesEndpoints.cs`, `Api/Requests/DeclareSourceModeRequest.cs`,
    `Api/Requests/ChangeSourceModeRequest.cs`.
  - Shared: `Api/Program.cs`, one `MapEventSourcesEndpoints()`.
  - Scopes go on each mapping. The summary contains `Required scope: …`. Each
    mapping has its own `ProducesProblem(403)`.
  - `PUT` reads `If-Match` **before** anything else.
  - `POST` honours `Idempotency-Key`, scoped by caller.
  - Depends on T006.

  *Commit:* `feat(event-ingestion): declare, list and change source modes over HTTP`

- [ ] **T010 [US1]** `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`:
  **re-read** `EndpointFileCount` and `RouteHandlerMappingCount`, then add **+1**
  and **+3**. At `2f477474` that is 14 → 15 and 65 → 68 (it was 13 / 60 when
  first drafted — spec 258 moved it).
  - Depends on T009.

  *Commit:* `test(architecture): three more mappings in one more endpoint file`

- [ ] **T011 [P] [US1] [US2]** `Application/Log.cs`: `SourceModeDeclared`,
  `SourceModeChanged`, `UnregisteredEventTypeRefused` (ADR-0050). Wire the third
  into `IngestEventCommandHandler`. The batch reuses `BatchEnvelopeRejected`.
  - This is a shared file: `[P]` only against tasks that do not touch it.

  *Commit:* `feat(event-ingestion): log source-mode writes and strict refusals`

- [ ] **T012 [US1] [US2]** Run everything.
  - **Done when:**
    - T003a–f are green;
    - **the characterisation suite is green with no assertion changed since T002**;
    - the Release build is analyzer-clean;
    - coverage holds (Domain ≥ 90 %, Application ≥ 80 %).
  - Stop any running AppHost before building.

---

## Phase D — The constitution's factual correction (gate-dependent)

- [ ] **T014 [US1]** `.specify/memory/constitution.md` §VIII, first bullet.
  - Replace **only** the clause *"no per-source strict/discovery mode (issue
    2324)"* with a statement that a per-`(fab, Source)` mode exists, defaults to
    discovery, and that strict refuses unregistered types (spec 269).
  - **Keep** *"no promotion path (issue 2325)"*, the *"unknown type is ingested
    like any other rather than quarantined"* clause (still true under the
    discovery default), and *"not as a description of today"*.
  - **This is a status correction, not an amendment** (spec FR-016).
  - **If the phase-1 gate rejected FR-016, skip this task** and say so in the PR.

  *Commit:* `docs(constitution): a source can be strict`

(T013 is deliberately unused, so that T014 keeps spec 143's numbering for the
constitution task.)

---

## Phase E — Verify (phase 5; T012 → E)

- [ ] **T015 [US1] [US2]** Run spec §6 end to end on a real stack and write
  `specs/269-the-unknown-a-source-refuses/verification.md`.
  - **Step 8 (MQTT) and step 12 (restart) cannot be skipped.** Step 8 is the only
    one that observes the batch path. Step 12 is the only one that tells a stored
    mode apart from a cache.
  - **Step 13 (clean-up) is mandatory.** The dev database outlives the run.
  - **Latency (plan §10):**
    - `IngestThroughputMeasurementTests`, run twice on `develop`, twice on the
      branch with the pair undeclared, and twice with it strict and its kinds
      registered.
    - The admission query's span duration from the Aspire trace, or a statement
      that no such span exists.
    - Quote every figure. **Do not edit §IV's table.**
    - If the added p95 exceeds a few ms, file the projection follow-up.

---

## Phase F — Review (phase 6)

- [ ] **T016 [US1]** Run `plan.md` §9's seven counterfactuals, each expected to
  turn **exactly** the named test(s) red, and record the output in
  `verification.md`.
  - Counterfactual 3 (absent row treated as strict) must also turn the
    characterisation suite red, which proves that suite guards the default.
  - Counterfactual 4 (batch check removed) must leave every HTTP test green.
- [ ] **T017 [US1]** `/code-review`, plus `/security-review`: FR-013 decides who may
  relax a source's policing. Address or accept every finding in writing.

---

## Dependency graph

```
T001 (Domain skeleton — blocks everything)
  └─ T002 (Application skeleton + ingest wiring + characterisation baseline)
       ├─ T003a [P] ─┐
       ├─ T003b [P] ─┤
       ├─ T003c [P] ─┤
       ├─ T003d [P] ─┼─ T003g (run; quote the red + the characterisation green)
       ├─ T003e [P] ─┤        │
       └─ T003f     ─┘        │
                              ├─ T004 [P] ─┐
                              ├─ T005 [P] ─┤
                              ├─ T006 [P] ─┼─ T007 ── T008 ─┐
                              └─ T011 [P]  │                │
                                  T006 ── T009 ── T010 ─────┤
                                                            T012 ── T014 ── T015 ── T016 ── T017
```

**Foundational, and it blocks the fan-out:** T001, then T002. Nothing in Phase B
compiles before both, and nothing in Phase C is meaningful before T003g has
captured the red.

**T003f is not `[P]` against the others in spirit.** It needs the running stack,
and one machine runs one Aspire stack. It is disjoint in files, not in resources.

---

## Board (ADR-0037 phase-3 gate)

**Feature-level, not per-task.** `/speckit-taskstoissues` is **not** run. The gate
is that **#2324 is on Project #13**:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2324
```

`item-add` prints nothing on success. Verify by `content.url` with `--limit 2000`.

**Checked 2026-09-27:** `gh issue view 2324 --json projectItems` reports #2324 on
"Smart Sentinel Eye" (Project #13), status **In Progress**. No `item-add` was
needed.

---

## Gate — phase 3

- [ ] Tasks are atomic and each names the files it writes.
- [ ] **Spec G1 settled.** If the phase-1 gate chose configuration over the
      `SourceMode` table (spec §0.3), this list is re-cut before phase 4: T001's
      aggregate, T002's command/query files, T003a/T003e, T003f's endpoint tests,
      and T006–T010 drop; T003c/T003d, T005 and the ingest wiring stand.
- [ ] **#2324 is on Project #13** (feature-level).
- [ ] **Phase 4a colour declared: RED.** The documented green-on-arrival
      exceptions are T003a.7, T003c.1–2, T003d.7 and T003f.17, and no others.
- [ ] **The characterisation half is understood:** the existing ingest tests change
      a constructor or DI line in T002 and nothing else, ever.
- [ ] **The ingest edge stays shut:** `EventsEndpoints.*` and
      `Infrastructure/Ingress/*` are unchanged.
- [ ] **No quarantine is built** (#2325), and **no ADR is written**.
- [ ] Phase 5's latency obligation (T015) is accepted as a direct measurement, not
      a leg-histogram reading.
