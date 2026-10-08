using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

/// <summary>
/// Returns a declared pair to undeclared (spec 317, #2325, FR-013, US4),
/// mirroring <see cref="ChangeSourceModeCommandHandler"/>'s shape: the
/// lookup runs before the version gate, so a stale version against an
/// already-undeclared pair still answers 404, not 409.
/// </summary>
public sealed class UndeclareSourceModeCommandHandler(
    ISourceModeRepository sourceModes,
    IClock clock,
    ILogger<UndeclareSourceModeCommandHandler> logger)
    : ICommandHandler<UndeclareSourceModeCommand, Result<SourceModeIdentifier, UndeclareSourceModeError>>
{
    public async Task<Result<SourceModeIdentifier, UndeclareSourceModeError>> HandleAsync(
        UndeclareSourceModeCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, source, expectedVersion, undeclaredBy) = command;

        Option<SourceMode> found = await sourceModes.GetAsync(fab, source, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(UndeclareSourceModeFailures.SourceModeNotDeclared(fab.Value, source.Value));
        }

        SourceMode sourceMode = found.Value;

        if (sourceMode.Version != expectedVersion)
        {
            return Failure(UndeclareSourceModeFailures.SourceModeStale(
                source.Value, expectedVersion, sourceMode.Version));
        }

        sourceMode.Undeclare(undeclaredBy, clock);
        sourceModes.Remove(sourceMode);
        await sourceModes.SaveAsync(cancellationToken);

        logger.SourceModeUndeclared(fab, source, sourceMode.Id);

        return Success(sourceMode.Id);
    }
}
