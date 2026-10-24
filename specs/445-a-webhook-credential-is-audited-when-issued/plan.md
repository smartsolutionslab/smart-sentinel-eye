# Plan 445: A webhook credential is audited when it is issued

[spec](spec.md) · [tasks](tasks.md) · Issue #3110

## Slice

One slice, two bounded contexts plus `Shared.Contracts`. Engineer: **backend-engineer** only.

| Context | Layer | File | Change |
|---|---|---|---|
| Shared.Contracts | — | `EventIngestion/WebhookIntegrationRegisteredV1.cs` (new) | the record |
| EventIngestion | Domain | `WebhookIntegration/Events/WebhookIntegrationRegisteredDomainEvent.cs` | widened |
| EventIngestion | Domain | `WebhookIntegration/WebhookIntegration.cs` | `Register(name, fab, defaultKind, registeredBy, clock)` raises the widened event; summary updated |
| EventIngestion | Application | `Commands/RegisterWebhookIntegrationCommand.cs` | `+ OperatorIdentifier RegisteredBy` |
| EventIngestion | Application | `Commands/Handlers/RegisterWebhookIntegrationCommandHandler.cs` | destructures and passes `registeredBy` |
| EventIngestion | Application | `EventHandlers/WebhookIntegrationRegisteredDomainEventHandler.cs` (new) | publish only |
| EventIngestion | Application | `Log.cs` | `PublishedWebhookIntegrationRegisteredV1` |
| EventIngestion | Infrastructure | `EventIngestionInfrastructureModule.cs` | register the handler; add the type to `durableOutboxMessageTypes` |
| EventIngestion | Api | `WebhookIntegrationsEndpoints.cs` | `user.ToOperatorIdentifier()` into the command |
| EventIngestion | Domain | `RegisteredEventType/Events/EventTypeRegisteredDomainEvent.cs` | comment only: it no longer has a subscriber-less sibling |
| AuditObservability | Application | `EventHandlers/IntegrationEventAuditHandler.cs` | `Handle(WebhookIntegrationRegisteredV1)` arm |
| AuditObservability | Application | `EventHandlers/V1ResourceMap.Conventions.cs` | `Add<WebhookIntegrationRegisteredV1>(map, WebhookIntegration, r => r.IntegrationName)` + comment |
| AuditObservability | Domain | `AuditEvent/ResourceKind.cs` | comment only: the Registered type now exists and maps to `WebhookIntegration`; `Webhook` stays unproducible |

## Entities, value objects, invariants

```csharp
public sealed record WebhookIntegrationRegisteredDomainEvent(
    WebhookIntegrationIdentifier Identifier,
    WebhookIntegrationName Name,
    FabIdentifier Fab,
    Kind DefaultKind,
    DateTimeOffset RegisteredAt,
    OperatorIdentifier RegisteredBy) : IDomainEvent;
```

`DateTimeOffset` mirrors the Revoked and EventTypeRegistered events; `PrimitiveBoundaryTests` is the judge.
`Register` guards `registeredBy` with `Ensure.That(...).IsNotNull()` and raises the event with `integration.Id`, the
same `now` as `RegisteredAt`, and the arguments.

**Invariants.** (1) The domain event has no field typed `BearerTokenHash` or `string` that could carry the token.
(2) One `Register` raises exactly one event. (3) The aggregate's persisted shape is unchanged (no migration).

## Messaging

```csharp
public sealed record WebhookIntegrationRegisteredV1(
    Guid Integration,
    string IntegrationName,
    string DefaultKind,
    DateTimeOffset RegisteredAt,
    EventMetadata Metadata) : IIntegrationEvent;
```

`Integration` follows ADR-0094 noun naming as `CameraRegisteredV1.Camera` does; `IntegrationName` matches the Rotated
and Revoked types, which the resource map pivots on. No `RegisteredBy` field: the owner placed the actor in
`Metadata`, which is what `IntegrationEventAuditHandler` writes to `actor_identifier` (ADR-0102).

Handler (mirror `WebhookIntegrationRevokedDomainEventHandler`):

```csharp
var (identifier, name, fab, defaultKind, registeredAt, registeredBy) = domainEvent;
await events.PublishAsync(
    new WebhookIntegrationRegisteredV1(
        identifier.Value, name.Value, defaultKind.Value, registeredAt,
        new EventMetadata(Guid.CreateVersion7(), registeredAt, fab.Value, registeredBy.Value)),
    cancellationToken);
logger.PublishedWebhookIntegrationRegisteredV1(name, fab);
```

Publish only: no repository, no Keycloak, no I/O but the bus (the invariant `SaveAsync`'s comment relies on).
**Ordering:** `SaveAsync` dispatches → the handler's publish writes the durable envelope in the open transaction →
`CommitAsync` commits row + envelope → handler returns the token → endpoint writes 201. A throw anywhere before the
commit leaves neither and returns no token.

**Durability (ADR-0185).** EventIngestion's list becomes
`[typeof(WebhookIntegrationRevokedV1), typeof(WebhookIntegrationRegisteredV1)]`. Decision 4 check: the body carries
an identifier, a name, a kind, a timestamp, a fab and an operator subject — no secret; the token hash is already at
rest in the same database, and is not in the body either.

**Consumers.** AuditObservability only (conventional routing binds the new `Handle`). No Identity consumer.

## Boundary rules

No new project references. EventIngestion → AuditObservability only through the `Shared.Contracts` type. The new
handler lives in `EventIngestion.Application`, references `Shared.Contracts` as the Revoked handler does.

## Phase 4a — colour: RED (declared, ADR-0144)

**Scaffold in the red commit, so every commit builds (CLAUDE.md; precedent spec 438's red commit, `6b4248c8`).**
The test-writer may add, with **no behaviour**: the `WebhookIntegrationRegisteredV1` record; the widened domain-event
record and `Register`/command/endpoint signatures with the event raised from the new arguments (pure plumbing; its red
is observed as a compile failure, quoted verbatim, before the scaffold is added); an **empty**
`WebhookIntegrationRegisteredDomainEventHandler` (guard + `return`) and its `Log` stub — **not registered** and **not**
on the durable list; and the mechanical call-site updates of `Register(...)`/`new RegisterWebhookIntegrationCommand(...)`
in the 9 test files that use them. Nothing in AuditObservability.

**Red tests the test-writer must write and observe failing (quote each):**

| # | Project / file | Test | Red on the scaffold because |
|---|---|---|---|
| R1 | `EventIngestion.Domain.Tests/WebhookIntegration/WebhookIntegrationTests.cs` (extend `Register_raises_a_WebhookIntegrationRegisteredDomainEvent`) | event carries `Identifier == integration.Id`, `Fab`, `RegisteredBy`; its property set contains no `BearerTokenHash`/token-valued member | compile-red before scaffold (quoted) |
| R2 | `EventIngestion.Application.Tests/EventHandlers/WebhookIntegrationRegisteredDomainEventHandlerTests.cs` (new, mirror the Revoked tests) | publishes exactly one V1 with every field, `Metadata.Fab`, `Metadata.Actor == registeredBy`, a v7 `EventIdentifier`; serialised V1 does not contain a supplied token string | stub publishes nothing |
| R3 | `EventIngestion.Application.Tests/Commands/WebhookIntegrationCommandHandlerTests.cs` | register passes `RegisteredBy` into the raised event; a taken name raises none | compile-red before scaffold |
| R4 | `Architecture.Tests/Messaging/DurableOutboxListTests.cs` | `{ "event-ingestion", typeof(WebhookIntegrationRegisteredV1) }` in `DurableTypes` | not on the list |
| R5 | `Architecture.Tests/Persistence/EventIngestionRegistrationCharacterisationTests.cs` | expected line `Scoped IDomainEventHandler<WebhookIntegrationRegisteredDomainEvent> -> WebhookIntegrationRegisteredDomainEventHandler` | stub not registered |
| R6 | `AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` | mapping case 9b → `ResourceKind.WebhookIntegration` / `IntegrationName` | unmapped |
| R7 | `Architecture.Tests/AuditPayloadRedactionCoverageTests.cs` `VerbatimInventory` | `WebhookIntegrationRegisteredV1.DefaultKind`, `.IntegrationName` | — (classification decision; the free red F3 goes green by it — name it in the PR) |
| R8 | `Shared.Contracts.Tests/EventIngestion/WebhookIntegrationRegisteredV1Tests.cs` (new, mirror Revoked) | positional/equality/JSON round trip **plus a shape pin**: exactly the five properties, none named or typed like a token/hash | shape tests arrive green (wire-shape characterisation — say so in the PR); the shape pin is the guard for FR-002 |
| R9 | `Integration.Tests/EventIngestion/WebhookRegistrationAuditIntegrationTests.cs` (new; **add to a shard filter**) | spec's end-to-end procedure: one row per 201 (actor, fab, payload, no token, no hash); 409 leaves the count at one; 403 and 400 leave zero, confirmed after a positive control's row appears | no row is ever written. **CI-only** — the red is the CI log of the red commit's run, quoted |

**Free reds (no test written; observed once the contract record exists, before the audit arm/map land):**
F1 `BoundaryTests.V1ResourceMap_covers_every_IIntegrationEvent`, F2 `BoundaryTests.Every_integration_event_has_an_audit_handler`,
F3 `AuditPayloadRedactionCoverageTests.Every_string_field_of_every_integration_event_is_classified_for_audit`
(green once R7 lands). Quote F1 and F2 red in the PR.

**Characterisation (green before and after, unmodified):** the rest of `DurableOutboxListTests` (incl. the Inline
theory), `WebhookIntegrationRevokedDomainEventHandlerTests`, `WebhookRegistryFabScopingIntegrationTests`,
`EventIngestionRouteTableCharacterisationTests`, `OutboxSharesTheWritesFateTests`.

### Counterfactuals (phase 4b, quoted in the PR)

1. Drop the type from the durable list → R4 red. 2. Pass `null` as `Metadata.Actor` → R2 and R9 red.
3. Add a `string Token` property to the V1 → R8 shape pin and F3 red. 4. Unregister the handler → R5 and R9 red.
5. Map to `ResourceKind.Webhook` → R6 red. Revert each.

## Risks

- **R-1** The integration test can only assert *absence* after a positive control lands; without it a slow consumer
  passes a 403 trivially. Required, not optional.
- **R-2** Concurrent same-name creates: the loser fails at commit, so its envelope rolls back with its row. Status
  unchanged from today; not a required test (test-adversary may add one).
- **R-3** Spec-number collision with worktree `sse-3064` (spec §Premise 6).
