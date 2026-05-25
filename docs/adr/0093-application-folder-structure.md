# ADR-0093: Application Layer Folder Structure — Per Message Kind

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney organizes each context's Application project by **message
kind** — top-level folders for commands, queries, event handlers,
DTOs — rather than by aggregate. Each command has its own file
alongside a paired sealed-record error union; handlers live in a
`Handlers/` subfolder.

## Decision

Each context's `Application` project uses the following top-level
folder structure:

```
SmartSentinelEye.CameraCatalog.Application/
  Commands/
    RegisterCameraCommand.cs              ← one file per command
    RegisterCameraErrors.cs               ← paired sealed-record error union
    RegisterCameraStreamingItem.cs        ← supporting types as needed
    DecommissionCameraCommand.cs
    DecommissionCameraErrors.cs
    Handlers/
      RegisterCameraCommandHandler.cs     ← all command handlers here
      DecommissionCameraCommandHandler.cs

  Queries/
    GetCameraByIdentifierQuery.cs         ← one file per query
    GetCameraByIdentifierErrors.cs
    ListCamerasQuery.cs
    Handlers/
      GetCameraByIdentifierQueryHandler.cs
      ListCamerasQueryHandler.cs

  EventHandlers/
    CameraRegisteredDomainEventHandler.cs ← in-process domain event handler
    OperatorRevokedIntegrationEventConsumer.cs ← cross-context integration event

  DTOs/
    CameraDto.cs                          ← return shapes for queries
    CameraSummaryDto.cs
```

- **Commands are records implementing `ICommand<TResult>`** (see
  ADR-0042); handlers implement `ICommandHandler<TCommand, TResult>`.
- **Queries are records implementing `IQuery<TResult>`**; handlers
  implement `IQueryHandler<TQuery, TResult>`.
- **Each command and each query has its own file**.
- **Paired error file `<Command/Query>Errors.cs`** holds the sealed-
  record `ApiError` hierarchy (ADR-0089) used by that handler.
- **`EventHandlers/`** holds both domain event handlers (in-process,
  same transaction) and integration event consumers (from RabbitMQ).
  Naming convention:
  `<Aggregate><Verb>DomainEventHandler` and
  `<Source><Verb>IntegrationEventConsumer`.
- **`DTOs/`** holds the read-shape return types for queries.

## Consequences

- **Positive:** uniform across all 9 contexts; reviewers know where
  to look.
- **Positive:** one file per command/query keeps merge conflicts
  minimal during concurrent feature work.
- **Positive:** matches Yumney verbatim.
- **Negative:** folder by message kind, not by aggregate, splits a
  feature's commands across `Commands/` and `Handlers/`. Acceptable;
  IDE navigation handles the cognitive cost.

## Alternatives Considered

- **Package by aggregate** (`Camera/Commands/...`,
  `CameraGroup/Commands/...`) — matches Domain layout but multiplies
  folder count.
- **Package by feature** (`RegisterCamera/{Command, Handler,
  Errors}.cs`) — fewer files per command but more folders per
  context.
