# Identity domain model

**Source:** `src/Identity/Domain/` (and the Application layer only where it shows publishing/consuming of integration events).

## Purpose

Identity owns the `RegisteredClient` aggregate: the record of a device, kiosk or webhook-integration client registered in a fab, including disable and secret-rotation facts. It does not own the credentials themselves in the Domain layer beyond the one-time-reveal `ClientSecret` wrapper; the Keycloak calls live in the Application layer.

## Aggregates

### `RegisteredClient`

- Identifier: `RegisteredClientIdentifier` (Guid v7, minted in `Register`) — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:54`, `src/Identity/Domain/RegisteredClient/RegisteredClientIdentifier.cs:16`.
- Base type: `AggregateRoot<RegisteredClientIdentifier>` — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:19`.
- Other state: `ClientId`, `Kind` (`ClientKind`), `Fab`, `Registration`, optional `DisabledAt`, optional `LastRotatedAt` — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:21`.

**Lifecycle.** There is no state enum; the aggregate is Registered (`DisabledAt` null) or Disabled (`DisabledAt` set). Two states, so no diagram.

| Transition | Rule | Citation |
|---|---|---|
| Register → Registered | `Register` creates the client and raises `ClientRegisteredDomainEvent`. | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:60` |
| Registered → Disabled | `Disable` stamps `DisabledAt` and raises `ClientDisabledDomainEvent`. | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:79`, `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:80` |
| Disabled → Disabled | `Disable` on a disabled client returns without effect or event. | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:74` |
| Registered → Registered (rotated) | `Rotate` stamps `LastRotatedAt` and raises `ClientRotatedDomainEvent`; can repeat. | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:102` |

No method re-enables a disabled client; the error text says to re-register instead (`src/Identity/Domain/RegisteredClient/RegisteredClient.cs:95`).

**Invariants**

- `Register` requires non-null client id, kind, fab and clock — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:46`.
- A disabled client cannot be rotated — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:92`.
- Only `WebhookIntegration` clients can be rotated — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:97`.
- `Disable` is idempotent — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:74`.
- The client's fab is fixed at registration (private setter, no method changes it) — `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:25`.

**Domain events raised:** `ClientRegisteredDomainEvent`, `ClientDisabledDomainEvent`, `ClientRotatedDomainEvent`.

## Value objects

| Name | Wraps | Rule enforced | Citation |
|---|---|---|---|
| `RegisteredClientIdentifier` | `Guid` | Non-empty; `New()` mints a v7 Guid | `src/Identity/Domain/RegisteredClient/RegisteredClientIdentifier.cs:20` |
| `ClientId` | `string` | Not blank, at most 255 chars, starts with a letter or digit, then letters, digits, `.`, `_`, `-` | `src/Identity/Domain/RegisteredClient/ClientId.cs:29` |
| `ClientKind` | `string` | Only `Device`, `Kiosk`, `WebhookIntegration`; anything else throws | `src/Identity/Domain/RegisteredClient/ClientKind.cs:20` |
| `ClientSecret` | plaintext string, held as SHA-256 base64 hash | Plaintext not blank; `Reveal()` works exactly once; `ToString()` is redacted; equality is by hash | `src/Identity/Domain/RegisteredClient/ClientSecret.cs:40`, `src/Identity/Domain/RegisteredClient/ClientSecret.cs:53`, `src/Identity/Domain/RegisteredClient/ClientSecret.cs:72` |
| `FabIdentifier` | `string` | 2 to 32 chars, lowercase letters, digits or `-`, starts with a letter | `src/Identity/Domain/RegisteredClient/FabIdentifier.cs:67` |
| `Registration` | `RegisteredAt`, `OperatorIdentifier` | `At` not null | `src/Identity/Domain/RegisteredClient/Registration.cs:21` |
| `RegisteredAt`, `DisabledAt`, `LastRotatedAt` | `DateTimeOffset` | Normalised to UTC in `From`; comparable (one row, all three named) | `src/Identity/Domain/RegisteredClient/RegisteredAt.cs:36`, `src/Identity/Domain/RegisteredClient/DisabledAt.cs:36`, `src/Identity/Domain/RegisteredClient/LastRotatedAt.cs:36` |

## Domain events

In-process only (ADR-0040).

| Event | Raised by | When | Citation |
|---|---|---|---|
| `ClientRegisteredDomainEvent` | `RegisteredClient.Register` | A client is registered | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:60` |
| `ClientDisabledDomainEvent` | `RegisteredClient.Disable` | First disable of a client | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:80` |
| `ClientRotatedDomainEvent` | `RegisteredClient.Rotate` | A webhook-integration client is rotated | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs:103` |

Only `ClientRegisteredDomainEvent` has an in-process handler in Identity's Application layer (`src/Identity/Application/EventHandlers/ClientRegisteredDomainEventHandler.cs:32`); a grep found no handler for the other two.

## Integration events

**Published**

| Event | Contract file | Publishing code |
|---|---|---|
| `DeviceRegisteredV1` | `src/Shared.Contracts/Identity/DeviceRegisteredV1.cs` | `src/Identity/Application/EventHandlers/ClientRegisteredDomainEventHandler.cs:42` (for `ClientKind.Device`) |
| `KioskEnrolledV1` | `src/Shared.Contracts/Identity/KioskEnrolledV1.cs` | `src/Identity/Application/EventHandlers/ClientRegisteredDomainEventHandler.cs:58` (for `ClientKind.Kiosk`) |
| `WebhookIntegrationRotatedV1` | `src/Shared.Contracts/Identity/WebhookIntegrationRotatedV1.cs` | `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:93` (published directly by the command handler, not via a domain event) |

**Consumed**

| Event | Contract file | Handler |
|---|---|---|
| `WebhookIntegrationRevokedV1` | `src/Shared.Contracts/EventIngestion/WebhookIntegrationRevokedV1.cs` | `src/Identity/Application/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandler.cs:26` |

For orientation (consumers of Identity's events, outside Identity): `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:41` (`DeviceRegisteredV1`), `:42` (`KioskEnrolledV1`), `:43` (`WebhookIntegrationRotatedV1`); and `src/EventIngestion/Application/EventHandlers/WebhookIntegrationRotatedV1Handler.cs:19` (`WebhookIntegrationRotatedV1`).

## Repositories and services

- `IRegisteredClientRepository` — lookups returning `Option<RegisteredClient>` by identifier, by `ClientId`, and by fab plus `ClientId`; the aggregate version for an identifier; `Add`; `SaveAsync`; and the set of active client ids — `src/Identity/Domain/RegisteredClient/IRegisteredClientRepository.cs:14`, `:17`, `:27`, `:31`, `:34`, `:36`, `:43`.

No domain services exist in the Domain layer.

## Omitted

None.

## Questions

- Where `Disable` is invoked from was not traced; only the Domain method was read. In particular, whether a path other than the revocation handler disables clients is unknown.
- The `WebhookClientRaceDisableFailed` message type exists in Identity's Application layer (`src/Identity/Application/EventHandlers/WebhookClientRaceDisableFailedHandler.cs:13`); it is not a `Shared.Contracts` event and was not classified as integration or in-process.
- The description of Keycloak backing (the `ClientSecret` hash, the client id on rotation) rests on Application-layer comments such as `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:93`; the Keycloak adapter itself was not read.
