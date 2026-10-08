using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

/// <summary>
/// Registers an event type. Since spec 317 (#2325) registering a kind also
/// promotes that fab's held rows of it — <c>Held</c> → <c>Promoted</c>, as
/// one set-based update (FR-007, A7) — on both the success path and the
/// already-registered (409) path, so a registration whose promotion step
/// failed, or a row held by a batch assessed just before the registration
/// committed, converges on the next attempt to register the same kind.
/// </summary>
public sealed class RegisterEventTypeCommandHandler(
    IRegisteredEventTypeRepository eventTypes,
    IDeadLetterRepository deadLetters,
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
            await PromoteAsync(fab, kind, cancellationToken);
            return Failure(RegisterEventTypeFailures.EventTypeAlreadyRegistered(fab.Value, kind.Value));
        }

        RegisteredEventType eventType = RegisteredEventType.Register(fab, kind, registeredBy, clock);
        eventTypes.Add(eventType);
        await eventTypes.SaveAsync(cancellationToken);

        logger.EventTypeRegistered(fab, kind, eventType.Id);

        await PromoteAsync(fab, kind, cancellationToken);

        return Success(eventType.Id);
    }

    private async Task PromoteAsync(FabIdentifier fab, Kind kind, CancellationToken cancellationToken)
    {
        int promoted = await deadLetters.PromoteHeldAsync(fab, kind, cancellationToken);
        if (promoted > 0)
        {
            logger.HeldRowsPromoted(fab, kind, promoted);
        }
    }
}
