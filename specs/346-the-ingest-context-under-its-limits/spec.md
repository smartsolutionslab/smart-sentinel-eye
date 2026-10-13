# Spec 346: The ingest context under its limits

**Feature Branch**: `refactor/2888-event-ingestion-metrics` (cut from `origin/develop` at `d259668e`)

**Created**: 2026-10-13

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Three judgement calls
are marked explicitly (§6).

**Input**: Issue [#2888](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2888),
*Clear the ADR-0084 metric warnings in Event Ingestion's Domain, Application and Infrastructure (26)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 346. Checked on 2026-10-13 against `origin/develop` (highest 342), every remote
branch via `git ls-tree` (highest 342), every worktree (`sse-2887` holds unpushed 345, `sse-1545`
holds nothing above 338) and the open PRs (#2811, #2837; neither carries a spec). Highest visible
is 345, so this takes 346. Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S104 <= 300 LOC/file, S138 <= 30 lines,
S107 <= 4 parameters, S1541 <= 10, advisory `warning`), ADR-0144 (autonomous lane; phase 4a
colours; the lane may not weaken a gate), ADR-0036 (smallest change), ADR-0050 (`[LoggerMessage]`
fields come from the template), ADR-0051 (per-context DI module), ADR-0141 (`Option<T>` in
Application), constitution §II (no primitives on a domain model) and §IV (latency budget).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written: `src/EventIngestion` holds no project file
(`MSB1003`). The equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/EventIngestion/Infrastructure/SmartSentinelEye.EventIngestion.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep EventIngestion
```

Result on `d259668e`: 0 errors, **26 hits, exactly the 26 the issue lists, at the same lines.
None is stale.** #2896 (spec 341, merged) touched only `Api/` files and did not move any of them.

## 2. Decision per hit

| # | File:line | Member | Rule | Decision |
|---|---|---|---|---|
| 1 | `Application/Commands/Handlers/IngestEventBatchCommandHandler.cs:37` | primary constructor (5 DI dependencies) | S107 | **Leave**: `IngestEventBatchCommandHandlerTests` and `IngestEventBatchStrictModeTests` construct it with five arguments; any change fails "tests unmodified", and folding dependencies together is a DI design change |
| 2 | `Application/Commands/Handlers/IngestEventCommandHandler.cs:25` | primary constructor (5) | S107 | **Leave**: same reason; constructed with five arguments by `IngestEventCommandHandlerTests`, `IngestEventBatchCommandHandlerTests`, `IngestEventQuarantineTests`, `IngestEventStrictModeTests` |
| 3-7 | `Application/Log.cs:29, 84, 89, 94, 99` | five `[LoggerMessage]` partials | S107 | **Leave**: parameters are the structured log template's fields (ADR-0050) |
| 8 | `Application/Queries/Handlers/ListDeadLettersQueryHandler.cs:14` | `HandleAsync` | S138 | **Clear** by extraction |
| 9 | `Application/Queries/Handlers/ListEventsQueryHandler.cs:67` | `BuildPagedQuery` (public static) | S138 | **Clear** by extraction; signature unchanged (called by `ListEventsTranslationTests`) |
| 10 | `Domain/DeadLetter/DeadLetter.cs:70` | `DeadLetter.Capture` (public, 7) | S107 | **Leave**: public Domain factory called by both ingest handlers, `MqttSubscriberHostedService`, `PersistenceLoopHostedService`, `DeadLetterTests`, `ListDeadLettersQueryHandlerTests` |
| 11 | `Domain/Event/Event.cs:39` | `Event.Ingest` (public, 8) | S107 | **Leave**: public Domain factory called by both ingest handlers, `EventBuilder`, `IngestEventQuarantineTests`, `IngestEventStrictModeTests`, `EventRepositoryOutboxTests`. The natural parameter object, `EventEnvelope`, is an Application type Domain cannot see |
| 12 | `Domain/SourceMode/SourceMode.cs:30` | `SourceMode.Declare` (public, 5) | S107 | **Leave**: public Domain factory called by `DeclareSourceModeCommandHandler`, `SourceModeBuilder`, `SourceModeTests`, `SourceModeCommandHandlerTests`, `ListSourceModesQueryHandlerTests` |
| 13 | `Infrastructure/EventIngestionInfrastructureModule.cs:39` | `AddEventIngestionInfrastructure` | S138 | **Clear** by extraction (public signature unchanged) |
| 14 | `Infrastructure/Ingress/MqttSubscriberHostedService.cs:198` | `TryParseEnvelope` (internal static) | S138 | **Clear** by extraction, with a private address value (J1); signature unchanged |
| 15 | `Infrastructure/Ingress/PersistenceLoopHostedService.cs:1` | file, 310 LOC | S104 | **Clear** by making the class `partial` and moving its private outcome vocabulary (`Outcome`, `Ending`) to `PersistenceLoopHostedService.Ending.cs` (J2) |
| 16 | `Infrastructure/Log.cs:18` | `IngestFailed` `[LoggerMessage]` | S107 | **Leave**: ADR-0050, as 3-7 |
| 17 | `Infrastructure/Persistence/CatalogFabStorageReadiness.cs:124` | `ReadProvisionedFabsAsync` (private static) | S138 | **Clear** by extraction (J3: covered by Integration.Tests only) |
| 18 | `Infrastructure/Persistence/Configurations/DeadLetterConfiguration.cs:12` | `Configure` | S138 | **Clear** by extraction |
| 19 | `Infrastructure/Persistence/Configurations/EventConfiguration.cs:25` | `Configure` | S138 | **Clear** by extraction |
| 20 | `Infrastructure/Persistence/Configurations/RegisteredEventTypeConfiguration.cs:19` | `Configure` | S138 | **Clear** by extraction |
| 21 | `Infrastructure/Persistence/Configurations/SourceModeConfiguration.cs:19` | `Configure` | S138 | **Clear** by extraction |
| 22 | `Infrastructure/Persistence/Configurations/WebhookIntegrationConfiguration.cs:12` | `Configure` | S138 | **Clear** by extraction |
| 23 | `Infrastructure/Persistence/EventRepository.cs:84` | `SaveAsync` | S138 | **Clear** by extraction (synchronous helpers only; no new `await`) |
| 24-26 | `Infrastructure/Persistence/Migrations/*` (3) | generated `Up`/`Down` | S138 | **Leave**: the issue forbids touching migrations; generated and already applied |

**Cleared: 12** (11 S138, 1 S104). **Left: 14** (6 `[LoggerMessage]`, 3 migrations, 3 public
Domain factories, 2 DI constructors). Expected after-count: **14**, every one named in the PR body
with its reason.

## 3. User story

### US1 (P1): Event Ingestion's non-Api layers meet ADR-0084 wherever that is mechanical

As a maintainer, I want the 12 clearable hits gone, each by an extraction, a partial-file split
or a private value that names one concept, **without** any change an MQTT sender, an HTTP caller,
another context, a log reader or the database could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing EventIngestion test passes unmodified (happy path)
  Given EventIngestion.Domain.Tests (185), Application.Tests (150), Infrastructure.Tests (72)
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: An MQTT delivery is refused with exactly today's reason, in today's precedence (bad request)
  Given a topic of the wrong shape / an illegal fab segment / an illegal source / an illegal device
    / JSON that does not parse / a JSON null body / a payload the Payload rule rejects
    / an empty event id / an illegal kind
  When TryParseEnvelope reads it
  Then Envelope is null and Error is exactly today's text (prefix and inner message)
  And a delivery that is wrong in both the address and the body reports the address
  And a delivery wrong in both payload and kind reports the payload

Scenario: A well-formed MQTT delivery maps every field to its own slot
  Given distinct fab, source, device, event id, kind, occurredAt and payload
  Then the envelope carries each one in its own property

Scenario: The event listing filters, bounds and orders exactly as today
  Given events across two fabs, sources, devices, kinds and times
  Then each filter alone narrows strictly (> after, < before), the cursor is strict on IngestedAt,
    order is IngestedAt descending then Id descending, and pageSize + 1 rows are taken
  And the fully-filtered, cursor-bounded query still translates to SQL offline

Scenario: The dead-letter listing defaults, caps and maps exactly as today
  Given a limit of 0 or below
  Then 100 rows are returned of 150
  And every DTO field carries its own value, with an unattributed fab and a parse-failure kind as null

Scenario: A save with a failing announcement offers every event and commits nothing (conflict)
  Given three pending events whose second dispatch throws
  Then all three are dispatched, one per call, in order
  And an AggregateException with today's message ("1 of 3 domain event(s) could not be captured; ...")
    carries the one failure, and commit is never called
  And cancellation during dispatch propagates rather than being collected

Scenario: The EF model is unchanged
  Then EventIngestionDbContext's logical shape (tables, keys, columns, store types, nullability,
    lengths, converters, defaults, concurrency tokens, owned types, foreign keys, indexes with
    filters) equals the table captured on d259668e

Scenario: The DI composition is unchanged
  Then AddEventIngestionInfrastructure registers the same EventIngestion-owned services and
    factories, in the same order, with the same lifetimes, hosted services included
```

**Auth scenario: N/A.** No authorisation boundary moves. Fab scoping on the listings (spec 018
FR-001/FR-009/FR-011) stays in the same expression and is pinned above, not changed. The Api layer,
where `RequireScope` and fab resolution live, is not touched.

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 14 "Leave" rows after the change.
3. CI's Integration.Tests `EventIngestion/*` green: they boot the real stack, so they cover the EF
   mapping against Postgres, the catalog read (`FabStorageRefusalIntegrationTests`,
   `FabPartitionProvisioningIntegrationTests`), MQTT ingest end to end
   (`MissingPayloadIsDeadLetteredIntegrationTests`, `PoisonDeliveryEscapeIntegrationTests`,
   `OutageRecoveryIntegrationTests`) and the outbox (`OutboxSharesTheWritesFateTests`). Docker-only,
   so observed in CI, not locally.

## 4. Functional requirements

- **FR-001** The 12 "Clear" hits of §2 report nothing after the change. If an extraction leaves a
  hit, the PR says so with the measured value; it is never suppressed.
- **FR-002** No public, `internal` or `protected` signature changes. `AddEventIngestionInfrastructure`,
  `ListEventsQueryHandler.BuildPagedQuery`, `MqttSubscriberHostedService.TryParseEnvelope`,
  `PersistenceLoopHostedService.Because`, every `Configure`, every Domain factory, everything in
  `Shared.Contracts` and every `Log.cs` partial keep their signatures.
- **FR-003** Refusal text, refusal precedence, log calls (method, arguments, order), dispatch order,
  the commit-after-dispatch order (spec 021 FR-001) and query shape are identical for every input.
- **FR-004** No new primitive on a Domain type (constitution §II). No Domain file changes.
- **FR-005** DI registrations keep their order and lifetimes; EF configuration produces the same
  model.
- **FR-006** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change; no
  guard edited; no file under any `Api/` or `Migrations/` directory touched; no existing test edited.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 14 "Leave" rows.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted.
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  touched projects; coverage gates hold (Domain >= 90 %, Application >= 80 %).

## 6. Judgement calls (marked)

- **J1. A private address value in Infrastructure.** `TryParseEnvelope`'s tail needs fab, source,
  device and the deserialised body: five with the payload value. The first three are one concept,
  the address the topic names, so they become
  `private readonly record struct DeliveryAddress(FabIdentifier Fab, Source Source, DeviceIdentifier Device)`.
  All value objects; private; Infrastructure, so outside §II's scope in any case.
- **J2. S104 by partial file, not by extraction.** The persistence loop is 310 LOC and every method
  in it is already under the method limits; nothing is too long, the file is. Moving the outcome
  vocabulary (`Outcome`, `Ending`, about 25 LOC) to a second partial file is the change that
  produces **identical IL** for every method body. The `Ending` invariant comment (a rejection
  without a reason is unrepresentable) moves with the type.
- **J3. Clearing a member only Integration.Tests reach.** `CatalogFabStorageReadiness.ReadProvisionedFabsAsync`
  talks only to Postgres's catalog; the unit tests drive the cache policy through the internal seam
  and never call it. It is cleared anyway because the extraction moves statements verbatim (the SQL
  literal unchanged, the parameter name unchanged, disposal order unchanged), and CI's
  `FabStorageRefusalIntegrationTests` exercises it against a real catalog. The PR must quote that
  job green before merge.

No defect was found while reading the touched members, so this is not a refactor-plus-fix.
Recorded and **not** changed: `ListEventsQueryHandler`'s cursor is strict on `IngestedAt` alone, so
two events sharing a microsecond across a page boundary can be skipped; the code says so and calls
it accepted. Pinned as-is; if anyone thinks it wrong, that is a separate issue.

**Prose that will drift.** `ConcurrencyConflictDeclarationTests.cs:328` (a string, not an
assertion on the file) cites `EventConfiguration.cs:30`, and
`InferencePayloadRoundTripIntegrationTests.cs:31,40` cite `EventConfiguration.cs:72,73`. Neither
reads the file. Tests may not be edited in this spec, so the PR body lists the new line numbers;
`HasKey` should stay at line 30 if `Configure` keeps its first statements in place.

## 7. Latency budget impact (constitution §IV)

**Leg: Event → overlay state (RabbitMQ + projection), <= 200 ms. Touched, not changed.** Four
members sit on the MQTT ingest path that feeds this leg: `TryParseEnvelope` (per delivery),
`PersistenceLoopHostedService` (the drain), `EventRepository.SaveAsync` (dispatch into the outbox,
then commit) and `EventConfiguration` (model build, once per process). The changes add **no I/O,
no `await`, no lock and no per-event allocation** beyond one stack-only `DeliveryAddress` per parse:

- the persistence loop's split is file layout only (identical IL);
- `SaveAsync`'s helpers are synchronous; the dispatch loop and its awaits stay in `SaveAsync`;
- EF configuration runs once when the model is built, not per request.

No re-measurement is owed for a change with no runtime delta; the PR states this reasoning rather
than citing a figure. `CatalogFabStorageReadiness` is on the HTTP direct-write path (cached, 30 s
TTL) and not on this leg's hot path.

## 8. Out of scope

Every `Api/` file, migrations, tests' own metrics, the 14 left hits, `ServiceDefaults` hits the same
build may print, and the drifting line-number prose in §6.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052), sentence-style names (ADR-0053), `Ensure.That`
guards (ADR-0105), `var` wherever it compiles, collection expressions, fluent DI chains, one
parameter per line when wrapping, no leading underscores (CLAUDE.md house rules). No
Testcontainers (ADR-0103).
