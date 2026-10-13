# Tasks 346: The ingest context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2888 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T007 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T007.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/` or `Migrations/`; any existing test file; any
`Log.cs`; any Domain file; `Shared.Contracts`; suppressions, thresholds, `.editorconfig`,
`SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (26 lines on `d259668e`) and the
  §5 test runs (Domain 185, Application 150, Infrastructure 72, Architecture, the offline
  `ListEventsTranslationTests`).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [P] [US1] C1, `tests/EventIngestion.Application.Tests/Queries/ListDeadLettersMappingCharacterisationTests.cs`.
  Commit `test(event-ingestion): pin dead-letter listing default limit and DTO mapping`.
- [ ] **T002** [P] [US1] C2, `tests/EventIngestion.Application.Tests/Queries/ListEventsPagedQueryCharacterisationTests.cs`.
  Commit `test(event-ingestion): pin event listing filters, cursor and order`.
- [ ] **T003** [P] [US1] C3, `tests/EventIngestion.Infrastructure.Tests/ListEventsQueryTranslationCharacterisationTests.cs`.
  Commit `test(event-ingestion): pin that the fully filtered event listing translates`.
- [ ] **T004** [P] [US1] C4, `tests/EventIngestion.Infrastructure.Tests/MqttEnvelopeRefusalCharacterisationTests.cs`.
  Commit `test(event-ingestion): pin MQTT envelope refusal text and precedence`.
- [ ] **T005** [P] [US1] C5, `tests/EventIngestion.Infrastructure.Tests/EventRepositorySaveCharacterisationTests.cs`.
  Commit `test(event-ingestion): pin event repository dispatch order and failure aggregation`.
- [ ] **T006** [P] [US1] C6, `tests/Architecture.Tests/Persistence/EventIngestionRegistrationCharacterisationTests.cs`.
  Commit `test(architecture): pin event-ingestion infrastructure registrations`.
- [ ] **T007** [P] [US1] C7, `tests/EventIngestion.Infrastructure.Tests/EventIngestionModelCharacterisationTests.cs`.
  Commit `test(event-ingestion): pin the EF model's logical shape`.

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code.

Gate: the orchestrator holds seven verbatim green outputs.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T010** [P] [US1] `Application/Queries/Handlers/ListDeadLettersQueryHandler.cs`: plan §3.1. Hit 8.
  Commit `refactor(event-ingestion): extract dead-letter filter and DTO mapping`.
- [ ] **T011** [P] [US1] `Application/Queries/Handlers/ListEventsQueryHandler.cs`: plan §3.2. Hit 9.
  Commit `refactor(event-ingestion): extract event listing origin and time filters`.
- [ ] **T012** [P] [US1] `Infrastructure/EventIngestionInfrastructureModule.cs`: plan §3.3. Hit 13.
  Commit `refactor(event-ingestion): group infrastructure registrations`.
- [ ] **T013** [P] [US1] `Infrastructure/Ingress/MqttSubscriberHostedService.cs`: plan §3.4. Hit 14.
  Commit `refactor(event-ingestion): extract envelope construction from TryParseEnvelope`.
- [ ] **T014** [P] [US1] `Infrastructure/Ingress/PersistenceLoopHostedService.cs` +
  new `Infrastructure/Ingress/PersistenceLoopHostedService.Ending.cs` (one commit; they only build
  together): plan §3.5. Hit 15.
  Commit `refactor(event-ingestion): move the persistence loop's outcome types to a partial file`.
- [ ] **T015** [P] [US1] `Infrastructure/Persistence/CatalogFabStorageReadiness.cs`: plan §3.6. Hit 17.
  Commit `refactor(event-ingestion): extract the provisioned-fab catalog command`.
- [ ] **T016** [P] [US1] `Infrastructure/Persistence/Configurations/DeadLetterConfiguration.cs`: plan §3.7. Hit 18.
  Commit `refactor(event-ingestion): split dead-letter EF configuration`.
- [ ] **T017** [P] [US1] `Infrastructure/Persistence/Configurations/EventConfiguration.cs`: plan §3.7. Hit 19.
  Commit `refactor(event-ingestion): split event EF configuration`.
- [ ] **T018** [P] [US1] `Infrastructure/Persistence/Configurations/RegisteredEventTypeConfiguration.cs`: plan §3.7. Hit 20.
  Commit `refactor(event-ingestion): split registered event type EF configuration`.
- [ ] **T019** [P] [US1] `Infrastructure/Persistence/Configurations/SourceModeConfiguration.cs`: plan §3.7. Hit 21.
  Commit `refactor(event-ingestion): split source mode EF configuration`.
- [ ] **T020** [P] [US1] `Infrastructure/Persistence/Configurations/WebhookIntegrationConfiguration.cs`: plan §3.7. Hit 22.
  Commit `refactor(event-ingestion): split webhook integration EF configuration`.
- [ ] **T021** [P] [US1] `Infrastructure/Persistence/EventRepository.cs`: plan §3.8. Hit 23.
  Commit `refactor(event-ingestion): extract pending-event drain and failure aggregation`.

Per task: run plan §5's grep (the task's hit gone, no new hit) and the test suites of the touched
layer plus `Architecture.Tests`. T011 also runs the offline `ListEventsTranslationTests`. T012 and
T016-T020 also run `EventIngestion.Infrastructure.Tests` (C6/C7 are the net there).

## Phase 4: Verify (orchestrator)

- [ ] **T022** [US1] After evidence, verbatim: the §5 grep (exactly the 14 "Leave" rows), all §5
  test runs, `dotnet build -c Release` (0 errors), `dotnet format --verify-no-changes` on the two
  touched projects, coverage (Domain >= 90 %, Application >= 80 %). Confirm
  `git diff <T007 commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Domain/|Log\.cs'` is empty.
- [ ] **T023** PR to `develop` (`--base develop`). Body: T000 and T022 outputs, the 14 left hits each
  with its reason (spec §2), Phase 4a colour "characterisation", J1-J3, latency "Event -> overlay
  state leg touched, no runtime delta" with spec §7's reasoning, the cursor oddity left unchanged,
  the drifting line-number prose (spec §6) with new line numbers, `Closes #2888`. Re-check spec
  number 346 is still free. Integration.Tests observed in CI, and
  `FabStorageRefusalIntegrationTests` quoted green before merge (J3).

## Dependency graph

```
T000
T001..T007 (all [P]) ──> T010..T021 (all [P]) ──> T022 ──> T023
```

Phase 3 tasks own disjoint files and need no order among themselves. Nothing here is foundational
to another context: no `Shared.Kernel`, `Shared.Contracts`, `AppHost` or Aspire resource change.
