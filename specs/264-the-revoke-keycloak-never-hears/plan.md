# Plan 264: The revoke Keycloak never hears

**Spec**: [spec.md](spec.md) · **Issue**: #2206 (defect 2 only) · **Phase**: 2 (Plan)

## Constitution / ADR check

| Rule | How this plan satisfies it |
|---|---|
| §III: no cross-context references | EventIngestion publishes and Identity subscribes. They share only `SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRevokedV1`. No project reference is added (`BoundaryTests` stays green). |
| ADR-0040 / ADR-0073 | The domain event (already raised) is translated to a `V1` integration event by a domain-event handler. The existing domain event is extended, and no second one is added. |
| ADR-0088 | No hand-written routing. Conventional routing gives Identity the queue `identity.SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRevokedV1`, and the audit context gets its own. The publish rides EventIngestion's Postgres outbox, committed with the `RevokedAt` write. |
| ADR-0102 | Fab travels in `Metadata.Fab`, as it does for `WebhookIntegrationRotatedV1`. |
| ADR-0042 / 0057 | The subscriber translates to `DisableWebhookClientCommand`, which runs through a hand-rolled `ICommandHandler`, as `CameraRetiredIntegrationEventHandler` does. |
| ADR-0047 / 0105 / 0141 | `Result<…, DisableWebhookClientError>`, `Ensure.That(...)` guards and `Option<RegisteredClient>` lookups, all copied from `DisableKioskCommandHandler`. |
| §II (primitives) | Domain: the domain event gains a `FabIdentifier`, not a string. The contract carries primitives, as `Shared.Contracts` is exempt. |
| House rule: handlers destructure | The V1 subscriber reads two or more fields, so it opens with `var (integrationName, _, metadata) = message;`. The domain-event handler does the same. `HandlerDeconstructionTests` guards names. |
| ADR-0036 | No refactor of the rotate handler, and no new abstraction. §7 lists what is deliberately not changed. |

## 1. Bounded contexts and layers touched

| Context | Layer | Change |
|---|---|---|
| Shared.Contracts | — | **New** `EventIngestion/WebhookIntegrationRevokedV1.cs` |
| EventIngestion | Domain | `WebhookIntegration/Events/WebhookIntegrationRevokedDomainEvent.cs` gains `FabIdentifier Fab`. `WebhookIntegration.Revoke` (`WebhookIntegration.cs:103`) passes `Fab`. |
| EventIngestion | Application | **New** `EventHandlers/WebhookIntegrationRevokedDomainEventHandler.cs`. `Log.cs` gains one `[LoggerMessage]`. |
| EventIngestion | Infrastructure | `EventIngestionInfrastructureModule.cs`: one `AddScoped<IDomainEventHandler<…>, …>` beside `:70-72`. |
| Identity | Application | **New** `Commands/DisableWebhookClientCommand.cs` (command + error union + failures), **new** `Commands/Handlers/DisableWebhookClientCommandHandler.cs`, **new** `EventHandlers/WebhookIntegrationRevokedIntegrationEventHandler.cs`. `Log.cs` gains its messages. |
| Identity | Infrastructure | `IdentityInfrastructureModule.cs`: two registrations beside the DisableDevice pair (`:78-81`). **No Wolverine change** (§4). |
| AuditObservability | Application | `EventHandlers/IntegrationEventAuditHandler.cs`: one `Handle` line. `EventHandlers/V1ResourceMap.Conventions.cs`: one `Add<…>` hand tweak. |
| — | Unchanged on purpose | `RevokeWebhookIntegrationCommandHandler`, `WebhookIntegrationsEndpoints`, `EventsEndpoints*`, `RotateWebhookClientCommandHandler`, `RegisteredClient`, `IKeycloakAdminClient`, `HttpKeycloakAdminClient`, `KeycloakScopeBundles`, `WolverineDefaults`, AppHost. |

**Why `RevokeWebhookIntegrationCommandHandler` does not change.** The aggregate
already raises the domain event (`WebhookIntegration.cs:103`), and
`WebhookIntegrationRepository.SaveAsync` (`:64-71`) already dispatches it *before*
`commit.CommitAsync`. Registering a handler is the whole of the publish side.
`RotateWebhookClientCommandHandler` publishes directly only because its client id
exists only after Keycloak answers (`:156-169`). Revoke has everything it needs
in the aggregate, so the domain-event route used by the other ten handlers applies.
The repository comment at `:60-63` states that *every handler on this path publishes
and does nothing else*. The new handler keeps that true.

## 2. The contract

`src/Shared.Contracts/EventIngestion/WebhookIntegrationRevokedV1.cs`:

```csharp
namespace SmartSentinelEye.Shared.Contracts.EventIngestion;

public sealed record WebhookIntegrationRevokedV1(
    string IntegrationName,
    DateTimeOffset RevokedAt,
    EventMetadata Metadata) : IIntegrationEvent;
```

- **Namespace by publisher.** EventIngestion publishes it, as it does
  `FabEventIngestedV1`. The mirror, `WebhookIntegrationRotatedV1`, sits under
  `Identity` because Identity publishes that one.
- **No `ClientId` field.** EventIngestion knows the Keycloak client id only if the
  rotation message reached it (`WebhookIntegration.KeycloakClientId`, nullable). A
  lost or late `WebhookIntegrationRotatedV1` would then silently skip the disable.
  Identity owns the `"webhook-{name}"` rule (`RotateWebhookClientCommandHandler.cs:46`)
  and derives it again from the name.
- **Published for every first revoke**, rotated or not. It costs one message and a
  not-found log when there is no client. It is also the only behaviour that is correct
  when EventIngestion's view of "rotated" lags Identity's.
- `Metadata = new EventMetadata(Guid.CreateVersion7(), revokedAt, fab.Value, null)`.
  `Actor` is `null`, as in `ClientRegisteredDomainEventHandler.cs:49,63` (spec §3).
  Also add an XML summary in the house style of `WebhookIntegrationRotatedV1.cs`,
  naming the publisher, the subscriber and the reason (#2206).

## 3. Entities, value objects, invariants

**No new aggregate, value object or state.**

- `WebhookIntegrationRevokedDomainEvent(WebhookIntegrationName Name, FabIdentifier Fab, DateTimeOffset RevokedAt)`.
  The field is added **between** `Name` and `RevokedAt`, so the positional order
  reads identity first and time last. Only `WebhookIntegration.Revoke` constructs it,
  and no test constructs it (`tests/EventIngestion.Domain.Tests/WebhookIntegration/WebhookIntegrationTests.cs:67`
  only filters with `OfType<>`). Invariant unchanged: raised once, on the first
  transition to revoked, and never on a repeat (`WebhookIntegration.cs:97-100`).
- `RegisteredClient.Disable(IClock)` (`RegisteredClient.cs:103-113`) is reused as is.
  It is idempotent: a second call neither moves `DisabledAt` nor raises
  `ClientDisabledDomainEvent`. Nothing handles that domain event today, and this plan
  adds no handler for it.
- `ClientKind.WebhookIntegration` (`ClientKind.cs:18`) is the Kind gate.

## 4. Messaging and wiring

```
DELETE /webhook-integrations/{name}                       (EventIngestion.Api, unchanged)
 └─ RevokeWebhookIntegrationCommandHandler                 (unchanged)
     └─ integration.Revoke(clock)  → raises WebhookIntegrationRevokedDomainEvent(Name, Fab, RevokedAt)
     └─ integrations.SaveAsync
         ├─ DomainEventDispatcher → WebhookIntegrationRevokedDomainEventHandler   [NEW]
         │     └─ IEventBus.PublishAsync(WebhookIntegrationRevokedV1)  → captured in outbox
         └─ ITransactionalCommit.CommitAsync  → row + outbox message, one transaction, then released

RabbitMQ (conventional routing, ADR-0088)
 ├─ identity.SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRevokedV1
 │    └─ WebhookIntegrationRevokedIntegrationEventHandler.Handle     [NEW, Identity.Application]
 │         └─ DisableWebhookClientCommandHandler.HandleAsync         [NEW]
 │              ├─ clients.GetWithinFabAsync(fab, clientId)  + Kind == WebhookIntegration
 │              ├─ keycloak.DisableClientAsync(clientId)
 │              └─ client.Disable(clock); clients.SaveAsync
 └─ audit-observability.SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRevokedV1
      └─ IntegrationEventAuditHandler.Handle   [NEW line]
```

**Wiring point for the subscription: none to write.** This is the answer to "where is
the subscription registered". It is the existing call
`builder.AddWolverineForContext<IdentityDbContext>(moduleQueuePrefix: ContextName, …)`
at `src/Identity/Infrastructure/IdentityInfrastructureModule.cs:109-112`
(`ContextName = "identity"`, `:30`), inside the Identity infrastructure DI extension.
`WolverineDefaults.AddWolverineForContext` (`src/ServiceDefaults/WolverineDefaults.cs`)
loads `SmartSentinelEye.Identity.Application` (`:66`, `TryLoadApplicationAssembly`),
includes it in discovery (`:133-136`) and names each listener queue
`{prefix}.{eventType.FullName}` (`:93-96`). A public class in Identity.Application with
a `public async Task Handle(WebhookIntegrationRevokedV1, CancellationToken)` is
therefore discovered, and its queue is provisioned and bound on boot. This is exactly
how `WebhookIntegrationRotatedV1Handler` became EventIngestion's subscriber. **Adding a
manual `ListenToRabbitQueue` / `PublishMessage<>` would bypass the convention. It is a
review blocker, not a belt-and-braces measure.**

**DI registrations (what *is* written):**

- `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs`, after `:72`:
  `builder.Services.AddScoped<IDomainEventHandler<WebhookIntegrationRevokedDomainEvent>, WebhookIntegrationRevokedDomainEventHandler>();`
  Without it the dispatcher resolves an empty set, which is today's defect exactly.
- `src/Identity/Infrastructure/IdentityInfrastructureModule.cs`, after `:81`:
  `AddScoped<DisableWebhookClientCommandHandler>()` and
  `AddScoped<ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>, DisableWebhookClientCommandHandler>()`.
  This is the DisableDevice pair's shape. The Wolverine subscriber takes the
  **interface** (as `CameraRetiredIntegrationEventHandler` does), so it can be unit
  tested with a fake. The Wolverine handler class itself is **not** registered in DI.
  Wolverine builds it.

**Transactions inside the Identity subscriber.** `AutoApplyTransactions` (ADR-0088)
wraps the handler. `RegisteredClientRepository.SaveAsync` commits through
`ITransactionalCommit`, as `RetireStreamCommand` does under
`CameraRetiredIntegrationEventHandler`. No change and no new pattern.

## 5. Handler specifications

### 5.1 `WebhookIntegrationRevokedDomainEventHandler` (EventIngestion.Application)

Shape: `EventIngestedDomainEventHandler`, minus the journey (a revoke runs inside an
HTTP request, so it already has an ambient trace).

```
(IEventBus events, ILogger<…> logger) : IDomainEventHandler<WebhookIntegrationRevokedDomainEvent>
Handle(domainEvent, ct):
  Ensure.That(domainEvent).IsNotNull();
  var (name, fab, revokedAt) = domainEvent;
  await events.PublishAsync(new WebhookIntegrationRevokedV1(
      name.Value, revokedAt,
      new EventMetadata(Guid.CreateVersion7(), revokedAt, fab.Value, null)), ct);
  logger.PublishedWebhookIntegrationRevokedV1(name, fab);   // Information
```

Nothing else. It makes no Keycloak or repository call, per the repository's
publish-only invariant.

### 5.2 `DisableWebhookClientCommand` + handler (Identity.Application)

**A line-for-line mirror of `DisableKioskCommand.cs` / `DisableKioskCommandHandler.cs`.**
Only the names, the Kind and the codes differ:

```
public sealed record DisableWebhookClientCommand(ClientId ClientId, FabIdentifier Fab)
    : ICommand<Result<RegisteredClientIdentifier, DisableWebhookClientError>>;

DisableWebhookClientError(Code, Message, Status) : ApiError
  WebhookClientNotFound(string ClientId)  "WEBHOOK_CLIENT_NOT_FOUND"  404
  KeycloakUnavailable(string Reason)      "KEYCLOAK_UNAVAILABLE"      502
DisableWebhookClientFailures { WebhookClientNotFound(..), KeycloakUnavailable(..) }
```

`RotateWebhookClientError.WebhookClientNotFound` already uses the code
`WEBHOOK_CLIENT_NOT_FOUND`, at **412**, meaning "no client at the version you sent"
(`RotateWebhookClientCommand.cs`). This command's error never reaches HTTP, because
only the subscriber consumes it. Reusing the same string with a 404 would still put
two meanings on one code. **Use `WEBHOOK_CLIENT_NOT_FOUND` / 404 anyway, and note the
difference in the record's doc comment.** A distinct code would be a new vocabulary
entry for a value nobody outside the process sees. The error unions stay per-command,
as they are for kiosk and device.

The handler body is `DisableKioskCommandHandler.cs:20-45` with `ClientKind.Kiosk` →
`ClientKind.WebhookIntegration` and `logger.DisabledKiosk` → `logger.DisabledWebhookClient`.
The order is load-bearing, as it is there: **Keycloak first, then the row.** A Keycloak
failure therefore leaves the row un-disabled, so a retry repeats the whole disable
rather than finding a row that lies.

### 5.3 `WebhookIntegrationRevokedIntegrationEventHandler` (Identity.Application)

Shape: `CameraRetiredIntegrationEventHandler`. Its parse-and-refuse half is
`WebhookIntegrationRotatedV1Handler.ParseFab`.

```
(ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> handler,
 ILogger<…> logger)
public async Task Handle(WebhookIntegrationRevokedV1 message, CancellationToken cancellationToken = default)
  Ensure.That(message).IsNotNull();
  var (integrationName, _, metadata) = message;

  Option<FabIdentifier> fab = ParseFab(metadata, integrationName)    // null/blank/invalid → log Warning, return
  Option<ClientId> clientId = ParseClientId(integrationName)          // ClientId.From($"webhook-{integrationName}");
                                                                       // ArgumentException → log Warning, return
  result = await handler.HandleAsync(new(clientId, fab), ct)
  success                          → return (the command logged DisabledWebhookClient)
  WebhookClientNotFound            → logger.NoWebhookClientToDisable(integrationName, fab)  // Information; return
  KeycloakUnavailable (any other)  → logger.WebhookClientDisableFailed(...); throw InvalidOperationException(...)
                                      // Wolverine's retry signal, as CameraRetiredIntegrationEventHandler.cs:45-52
```

- **Malformed means drop, and a retryable failure means throw.** A message that can
  never succeed must not be retried forever. A Keycloak outage must not be swallowed,
  because swallowing it would leave the client enabled with nothing left to retry. This
  split is the whole of the handler's judgement.
- **The `"webhook-"` prefix is duplicated** from `RotateWebhookClientCommandHandler.cs:46`,
  and the duplication is deliberate (spec §3). The integration test in §6.1 rotates
  through the real endpoint, so a divergence fails it.
- `metadata?.Fab` is read with `?.` for the reason `WebhookIntegrationRotatedV1Handler`
  gives (`:193-200`): a deserialised message without `Metadata` is a real `null`.
- `FabIdentifier` and `ClientId` here are Identity's own
  (`SmartSentinelEye.Identity.Domain.RegisteredClient`).

## 6. Tests (phase 4a writes them; 4b may not edit them)

### 6.1 Integration: the load-bearing red

`tests/Integration.Tests/Identity/WebhookRevocationDisablesClientIntegrationTests.cs`,
`[Collection(AspireCollection.Name)]`, with no `[Trait("Category", …)]` (the CI filter
is a deny-list). Modelled on `CrossFabDisableIntegrationTests` (its
`IsEnabledInKeycloakAsync` reads Keycloak's own Admin API via `RealmProbe`, **not** the
`registered_clients` row, for the reason its header gives) and on
`CrossFabWebhookRotationEffectIntegrationTests` (register on event-ingestion, rotate
on identity, a bounded 20 s / 500 ms poll).

- **Fact 1: `A_rotated_webhook_integrations_client_is_disabled_once_it_is_revoked`.**
  Steps: register in munich, rotate (`If-None-Match: *`, body `fabId: munich`), then two
  **controls**: `enabled == true`, and a `client_credentials` grant with the rotated
  secret returns an `access_token`. Then revoke (`If-Match` from
  `GET /webhook-integrations`, asserting `200`). Then poll until `enabled == false` or
  20 s. Assert `false`. Assert the same grant is now refused (non-2xx, no
  `access_token`). **Red on develop:** stays `true`.
- **Fact 2: `Revoking_a_never_rotated_integration_still_succeeds`.** Register, then
  revoke, assert `200`, and assert Keycloak has no `webhook-<name>` client (the query
  returns an empty array). A **green-on-develop control**, declared as such. It pins
  that the new message does not make the endpoint fail when there is no client.
- The token request uses `aspire.CreateKeycloakClient()` and the form shape in
  `Fixtures/PlantFloor.cs:83-96`.
- **Add the class to a shard filter.** CI runs only the classes listed in
  `tests/Integration.Tests/ci-shards/shard-N.filter`, and `7aab60de` exists because a
  new class was left out. Add it to `shard-3.filter`, beside
  `Identity.CrossFabDisableIntegrationTests`.

### 6.2 Unit

| File (new unless noted) | Facts |
|---|---|
| `tests/EventIngestion.Domain.Tests/WebhookIntegration/WebhookIntegrationTests.cs` (**edit: add one fact, change none**) | `Revoke_raises_a_domain_event_carrying_the_integrations_fab`: the single `WebhookIntegrationRevokedDomainEvent`'s `Fab` equals the registered fab, and its `Name` and `RevokedAt` match. Red: compile. |
| `tests/EventIngestion.Application.Tests/EventHandlers/WebhookIntegrationRevokedDomainEventHandlerTests.cs` | publishes exactly one `WebhookIntegrationRevokedV1` with name, `RevokedAt`, `Metadata.Fab`, `Metadata.OccurredAt == RevokedAt`, `Metadata.Actor == null`. Uses `Fakes/FakeEventBus.cs`. |
| `tests/EventIngestion.Application.Tests/Commands/WebhookIntegrationCommandHandlerTests.cs` | **Not edited.** `InMemoryWebhookIntegrationRepository.SaveAsync` (`:73-90`) clears pending events without dispatching them, so "the command publishes" cannot be observed here without changing a fake that existing facts depend on. The claim decomposes. *Raised once, never on a repeat* is the domain facts (`Revoke_flips_the_state_and_raises_a_domain_event`, `Revoke_is_idempotent_on_an_already_revoked_integration`). *Translated to one V1* is the handler test above. *Stale / not-found / other-fab never reach `Revoke`* is structural (`RevokeWebhookIntegrationCommandHandler.cs:23-62` returns first). *Dispatch is wired* is §6.1 Fact 1. Existing facts must pass unmodified (SC5). |
| `tests/Identity.Application.Tests/Commands/DisableWebhookClientCommandHandlerTests.cs` | Mirror `DisableKioskCommandHandlerTests` fact for fact: disables in Keycloak and marks the row; another fab gives NotFound and no Keycloak call; Kind ≠ WebhookIntegration gives NotFound and no Keycloak call; unknown gives NotFound; Keycloak throws gives `KeycloakUnavailable` with the row **not** disabled; `OperationCanceledException` propagates; a repeat is idempotent (`DisabledAt` unchanged). |
| `tests/Identity.Application.Tests/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandlerTests.cs` | sends `DisableWebhookClientCommand(ClientId "webhook-<name>", Fab from metadata)`; NotFound returns with no throw; KeycloakUnavailable throws; null / blank / invalid fab makes no command call and no throw; a name that makes an invalid `ClientId` makes no command call and no throw. Uses a recording fake `ICommandHandler`. |
| `tests/Shared.Contracts.Tests/EventIngestion/WebhookIntegrationRevokedV1Tests.cs` | Mirror `Identity/WebhookIntegrationRotatedV1Tests.cs` (round-trip / shape facts, whatever that file asserts). |

### 6.3 Existing guards that will go red and must be turned green by 4b (not 4a's to write)

- `tests/Architecture.Tests/BoundaryTests.cs:225` `V1ResourceMap_covers_every_IIntegrationEvent`
  (the convention would map the `EventIngestion` namespace to `Event`, which is wrong
  for this contract, so a hand tweak is required).
- `BoundaryTests.cs` `Every_integration_event_has_an_audit_handler` (`:254-278`).

4a **records** these two as expected reds in its report the moment the contract
exists, and the report says so. They are guards turning red on a real omission, not
new-behaviour tests.

## 7. Deliberately not changed (review checklist)

- `RevokeWebhookIntegrationCommandHandler`: no `IEventBus` injection. It publishes
  through the domain event.
- `RotateWebhookClientCommandHandler`: the `"webhook-"` derivation is not extracted
  (spec §3).
- `EventsEndpoints*`: the `/events/manual` revocation check is #2241's.
- `KeycloakScopeBundles` / defect 1: already fixed.
- `WolverineDefaults`, AppHost, RabbitMQ topology: convention covers it.
- No migration. No column changes: `WebhookIntegrationRevokedDomainEvent` is not
  persisted, and `registered_clients.disabled_at` already exists.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Identity's first subscription fails to bind, so the queue is never created and the message is lost silently | §6.1 Fact 1 goes through the real bus, so it fails if the listener does not exist. Phase 5 inspects the queue in RabbitMQ (spec §7 step 9). |
| A Keycloak outage causes endless redelivery | Wolverine's default retry/dead-letter policy is the same one `CameraRetiredIntegrationEventHandler` relies on. After it gives up, the message is on the error queue, which is visible and replayable. It is not lost. |
| Audit row attributed to *System* | Accepted with a precedent (spec §3). |
| Token minted before the disable keeps working until it expires | Bounded by the realm's access-token lifespan. Request-time closure is #2241. The spec states this in US1. |
