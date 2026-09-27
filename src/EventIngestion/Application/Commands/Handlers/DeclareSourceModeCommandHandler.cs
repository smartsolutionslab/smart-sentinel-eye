using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class DeclareSourceModeCommandHandler(
    ISourceModeRepository sourceModes,
    IClock clock,
    ILogger<DeclareSourceModeCommandHandler> logger)
    : ICommandHandler<DeclareSourceModeCommand, Result<SourceModeIdentifier, DeclareSourceModeError>>
{
    public async Task<Result<SourceModeIdentifier, DeclareSourceModeError>> HandleAsync(
        DeclareSourceModeCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, source, mode, declaredBy) = command;

        Option<SourceMode> existing = await sourceModes.GetAsync(fab, source, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(DeclareSourceModeFailures.SourceModeAlreadyDeclared(fab.Value, source.Value));
        }

        SourceMode sourceMode = SourceMode.Declare(fab, source, mode, declaredBy, clock);
        sourceModes.Add(sourceMode);
        await sourceModes.SaveAsync(cancellationToken);

        logger.SourceModeDeclared(fab, source, mode, sourceMode.Id);

        return Success(sourceMode.Id);
    }
}
