using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class ChangeSourceModeCommandHandler(
    ISourceModeRepository sourceModes,
    IClock clock,
    ILogger<ChangeSourceModeCommandHandler> logger)
    : ICommandHandler<ChangeSourceModeCommand, Result<SourceModeIdentifier, ChangeSourceModeError>>
{
    /// <summary>
    /// The lookup runs before the version gate, so a stale version against an
    /// undeclared pair still answers 404, not 409 — mirroring
    /// <c>RetireEventTypeCommandHandler</c> (plan.md §6.4).
    /// </summary>
    public async Task<Result<SourceModeIdentifier, ChangeSourceModeError>> HandleAsync(
        ChangeSourceModeCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, source, mode, expectedVersion, changedBy) = command;

        Option<SourceMode> found = await sourceModes.GetAsync(fab, source, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(ChangeSourceModeFailures.SourceModeNotDeclared(fab.Value, source.Value));
        }

        SourceMode sourceMode = found.Value;

        if (sourceMode.Version != expectedVersion)
        {
            return Failure(ChangeSourceModeFailures.SourceModeStale(
                source.Value, expectedVersion, sourceMode.Version));
        }

        sourceMode.Change(mode, changedBy, clock);
        await sourceModes.SaveAsync(cancellationToken);

        logger.SourceModeChanged(fab, source, mode, sourceMode.Id);

        return Success(sourceMode.Id);
    }
}
