# Plan 442: A declared announcement outlives the broker

**Spec:** [spec.md](spec.md) · **ADR:** ADR-0185 (Implementation Notes items 1-9) · **Issue:** #3102

## Slices

**One slice, one PR** (ADR-0185 Implementation Notes: "one slice"). Size estimate: production code ~60 lines
(`WolverineDefaults` ~25, six modules ~5 each), docs ~60 lines in five files, tests ~700 lines (four new classes, two
modified). Reviewable as one PR because production change is small and every test maps to one ADR item. Internal
order is still mechanism-first: T001/T002 (test-only probe types) must be observed before T003/T004 (module lists)
are trusted.

If the Docker-free route of T001 proves impossible in 6.51.0 (R1), the slice does **not** split: T001's facts move
into T002 (Aspire) and T003/T005 use the Aspire per-type observation instead. Record which in the PR.

## Bounded context and layers

Cross-cutting: `ServiceDefaults` (Infrastructure-level shared wiring) plus the `Infrastructure` module of six
contexts. No Domain, Application, Api, contract or AppHost change. No cross-context reference: each module lists only
`Shared.Contracts` types it already publishes (plus Identity's own `WebhookClientRaceDisableFailed`).

## Components

### C1. `src/ServiceDefaults/WolverineDefaults.cs`
- New parameter `IReadOnlyCollection<Type>? durableOutboxMessageTypes = null` after `durableInboxMessageTypes`.
- Inside `UseConventionalRouting`, after `ConfigureListeners`: one `routing.ConfigureSending((exchange, context) =>
  { if (durableOutboxMessageTypes?.Contains(context.MessageType) == true) exchange.UseDurableOutbox(); })`.
- Comment (the *why*): replaces-not-composes (mirror `:113-118`), per type because the sender identifier is the type
  name, the `DisableRetry` precondition (`PostgresTelemetry.cs:98`), local queues untouched; extend the Inline comment
  at `:146-161` with the sending side (ADR-0185 item 7).

### C2. Six module lists (ADR-0185 item 2)
Named argument `durableOutboxMessageTypes: [typeof(...), ...]` beside the existing call, with a one-line *why*
comment citing ADR-0185:
- `src/Identity/Infrastructure/IdentityInfrastructureModule.cs:61` — `WebhookIntegrationRotatedV1`,
  `DeviceRegisteredV1`, `KioskEnrolledV1`, `WebhookClientRaceDisableFailed`
- `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs:53` — `WebhookIntegrationRevokedV1`
- `src/CameraCatalog/Infrastructure/CameraCatalogInfrastructureModule.cs:75` — `CameraRegisteredV1`,
  `CameraRenamedV1`, `CameraAddressChangedV1`, `CameraRetiredV1`
- `src/SystemVariables/Infrastructure/SystemVariablesInfrastructureModule.cs:63` — `SystemVariableDefinedV1`,
  `SystemVariableArchivedV1`
- `src/OverlayDesigner/Infrastructure/OverlayDesignerInfrastructureModule.cs:48` — `OverlayRevisionPublishedV3`,
  `OverlayRevisionArchivedV1`
- `src/LayoutComposition/Infrastructure/LayoutCompositionInfrastructureModule.cs:50` — `LayoutRevisionPublishedV2`,
  `LayoutRevisionArchivedV1`, `WallConfiguredV1`

For the per-type test to see the module's real list (not a copy), each module exposes the collection as an
`internal static readonly IReadOnlyCollection<Type> DurableOutboxMessageTypes` (InternalsVisibleTo already exists
for the `*.Infrastructure.Tests` projects that use `StubAllExternalTransports`; add where missing) **and** passes that
field to the call. T003 then has two halves: the field's content drives a real Wolverine host (effect), and a
host built through the module's own registration entry point proves the field is the one passed (wiring). If the
module's full registration cannot be built Docker-free, the wiring half is the Aspire observation in T004.

### C3. Docs (ADR-0185 items 7, 8)
`OutboxTransactionalCommit.cs:21-23`, `OutboxEventBus.cs:14-19` and `:46-52`, `Shared.CQRS/IEventBus.cs:32-34`,
`WolverineDefaults.cs:146-161`; drain-before-rollback in `OutboxBacklogHealthCheck`'s XML summary.

## Entities, value objects, invariants

None new (no domain change). Invariants this slice makes true and tests:
- **I1** A listed type's exchange sender `IsDurable`; an unlisted type's is not.
- **I2** A listed type's outgoing row is committed by the time `CommitAsync` returns.
- **I3** No D3 (hot path / mixed / Q3) type is durable in any module.
- **I4** A durable body carries no secret not already at rest in the same database (ADR-0185 decision 4; review, not test).

## Messaging

No new domain or integration events and no contract change. Delivery mode of 16 existing integration events changes
from Inline to Durable at the publisher; consumers, queues and routing keys are unchanged (the exchange endpoint per
type already exists).

## Boundary rules

No new project references. Test projects reference only the module they test plus `ServiceDefaults`. NetArchTest
boundary rules unchanged.

## Phase 4a — colours (declared here, ADR-0144)

| Item | Colour | Test |
|---|---|---|
| Mechanism, routing facts (ADR item 1, 3) | **red** | `tests/ServiceDefaults.Tests/DurableOutboxRoutingTests.cs` (Docker-free) |
| Mechanism, persistence facts (ADR item 3) | **red** | `tests/Integration.Tests/ServiceDefaults/DurableOutboxMechanismTests.cs` (Aspire) |
| Per-type lists (ADR item 2, 4) | **red** | `tests/<Context>.Infrastructure.Tests/DurableOutboxListTests.cs` ×6, or one theory in `Architecture.Tests` if module composition is reachable there (`PostgresComposition` precedent) |
| Hot-path guard (decision 5) | **characterisation** (green before and after), proven capable of failing by counterfactual | same harness, inverse theory over D3 |
| `OutboxCoversEveryContextTests`, `OutboxSurvivesAKillTests` (ADR item 5) | **red** by counterfactual | modified in place |
| Other outbox tests + leg latency (ADR item 6) | **characterisation**, unmodified | `OutboxSharesTheWritesFateTests`, `OutboxBacklogIsVisibleTests`, `OutboxEventBusTests`, `OutboxBacklogHealthCheckTests`, `OutboxBacklogAssessmentCharacterisationTests`, `ExecutionStrategyCharacterisationTests`, `e2e/kiosk-live-updates.spec.ts` |

### Test design notes for the test-writer

- **Docker-free host (T001):** `Host.CreateApplicationBuilder` with in-memory connection strings (dummy Postgres and
  `amqp://localhost`), `AddWolverineForContext<ProbeDbContext>(…, durableOutboxMessageTypes: [typeof(DurableProbe)],
  configureMore: opts => opts.StubAllExternalTransports())`, plus `services.DisableAllWolverineMessagePersistence()`.
  Decompiled 6.51.0: with stubbed transports `InitializeAsync` is skipped (`WolverineRuntime.cs:2197`),
  `Endpoint.StartSending` uses a `NullSender` (`Endpoint.cs:940`) but `EndpointCollection.buildSendingAgent`
  still switches on `endpoint.Mode` (`:522`), so `runtime.RoutingFor(typeof(T))` → route sender `.IsDurable` /
  `Endpoint.Mode` is observable without a broker. **Unobserved (R1):** whether `PersistMessagesWithPostgresql` still
  touches Postgres at `StartAsync` once persistence is disabled; whether RabbitMQ's conventional `DiscoverSenders`
  runs while stubbed. Observe both first; if either fails, fall back per §Slices.
  Facts: DurableProbe `IsDurable == true` and `Mode == Durable`; InlineProbe `Inline`; a third type with a
  same-context handler keeps its local route `BufferedInMemory`; a second `ConfigureSending` in `configureMore`
  **replaces** the first (the trap, observed, so the comment is evidence-backed).
- **Aspire host (T002):** built in the test process against the fixture's Postgres/RabbitMQ with a throwaway schema.
  *Row-before-return without a race:* the row is deleted on ack within milliseconds, so do not poll the table;
  install a test-only `AFTER INSERT` trigger on the probe schema's `wolverine_outgoing_envelopes` that copies
  `message_type` into a side table, or hold delivery back. *Send failure:* must not be an Aspire resource restart
  (excluded from CI); candidates: delete the probe exchange through the RabbitMQ management API after the sender
  starts, or point the probe host at an unreachable RabbitMQ port. *Retry precondition:* a probe DbContext
  configured with `EnableRetryOnFailure` makes the durable publish throw `InvalidOperationException` (T006).
- **Test-only types** live in the test project (`DurableProbeV1`, `InlineProbeV1`); never in `Shared.Contracts`.
- New Integration test classes need a `tests/Integration.Tests/ci-shards` entry (deterministic failure otherwise).

### Counterfactuals (each run, failure quoted in the PR)

1. Remove `DurableProbe` from the probe list → T001 and T002 red.
2. Delete the `ConfigureSending` call → T001, T002, T003 red.
3. Drop one type from one module list (e.g. `CameraRetiredV1`) → its T003 case red.
4. Add `FabEventIngestedV1` to EventIngestion's list → T005 red (the latency guard can fail).
5. Remove `CameraRegisteredV1` from CameraCatalog's list → `OutboxCoversEveryContextTests` red.
6. Remove `WebhookIntegrationRevokedV1` from EventIngestion's list → `OutboxSurvivesAKillTests` red (local, Disruptive).
7. `DisableRetry = false` in `PostgresTelemetry.cs:98` → `ExecutionStrategyCharacterisationTests` and T006 red.

## Measurement

§IV: N/A by decision 5; evidence is T005 green plus `e2e/kiosk-live-updates.spec.ts` green in CI on the PR's tip
(download the TRX/log before calling the run green). No latency figure is claimed or changed.

## Risks

- **R1** Docker-free host may still reach Postgres/RabbitMQ at start in 6.51.0 → fall back to Aspire (§Slices).
- **R2** `WebhookClientRaceDisableFailed` from a failure branch (`RotateWebhookClientCommandHandler.cs:463,488,536`)
  now opens a transaction on a context whose save just failed; a dead connection makes the publish throw inside the
  `catch`. Not ruled by the ADR. The engineer must not change the handler; T009 characterises today's and the new
  outcome for the `DbUpdateConcurrencyException`/`DbUpdateException` branches (connection healthy) so the change is
  observed, and the dead-connection case is listed for security review. If T009 shows a new failure on a healthy
  connection, **block** (needs an owner decision, not a lane fix).
- **R3** A durable publish with no `CommitAsync` after it rolls back its row silently (ADR-0185 Negative). All 16
  publish sites are followed by a commit (premise 6/7); the security reviewer re-checks.
- **R4** Rollback with rows pending is unverified → drain-first procedure (D4).
- **R5** CI's Disruptive filter excludes `OutboxSurvivesAKillTests`; its evidence is local only and must say so.
