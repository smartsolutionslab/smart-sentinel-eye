using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class RegisterEventTypeCommandHandler(
    IRegisteredEventTypeRepository eventTypes,
    IClock clock,
    ILogger<RegisterEventTypeCommandHandler> logger)
    : ICommandHandler<RegisterEventTypeCommand, Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>>
{
    public async Task<Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>> HandleAsync(
        RegisterEventTypeCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, kind, registeredBy) = command;

        Option<RegisteredEventType> existing = await eventTypes.GetRegisteredAsync(fab, kind, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(RegisterEventTypeFailures.EventTypeAlreadyRegistered(fab.Value, kind.Value));
        }

        RegisteredEventType eventType = RegisteredEventType.Register(fab, kind, registeredBy, clock);
        eventTypes.Add(eventType);
        await eventTypes.SaveAsync(cancellationToken);

        logger.EventTypeRegistered(fab, kind, eventType.Id);

        return Success(eventType.Id);
    }
}
