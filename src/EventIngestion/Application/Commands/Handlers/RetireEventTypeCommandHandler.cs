using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class RetireEventTypeCommandHandler(
    IRegisteredEventTypeRepository eventTypes,
    IClock clock,
    ILogger<RetireEventTypeCommandHandler> logger)
    : ICommandHandler<RetireEventTypeCommand, Result<RegisteredEventTypeIdentifier, RetireEventTypeError>>
{
    public async Task<Result<RegisteredEventTypeIdentifier, RetireEventTypeError>> HandleAsync(
        RetireEventTypeCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, kind, expectedVersion, retiredBy) = command;

        // The lookup runs over the resolved fab, not the row's own — a row in
        // a fab the caller did not resolve to is genuinely absent from where
        // the caller stands, and this order is load-bearing: it must run
        // before the version gate, so a stale version against another fab's
        // row still answers 404, not 409 (plan.md §6).
        Option<RegisteredEventType> found = await eventTypes.GetRegisteredAsync(fab, kind, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(RetireEventTypeFailures.EventTypeNotFound(kind.Value));
        }

        RegisteredEventType eventType = found.Value;

        if (eventType.Version != expectedVersion)
        {
            return Failure(RetireEventTypeFailures.EventTypeStale(
                kind.Value, expectedVersion, eventType.Version));
        }

        eventType.Retire(retiredBy, clock);
        await eventTypes.SaveAsync(cancellationToken);

        logger.EventTypeRetired(eventType.Fab, kind, eventType.Id);

        return Success(eventType.Id);
    }
}
