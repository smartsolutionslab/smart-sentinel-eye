# AuditObservability domain model

**Source:** `src/AuditObservability/Domain/` (and the Application layer only where it shows publishing/consuming of integration events).

## Purpose

AuditObservability owns the audit trail: one immutable, normalised row per integration event delivered to it, searchable by resource, fab and time. It does not own the events it records (the publishing contexts do), and it does not interpret their payloads: a payload is stored as text.

## Aggregates

None. `AuditEvent` is explicitly not an aggregate root: "once written it is immutable" (`src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:10`). It is an entity with a Guid v7 identifier, `AuditEventIdentifier` (`src/AuditObservability/Domain/AuditEvent/AuditEventIdentifier.cs:16`), and it raises no domain events.

### `AuditEvent` (immutable entity)

- **Identifier:** `AuditEventIdentifier`.
- **State:** every property has a `private init` setter, and the only constructor is private; the only way to build a row is `AuditEvent.From` (`src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:128`, `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:146`).
- **Lifecycle:** none. A row is created and never changes.
- **Stored fields:** `OccurredAt`, `ReceivedAt`, `Fab`, `FabAttribution`, `EventKind`, `ResourceKind`, `ResourceIdentifier`, `Actor`, `ActorUsername`, `EventIdentifier`, `Payload`, `SchemaVersion`, `HandlerEnteredAt`, `WrittenAt` (`src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:31`).

**Invariants**

- `From` requires an envelope, a mapping and a clock; each is null-guarded. `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:149`
- `ReceivedAt` is taken from the injected clock, not from the envelope. `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:157`
- A row is always written at `SchemaVersion.Current`. `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:168`
- `FabAttribution` is derived once, in `From`: `Resolved` if the envelope carries a fab; otherwise `NotApplicable` if the envelope's `FabScope` is `Neutral`; otherwise `Unresolved`. `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:180`
- A fab is never discarded because the event type is fab-neutral: `Resolved` is chosen first whenever a fab is present. `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:182`
- The `Actor` defaults to `ActorIdentifier.System` for a row built without one. `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:58`
- `HandlerEnteredAt` is set only when the caller supplies a value, and `WrittenAt` is never assigned in C# (the database supplies it). `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:158` and `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:126`
- Redelivery of the same event is absorbed by a unique pair `EventIdentifier` + `OccurredAt` enforced by the persistence layer, not by the entity; the contract is stated on the repository. `src/AuditObservability/Domain/AuditEvent/IAuditEventRepository.cs:19`

`V1Envelope` and `V1Mapping` are input records declared in `AuditEvent.cs`: `V1Envelope` (`src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:199`) carries the event type name, occurrence time, optional fab, actor, optional username, event identifier, serialised payload and a `FabScope` (default `Owned`); `V1Mapping` (`src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:214`) carries the optional resource kind and resource identifier, with `V1Mapping.Unmapped` for an event that maps to no resource (`src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:219`).

## Value objects

| Name | Wraps | Rule enforced | Citation |
|---|---|---|---|
| `AuditEventIdentifier` | `Guid` | must not be the empty Guid in `From` | `src/AuditObservability/Domain/AuditEvent/AuditEventIdentifier.cs:20` |
| `ActorIdentifier` | `Guid` | `From` refuses the empty Guid; `ActorIdentifier.System` is the single wrapper of `Guid.Empty` and `IsSystem` reports it | `src/AuditObservability/Domain/AuditEvent/ActorIdentifier.cs:27` |
| `ActorUsername` | `string` | not blank, at most 255 characters | `src/AuditObservability/Domain/AuditEvent/ActorUsername.cs:26` |
| `EventIdentifier` | `Guid` | must not be the empty Guid in `From` | `src/AuditObservability/Domain/AuditEvent/EventIdentifier.cs:22` |
| `EventKind` | `string` | not blank, at most 100 characters, starts with a letter and holds only letters or digits (the integration event's type name) | `src/AuditObservability/Domain/AuditEvent/EventKind.cs:21` |
| `FabIdentifier` | `string` | 2 to 32 characters, lowercase letters, digits or `-`, starting with a letter; this context's own copy (ADR-0044 per its comment) | `src/AuditObservability/Domain/AuditEvent/FabIdentifier.cs:70` |
| `FabAttribution` | enum | `Unresolved`, `Resolved`, `NotApplicable`; `Unresolved` is the zero value, the fail-safe default | `src/AuditObservability/Domain/AuditEvent/FabAttribution.cs:22` |
| `FabScope` | enum | `Owned` (zero value, default) or `Neutral` | `src/AuditObservability/Domain/AuditEvent/FabScope.cs:19` |
| `OccurredAt` | `DateTimeOffset` | normalised to UTC | `src/AuditObservability/Domain/AuditEvent/OccurredAt.cs:36` |
| `ReceivedAt` | `DateTimeOffset` | normalised to UTC | `src/AuditObservability/Domain/AuditEvent/ReceivedAt.cs:36` |
| `HandlerEnteredAt` | `DateTimeOffset` | normalised to UTC; null on the row unless the measurement switch is on | `src/AuditObservability/Domain/AuditEvent/HandlerEnteredAt.cs:36` |
| `WrittenAt` | `DateTimeOffset` | normalised to UTC; set by the database insert, not by C# | `src/AuditObservability/Domain/AuditEvent/WrittenAt.cs:36` |
| `ResourceKind` | `string` | closed vocabulary of 12 kinds; `From` throws for any value not in `All` | `src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:72` |
| `ResourceIdentifier` | `string` | not blank, at most 255 characters | `src/AuditObservability/Domain/AuditEvent/ResourceIdentifier.cs:20` |
| `AuditPayload` | `string` | must not be blank; not parsed or validated as JSON | `src/AuditObservability/Domain/AuditEvent/AuditPayload.cs:49` |
| `PayloadSizeBytes` | `int` | at least 0 (`From`); `Of(payload)` is the UTF-8 byte count | `src/AuditObservability/Domain/AuditEvent/PayloadSizeBytes.cs:29` |
| `StoredPayload` | `AuditPayload` + `PayloadSizeBytes` | the size is derived from the content in `From`; the two-argument constructor is private, for materialisation only | `src/AuditObservability/Domain/AuditEvent/StoredPayload.cs:45` |
| `SchemaVersion` | `short` | at least 0; `Current` is 1 | `src/AuditObservability/Domain/AuditEvent/SchemaVersion.cs:34` |

The twelve `ResourceKind` values are `camera`, `stream`, `layout`, `overlay`, `variable`, `rule`, `event`, `webhook`, `device`, `kiosk`, `webhook-integration` and `wall` (`src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:56`). The type's comments mark `rule` and `webhook` as currently unproducible (nothing maps to them) but kept in the list (`src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:33`, `src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:45`).

## Domain events

None. `AuditEvent` raises no domain events, and no `Events/` folder exists in this Domain layer.

## Integration events

### How AuditObservability consumes

There is one handler class, not one per event: `IntegrationEventAuditHandler` (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:33`). It has 26 public `Handle(<Event>V<N> message, CancellationToken)` overloads, one for each concrete integration event type in `Shared.Contracts`, each a one-line delegation to a private `AuditAsync` (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:35` to `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:60`). `AuditAsync` is generic over `IIntegrationEvent`: it reads the shared `EventMetadata` envelope, builds a `V1Envelope` and calls `AuditingMessageHandler.HandleAsync` (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:62`).

The class comment gives the reason for the overloads: handlers are matched by concrete type, so each event needs its own `Handle` entry point (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:24`). Coverage is guarded by an architecture test that fails if a concrete `IIntegrationEvent` has no `Handle` overload on this class: `tests/Architecture.Tests/BoundaryTests.cs:259`.

Per delivery, `AuditingMessageHandler.HandleAsync` looks the event type up in `V1ResourceMap`, calls `AuditEvent.From`, then `repository.Add` and `SaveAsync` (`src/AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs:56` to `src/AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs:60`). Which events are fab-neutral is a single register, `FabNeutralEvents`, listing `OverlayRevisionPublishedV3`, `OverlayRevisionArchivedV1` and `AuditChunkArchivedV1`; any other type resolves to `FabScope.Owned` (`src/AuditObservability/Application/EventHandlers/FabNeutralEvents.cs:41`, `src/AuditObservability/Application/EventHandlers/FabNeutralEvents.cs:57`). Some payloads are redacted before storage (camera addresses reduced to their origin) by `AuditPayloadRedaction` (`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs:26`).

### Published

| Event | Contract file | Publishing code |
|---|---|---|
| `AuditChunkArchivedV1` | `src/Shared.Contracts/AuditObservability/AuditChunkArchivedV1.cs` | `src/AuditObservability/Application/Retention/AuditRetentionHostedService.cs:146` (published after a chunk is archived, by the retention hosted service; there is no domain event handler on this path) |

AuditObservability also consumes its own event, see below.

### Consumed

Each row is a `Handle(...)` overload in `IntegrationEventAuditHandler`. The publishing context is the folder of the contract file.

| Event | Contract file | Handler |
|---|---|---|
| `CameraRegisteredV1` | `src/Shared.Contracts/CameraCatalog/CameraRegisteredV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:35` |
| `CameraRetiredV1` | `src/Shared.Contracts/CameraCatalog/CameraRetiredV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:36` |
| `CameraAddressChangedV1` | `src/Shared.Contracts/CameraCatalog/CameraAddressChangedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:37` |
| `CameraAddressReadV1` | `src/Shared.Contracts/CameraCatalog/CameraAddressReadV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:38` |
| `CameraRenamedV1` | `src/Shared.Contracts/CameraCatalog/CameraRenamedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:39` |
| `FabEventIngestedV1` | `src/Shared.Contracts/EventIngestion/FabEventIngestedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:40` |
| `DeviceRegisteredV1` | `src/Shared.Contracts/Identity/DeviceRegisteredV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:41` |
| `KioskEnrolledV1` | `src/Shared.Contracts/Identity/KioskEnrolledV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:42` |
| `WebhookIntegrationRotatedV1` | `src/Shared.Contracts/Identity/WebhookIntegrationRotatedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:43` |
| `WebhookIntegrationRegisteredV1` | `src/Shared.Contracts/EventIngestion/WebhookIntegrationRegisteredV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:44` |
| `WebhookIntegrationRevokedV1` | `src/Shared.Contracts/EventIngestion/WebhookIntegrationRevokedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:45` |
| `LayoutRevisionArchivedV1` | `src/Shared.Contracts/LayoutComposition/LayoutRevisionArchivedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:46` |
| `LayoutRevisionPublishedV2` | `src/Shared.Contracts/LayoutComposition/LayoutRevisionPublishedV2.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:47` |
| `OverlayHighlightRequestedV1` | `src/Shared.Contracts/LayoutComposition/OverlayHighlightRequestedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:48` |
| `OverlayRevisionArchivedV1` | `src/Shared.Contracts/OverlayDesigner/OverlayRevisionArchivedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:49` |
| `OverlayRevisionPublishedV3` | `src/Shared.Contracts/OverlayDesigner/OverlayRevisionPublishedV3.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:50` |
| `StreamHealthChangedV1` | `src/Shared.Contracts/StreamDistribution/StreamHealthChangedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:51` |
| `SystemVariableArchivedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableArchivedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:52` |
| `SystemVariableDefinedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableDefinedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:53` |
| `SystemVariableValueChangedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableValueChangedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:54` |
| `SystemVariableValueRequestedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableValueRequestedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:55` |
| `ResolvedOverlayTextChangedV3` | `src/Shared.Contracts/SystemVariables/ResolvedOverlayTextChangedV3.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:56` |
| `AuditChunkArchivedV1` | `src/Shared.Contracts/AuditObservability/AuditChunkArchivedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:57` |
| `WallConfiguredV1` | `src/Shared.Contracts/LayoutComposition/WallConfiguredV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:58` |
| `WallSceneChangedV1` | `src/Shared.Contracts/LayoutComposition/WallSceneChangedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:59` |
| `WallSceneSwitchRequestedV1` | `src/Shared.Contracts/LayoutComposition/WallSceneSwitchRequestedV1.cs` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:60` |

That is 26 consumed events, which equals the 26 `IIntegrationEvent` records under `src/Shared.Contracts/` at the time of reading (counted by file listing, excluding `RevokedClientsResponse`, which is not named `*V<N>`).

## Repositories and services

- `IAuditEventRepository` (`src/AuditObservability/Domain/AuditEvent/IAuditEventRepository.cs:9`): write path only.
  - `Add(AuditEvent)` stages a row; idempotency on `EventIdentifier` + `OccurredAt` is enforced by the persistence layer's unique index `ux_audit_event_identifier` (`src/AuditObservability/Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs:162`), so callers do not check for prior rows (`src/AuditObservability/Domain/AuditEvent/IAuditEventRepository.cs:19`).
  - `SaveAsync` persists staged rows, expected to be `INSERT ... ON CONFLICT (event_identifier, occurred_at) DO NOTHING` so an at-least-once redelivery is absorbed (`src/AuditObservability/Domain/AuditEvent/IAuditEventRepository.cs:31`).
  - Reads bypass this interface and go through query handlers against the DbContext (entity comment, `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:11`).

No domain services exist in this Domain layer.

## Omitted

None. Every Domain type is named above.

## Questions

- `AuditingMessageHandler`'s comment says Infrastructure "wires this handler into Wolverine by building one bus subscriber per concrete `IIntegrationEvent`" (`src/AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs:25`). The code shows one handler class with 26 overloads (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:35`), and `AddAuditObservabilityInfrastructure` registers no per-event subscribers (`src/AuditObservability/Infrastructure/AuditObservabilityInfrastructureModule.cs:79`). How Wolverine discovers `IntegrationEventAuditHandler` was not established; the comment and the code do not describe the same mechanism.
- The "26" in the Consumed table is a count of contract files at the time of reading, cross-checked by hand against the 26 overloads; the architecture test at `tests/Architecture.Tests/BoundaryTests.cs:259` is the enforced guard, not this page.
- `AuditChunkArchivedV1` is both published (retention service) and consumed (the audit handler records it). That the loop is intended is implied by the overload at `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:57` and its registration as fab-neutral (`src/AuditObservability/Application/EventHandlers/FabNeutralEvents.cs:45`), but whether the publish reaches the same context's listener at runtime was not verified.
- `ResourceKind.Rule` and `ResourceKind.Webhook` are documented as currently unproducible (`src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:33`, `src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:45`); this page records that claim from the comments and did not independently verify it against `V1ResourceMap`.
