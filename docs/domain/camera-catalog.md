# CameraCatalog domain model

**Source:** `src/CameraCatalog/Domain/` (and the Application layer only where it shows publishing/consuming of integration events).

## Purpose

CameraCatalog owns the registry of cameras: which fab a camera belongs to, its operator-facing name, its RTSP address and whether it is still in service. It does not own the video stream built from a camera (StreamDistribution does) and it does not decide who may see a camera (that is access control outside this Domain layer).

## Aggregates

### `Camera`

- **Identifier:** `CameraIdentifier` (Guid v7 via `CameraIdentifier.New()`, `src/CameraCatalog/Domain/Camera/CameraIdentifier.cs:13`).
- **Root type:** `AggregateRoot<CameraIdentifier>` (`src/CameraCatalog/Domain/Camera/Camera.cs:11`).
- **State:** `Fab`, `Name`, `Url`, `Status`, `Registration`, all with private setters (`src/CameraCatalog/Domain/Camera/Camera.cs:24`).
- **Lifecycle:** two states, so no diagram. `Register` creates it as `Registered` (`src/CameraCatalog/Domain/Camera/Camera.cs:51`); `Retire` moves it to `Decommissioned` (`src/CameraCatalog/Domain/Camera/Camera.cs:88`). There is no operation out of `Decommissioned`.

| Behaviour | Effect | Raises | Citation |
|---|---|---|---|
| `Register(fab, name, url, registeredBy, clock)` | new camera, status `Registered` | `CameraRegisteredDomainEvent` | `src/CameraCatalog/Domain/Camera/Camera.cs:37` |
| `Rename(name, renamedBy, clock)` | replaces `Name` | `CameraRenamedDomainEvent` | `src/CameraCatalog/Domain/Camera/Camera.cs:176` |
| `ChangeAddress(url, changedBy, clock)` | replaces `Url` | `CameraAddressChangedDomainEvent` | `src/CameraCatalog/Domain/Camera/Camera.cs:121` |
| `Retire(retiredBy, clock)` | status to `Decommissioned` | `CameraRetiredDomainEvent` | `src/CameraCatalog/Domain/Camera/Camera.cs:79` |

**Invariants**

- Registration requires a fab, a name, an address and a clock; each is null-guarded. `src/CameraCatalog/Domain/Camera/Camera.cs:40`
- A new camera always starts as `Registered`. `src/CameraCatalog/Domain/Camera/Camera.cs:51`
- Retiring is idempotent: retiring an already `Decommissioned` camera returns without raising a second event. `src/CameraCatalog/Domain/Camera/Camera.cs:83`
- A retired camera cannot have its address changed; the call throws `InvalidOperationException`. `src/CameraCatalog/Domain/Camera/Camera.cs:126`
- A retired camera cannot be renamed; the call throws `InvalidOperationException`. `src/CameraCatalog/Domain/Camera/Camera.cs:181`
- Changing the address to the value already held is a no-op and raises nothing. `src/CameraCatalog/Domain/Camera/Camera.cs:132`
- Renaming compares the raw value ordinally, not `CameraName` equality, so a case-only correction is applied and announced; renaming to an identical string is a no-op. `src/CameraCatalog/Domain/Camera/Camera.cs:195`
- The fab is set at registration and no behaviour method changes it (the property has a private setter and none of the four methods assigns it). `src/CameraCatalog/Domain/Camera/Camera.cs:24`
- `CameraAddressChangedDomainEvent` carries the aggregate `Version` as it was before the change (`PreviousVersion`). `src/CameraCatalog/Domain/Camera/Camera.cs:147`

## Value objects

| Name | Wraps | Rule enforced | Citation |
|---|---|---|---|
| `CameraIdentifier` | `Guid` | must not be the empty Guid in `From` | `src/CameraCatalog/Domain/Camera/CameraIdentifier.cs:17` |
| `CameraName` | `string` | not null/whitespace, at most 200 characters, trimmed; equality and ordering use an upper-cased `NormalizedValue` | `src/CameraCatalog/Domain/Camera/CameraName.cs:47` |
| `CameraStatus` | `string` | closed set `Registered` / `Decommissioned`; any other string throws | `src/CameraCatalog/Domain/Camera/CameraStatus.cs:16` |
| `FabIdentifier` | `string` | 2 to 32 characters, lowercase letters, digits or `-`, starting with a letter | `src/CameraCatalog/Domain/Camera/FabIdentifier.cs:76` |
| `RegisteredAt` | `DateTimeOffset` | normalised to UTC | `src/CameraCatalog/Domain/Camera/RegisteredAt.cs:36` |
| `Registration` | `RegisteredAt` + `OperatorIdentifier` (a Shared.Kernel type) | the timestamp must not be null | `src/CameraCatalog/Domain/Camera/Registration.cs:21` |
| `RtspUrl` | `string` | see below | `src/CameraCatalog/Domain/Camera/RtspUrl.cs:65` |

`RtspUrl.From` (`src/CameraCatalog/Domain/Camera/RtspUrl.cs:67`) requires a non-blank value of at most 2048 characters starting with `rtsp://` (case-insensitive) (`src/CameraCatalog/Domain/Camera/RtspUrl.cs:92`), and additionally rejects a `user:password@` segment, a raw `@` after the authority, an unreadable or empty host, a punctuation-only host, an empty port, control characters or whitespace, a non-ASCII plain host, and several malformed IPv6 bracket forms (`src/CameraCatalog/Domain/Camera/RtspUrl.cs:68`). `RtspUrl.FromStored` (`src/CameraCatalog/Domain/Camera/RtspUrl.cs:86`) is the persistence-read path: it applies only the base checks, so a stored row that predates a stricter rule stays loadable (comment at `src/CameraCatalog/Domain/Camera/RtspUrl.cs:83`).

## Domain events

In-process only (ADR-0040).

| Event | Raised by | When | Citation |
|---|---|---|---|
| `CameraRegisteredDomainEvent` | `Camera.Register` | a camera is registered | `src/CameraCatalog/Domain/Camera/Camera.cs:55` |
| `CameraRenamedDomainEvent` | `Camera.Rename` | the raw name actually changed | `src/CameraCatalog/Domain/Camera/Camera.cs:203` |
| `CameraAddressChangedDomainEvent` | `Camera.ChangeAddress` | the address actually changed | `src/CameraCatalog/Domain/Camera/Camera.cs:140` |
| `CameraRetiredDomainEvent` | `Camera.Retire` | first retirement only | `src/CameraCatalog/Domain/Camera/Camera.cs:90` |

## Integration events

### Published

Each of the first four is published by an Application domain-event handler that translates the domain event; `CameraAddressReadV1` has no domain event and is published directly by a query handler.

| Event | Contract file | Publishing code |
|---|---|---|
| `CameraRegisteredV1` | `src/Shared.Contracts/CameraCatalog/CameraRegisteredV1.cs` | `src/CameraCatalog/Application/EventHandlers/CameraRegisteredDomainEventHandler.cs:19` |
| `CameraRenamedV1` | `src/Shared.Contracts/CameraCatalog/CameraRenamedV1.cs` | `src/CameraCatalog/Application/EventHandlers/CameraRenamedDomainEventHandler.cs:26` |
| `CameraAddressChangedV1` | `src/Shared.Contracts/CameraCatalog/CameraAddressChangedV1.cs` | `src/CameraCatalog/Application/EventHandlers/CameraAddressChangedDomainEventHandler.cs:27` |
| `CameraRetiredV1` | `src/Shared.Contracts/CameraCatalog/CameraRetiredV1.cs` | `src/CameraCatalog/Application/EventHandlers/CameraRetiredDomainEventHandler.cs:26` |
| `CameraAddressReadV1` | `src/Shared.Contracts/CameraCatalog/CameraAddressReadV1.cs` | `src/CameraCatalog/Application/Queries/Handlers/GetCameraAddressQueryHandler.cs:53` |

All five types are declared durable-outbox types for this context: `src/CameraCatalog/Infrastructure/CameraCatalogInfrastructureModule.cs:84`.

Consumers of these events, found by reading the `Handle(...)` methods in other contexts:

| Event | Consuming context | Handler |
|---|---|---|
| `CameraRegisteredV1` | StreamDistribution | `src/StreamDistribution/Application/EventHandlers/CameraRegisteredIntegrationEventHandler.cs:21` |
| `CameraRegisteredV1` | ScenarioSimulator | `src/ScenarioSimulator/EventHandlers/CameraRegisteredSimHandler.cs:33` |
| `CameraAddressChangedV1` | StreamDistribution | `src/StreamDistribution/Application/EventHandlers/CameraAddressChangedIntegrationEventHandler.cs:27` |
| `CameraRetiredV1` | StreamDistribution | `src/StreamDistribution/Application/EventHandlers/CameraRetiredIntegrationEventHandler.cs:31` |
| all five | AuditObservability | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:35` (one `Handle` overload per event, lines 35 to 39) |

No consumer of `CameraRenamedV1` or `CameraAddressReadV1` other than AuditObservability was found in `src/`.

### Consumed

None. No `Handle(...)` for another context's integration event exists in `src/CameraCatalog/`.

## Repositories and services

- `ICameraRepository` (`src/CameraCatalog/Domain/Camera/ICameraRepository.cs:10`):
  - `GetByIdentifierAsync` returns `Option<Camera>` by identifier alone (`src/CameraCatalog/Domain/Camera/ICameraRepository.cs:12`).
  - `GetWithinFabAsync` returns `Option<Camera>` for a camera within a given fab (`src/CameraCatalog/Domain/Camera/ICameraRepository.cs:28`).
  - `ExistsByNameAsync` answers whether a name is already used within a fab, optionally excluding one camera (`src/CameraCatalog/Domain/Camera/ICameraRepository.cs:57`).
  - `Add` stages a camera and `SaveAsync` persists (`src/CameraCatalog/Domain/Camera/ICameraRepository.cs:63`).

No domain services exist in this Domain layer.

## Omitted

None. Every Domain type is named above.

## Questions

- Name uniqueness within a fab is queried through `ExistsByNameAsync` (`src/CameraCatalog/Domain/Camera/ICameraRepository.cs:57`), but the aggregate itself does not enforce it; where the check is made and whether a database constraint backs it was not established from the Domain layer.
- `Camera.Register` does not null-guard `registeredBy`, and `Retire`, `Rename` and `ChangeAddress` do not guard their `OperatorIdentifier` argument (`src/CameraCatalog/Domain/Camera/Camera.cs:40`); whether `OperatorIdentifier` (Shared.Kernel) is a non-nullable struct was not checked.
