# Spec 442: A declared announcement outlives the broker

**Issue:** #3102 (feature-level, on Project #13) · **Branch:** `fix/3102-durable-outbox`
**ADRs:** **ADR-0185** (accepted 2026-10-23; the whole of this spec implements its *Implementation Notes* items 1-9),
ADR-0088 (amended by 0185; Wolverine defaults, Postgres outbox), ADR-0073 (contracts carry shape, not delivery policy),
ADR-0125 (the message store's own pool), ADR-0154 (outbox backlog check is Degraded), ADR-0049 (cancellation),
ADR-0103 (Aspire fixture, no Testcontainers), ADR-0144 (lane), ADR-0179 decision 5 (audit-before-disclosure, the
pressure). Constitution §IV, §Testing.
**Specs built on:** 021 (the outbox adapter), 328 (`durableInboxMessageTypes`, the pattern mirrored), 282 (the
non-retrying execution strategy pin).
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** red for new behaviour; characterisation for the untouched
outbox tests and the hot-path guard (plan §"Phase 4a").
**Security-sensitive:** **yes** (ADR-0185 decision 6): `security-reviewer` is mandatory at phase 6.
**Written 2026-10-23 against `develop` at `145c2503`.**

---

## Premise, checked at `145c2503` (ADR-0185 was written at `b5579a60`)

1. **Mechanism absent, model present.** `src/ServiceDefaults/WolverineDefaults.cs:40-49` has
   `durableInboxMessageTypes` and one `ConfigureListeners` call (`:113-165`, with the *replaces, not composes* comment
   at `:113-118`); there is no `ConfigureSending`, no `UseDurableOutbox`, no `UseDurableOutboxOnAllSendingEndpoints`
   anywhere in `src/` or `tests/`. Every RabbitMQ sender is Inline (ADR-0185 Context). Holds.
2. **The 16 types each have exactly one construction site in `src/`** (one `new <Type>(`, or `<Type> x = new(` for
   `SystemVariableDefinedV1`), each one line below the ADR table's line (a leading line moved; same statement):
   EventIngestion `WebhookIntegrationRevokedDomainEventHandler.cs:30`; Identity
   `RotateWebhookClientCommandHandler.cs:101,507`, `ClientRegisteredDomainEventHandler.cs:42,58`; CameraCatalog
   `Camera{Registered,Renamed,AddressChanged,Retired}DomainEventHandler.cs:20,27,28,27`; SystemVariables
   `VariableDefinedDomainEventHandler.cs:28`, `VariableArchivedDomainEventHandler.cs:79`; OverlayDesigner
   `OverlayRevision{Published,Archived}DomainEventHandler.cs:32,26`; LayoutComposition
   `LayoutRevision{Published,Archived}DomainEventHandler.cs:39,28`, `WallConfiguredDomainEventHandler.cs:26`.
3. **None of the 16 has a hot-path publisher.** The domain events behind them are raised only by `Variable.Define`
   / `Variable.Archive`, `Layout.PublishRevision` / `ArchiveRevision`, `Wall.Create` / `Wall.EditScenes`,
   `Overlay.*Revision`, the Camera admin methods and the Identity/EventIngestion admin commands, and those are
   called only from `*CommandHandler`s that only `Api` endpoints construct (`DefineVariableCommandHandler.cs:40`,
   `ArchiveVariableCommandHandler.cs:41`, `ArchiveRevisionCommandHandler.cs:43` ×2, `EditWallScenesCommandHandler.cs:53`,
   `WallEndpoints.cs:71`). The hot-path integration handlers (`SystemVariableValueRequestedV1Handler` →
   `SetVariableValueCommand`; `WallSceneSwitchRequestedV1Handler` → `Wall.SwitchTo`; `OverlayHighlightRequestedV1`,
   `ResolvedOverlayTextChangedV3`, `WallSceneChangedV1` handlers) raise none of those events. `Api` endpoints call
   command handlers directly (`services.Handler.HandleAsync`, no `IMessageBus.InvokeAsync` in any `src/*/Api`), so
   `OutboxEventBus` takes its DbContext-outbox branch for all 16. **Holds; the lane is not blocked.**
4. **No schema change.** `PersistMessagesWithPostgresql(postgresConnection, outboxSchema)` (`WolverineDefaults.cs:87`)
   with `AutoBuildMessageStorageOnStartup = CreateOrUpdate` already creates `<schema>.wolverine_outgoing_envelopes`
   per context; `OutboxBacklogHealthCheck` reads it. No migration, no `MigrationRunner` change, no AppHost change.
5. **The `DisableRetry` precondition is already pinned, Docker-free, for every context:**
   `tests/Architecture.Tests/Persistence/ExecutionStrategyCharacterisationTests.cs` (theory over
   `PostgresComposition.AllContexts`) asserts the non-retrying `NpgsqlExecutionStrategy`, set by
   `PostgresTelemetry.cs:98`. What is *not* yet observed is that a durable publish actually depends on it (plan T006).
6. **Domain events dispatch before commit, outside `SaveChanges`** (e.g. `CameraRepository.cs:105-112`), so the
   durable publish's early `BEGIN` on the DbContext precedes `SaveChangesAndFlushMessagesAsync`, as ADR-0185 assumes.
7. **One path the ADR does not list, recorded (no contradiction):** `RotateWebhookClientCommandHandler` publishes
   `WebhookClientRaceDisableFailed` from three `catch`/failure branches (`:463, :488, :536`) *after* a failed save,
   and every branch is followed by a `CommitAsync` (`:475`, or `:115` because those branches return `false`). Today
   that publish touches no database; durable, it opens a transaction on a context whose last save just failed. If
   that failure was a broken connection, the publish now throws inside the `catch`, after the create committed. This
   is a behaviour change on a failure path; plan R2 and T009 pin it, and security review checks it.
8. **Spec number 442.** 438-441 are taken (`develop`, local branches, open PRs #3091/#3106 are ADR-only).

## Decisions this spec makes (all from ADR-0185; none open)

- **D1** Mechanism exactly as ADR-0185 decision 1: `IReadOnlyCollection<Type>? durableOutboxMessageTypes = null` on
  `AddWolverineForContext`, applied in **one** `routing.ConfigureSending((exchange, context) => …)` inside the existing
  `UseConventionalRouting` block. Placed after `durableInboxMessageTypes`, before `configureMore` (callers use named
  arguments; no positional caller exists).
- **D2** Per-module lists beside each module's `AddWolverineForContext` call (ADR-0185 decision 2, Implementation
  Notes item 2): Identity 4, CameraCatalog 4, LayoutComposition 3, SystemVariables 2, OverlayDesigner 2,
  EventIngestion 1. `CameraAddressReadV1` is **not** added (it is #3016 slice A's, decision 7).
- **D3** Not durable, and guarded as such: `FabEventIngestedV1`, `SystemVariableValueRequestedV1`,
  `OverlayHighlightRequestedV1`, `WallSceneSwitchRequestedV1`, `SystemVariableValueChangedV1`,
  `ResolvedOverlayTextChangedV3`, `WallSceneChangedV1`, `StreamHealthChangedV1`, `AuditChunkArchivedV1`.
- **D4** Rollback is drain-first (ADR-0185 *Rollout*), documented on `OutboxBacklogHealthCheck`.

## User story 1 (P1): An admin change, or a credential issued, is announced even if the broker is down

As the platform, when a listed type is published from an operator or integration request and the request returns
success, the announcement is a committed row in that context's outgoing table and reaches its consumers once the
broker is reachable, including after a process crash between commit and send.

### Acceptance scenarios

```gherkin
Scenario: happy path, a declared type is persisted in the caller's transaction
  Given a host built with AddWolverineForContext declaring DurableProbe and not InlineProbe
  When DurableProbe is published through OutboxEventBus and CommitAsync returns, with delivery held back
  Then an outgoing row for DurableProbe exists in <schema>.wolverine_outgoing_envelopes
  And it is delivered and the row deleted once delivery resumes

Scenario: an undeclared type is unchanged (today's behaviour, pinned)
  Given the same host
  When InlineProbe is published and committed
  Then no outgoing row for InlineProbe is ever written

Scenario: broker loss (the conflict case)
  Given the send to the exchange fails
  When DurableProbe is published and committed
  Then CommitAsync returns, the row remains, and it is delivered after sending recovers
  And InlineProbe published the same way is discarded (logged), as today

Scenario: a publish with no accompanying write
  When DurableProbe is published with no EF change and CommitAsync is called
  Then a committed outgoing row exists (the CameraAddressReadV1 / AuditChunkArchivedV1 shape)

Scenario: bad configuration, retries on (the precondition)
  Given the DbContext uses a retrying execution strategy
  When DurableProbe is published
  Then the publish fails loudly (user-initiated transaction refused), not silently

Scenario: every listed type is routed durably by its own module
  Given each of the six modules' real Wolverine configuration
  Then each of its listed types resolves a sender with IsDurable = true
  And every hot-path / mixed-caller / Q3 type (D3) resolves IsDurable = false

Scenario: auth
  No endpoint, scope or token changes. Existing endpoint auth is unchanged and is not re-tested here;
  the security property under test is decision 4 (no secret not already at rest in the same database).
```

### Functional requirements

- **FR-001** `durableOutboxMessageTypes` exists on `AddWolverineForContext` and makes each listed type's RabbitMQ
  exchange sender Durable; every other type stays Inline. Local queues stay `BufferedInMemory`.
- **FR-002** The six modules pass exactly the 16 types of ADR-0185 decision 3 rows 2-17.
- **FR-003** `OutboxCoversEveryContextTests` observes a non-zero outgoing write for `CameraRegisteredV1` before the
  drain; `OutboxSurvivesAKillTests` uses `WebhookIntegrationRevokedV1` and observes the row pending across the
  restart and delivered after it. Both shown red by counterfactual.
- **FR-004** Docs corrected per ADR-0185 Implementation Notes item 7 (four places) and item 8 (drain procedure).
- **FR-005** No ADR, constitution or CLAUDE.md edit (ADR-0144; the ADR-0088 body edits and CLAUDE.md row are a
  human's, ADR-0185 *Record edits*).

## Locked tech choices

WolverineFx / .EntityFrameworkCore / .Postgresql / .RabbitMQ **6.51.0** (no bump); xUnit + Shouldly; Docker-free
host for routing facts (pattern: `WallSceneSwitchFailurePolicyTests`, `StubAllExternalTransports`); Aspire fixture
for persistence facts (ADR-0103).

## Latency budget impact (constitution §IV)

**N/A for every leg** (ADR-0185 decision 5): no listed type is published on the *event → overlay state* path
(premise 3). Verified two ways: (a) a guard test (T005) asserts every D3 type resolves a **non-durable** sender in its
own module, so adding a hot-path type to a list fails a named test; (b) the leg's existing measurement,
`e2e/kiosk-live-updates.spec.ts` (§IV's 57.6-75 ms CI bracket), runs unmodified in CI.

## Independent end-to-end test procedure

1. Docker-free: `dotnet test tests/ServiceDefaults.Tests --filter DurableOutboxRouting` → durable/inline facts.
2. Aspire: `dotnet test tests/Integration.Tests --filter DurableOutboxMechanismTests|OutboxCoversEveryContextTests`.
3. Locally only (Disruptive): `--filter OutboxSurvivesAKillTests`.
4. Manual: register a camera with RabbitMQ stopped; `201` returned; `SELECT count(*) FROM
   wolverine_camera_catalog.wolverine_outgoing_envelopes` = 1; `/health` shows `outbox-camera-catalog` pending 1;
   start RabbitMQ; the row drains and the camera's stream is provisioned.

## Out of scope

`CameraAddressReadV1` (#3016 slice A); the hot path and mixed-caller types (#3103); retention of undelivered
durable rows (#3092); the two credential paths that publish no event (follow-up issue, ADR-0185 decision 3);
ADR-0088 / CLAUDE.md record edits (human).
