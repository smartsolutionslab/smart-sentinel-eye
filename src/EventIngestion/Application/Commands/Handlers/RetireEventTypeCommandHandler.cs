using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

/// <summary>
/// Phase 4a prelude (spec 143 T002): the signature is the one plan.md §6
/// specifies. The body below refuses everything as not-found and touches no
/// repository; T005 replaces it with the lookup-then-version-gate order
/// plan.md describes (load-bearing: lookup first, version gate second).
/// </summary>
public sealed class RetireEventTypeCommandHandler(
    IRegisteredEventTypeRepository eventTypes,
    IClock clock,
    ILogger<RetireEventTypeCommandHandler> logger)
    : ICommandHandler<RetireEventTypeCommand, Result<RegisteredEventTypeIdentifier, RetireEventTypeError>>
{
    public Task<Result<RegisteredEventTypeIdentifier, RetireEventTypeError>> HandleAsync(
        RetireEventTypeCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fabs, kind, expectedVersion, retiredBy) = command;

        // Phase 4a prelude: the injected collaborators are unused for now.
        // T005 replaces this whole body with the fab-scoped lookup and the version gate.
        _ = eventTypes;
        _ = clock;
        _ = logger;
        _ = fabs;
        _ = expectedVersion;
        _ = retiredBy;

        return Task.FromResult<Result<RegisteredEventTypeIdentifier, RetireEventTypeError>>(
            Failure(RetireEventTypeFailures.EventTypeNotFound(kind.Value)));
    }
}
