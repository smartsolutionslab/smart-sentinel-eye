using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

/// <summary>
/// Phase 4a prelude (spec 143 T002): the signature is the one plan.md §6
/// specifies, so the 4a tests written against it keep compiling once T005
/// fills the body in. The body below answers nothing yet and touches no
/// repository — it is not the uniqueness lookup, register, add or save
/// plan.md describes; those land at T005.
/// </summary>
public sealed class RegisterEventTypeCommandHandler(
    IRegisteredEventTypeRepository eventTypes,
    IClock clock,
    ILogger<RegisterEventTypeCommandHandler> logger)
    : ICommandHandler<RegisterEventTypeCommand, Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>>
{
    public Task<Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>> HandleAsync(
        RegisterEventTypeCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, kind, registeredBy) = command;

        // Phase 4a prelude: the injected collaborators are unused for now.
        // T005 replaces this whole body with the uniqueness lookup and the write.
        _ = eventTypes;
        _ = clock;
        _ = logger;
        _ = fab;
        _ = kind;
        _ = registeredBy;

        return Task.FromResult<Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>>(
            Success(RegisteredEventTypeIdentifier.New()));
    }
}
