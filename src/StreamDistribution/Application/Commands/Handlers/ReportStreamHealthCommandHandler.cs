using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class ReportStreamHealthCommandHandler(
    IStreamRepository streams,
    IClock clock,
    ILogger<ReportStreamHealthCommandHandler> logger)
    : ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>
{
    public async Task<Result<StreamState, ReportStreamHealthError>> HandleAsync(
        ReportStreamHealthCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (CameraIdentifier camera, RtspPathHealth? observation, bool declareOffline) = command;

        Option<Stream> existing = await streams.GetByCameraAsync(camera, cancellationToken);

        if (!existing.HasValue)
        {
            return Failure(ReportStreamHealthFailures.StreamNotFound(camera.Value));
        }

        Stream stream = existing.Value;

        try
        {
            if (declareOffline)
            {
                stream.ReportOffline(StreamError.Truncating(observation.LastError ?? "offline (no frames within retry window)"), clock);
            }
            else if (observation.IsReady)
            {
                stream.ReportHealthy(observation.DetectedMode, clock);
            }
            else
            {
                stream.ReportDegraded(StreamError.Truncating(observation.LastError ?? "no frame within the health-watcher window"), clock);
            }
        }
        catch (InvalidOperationException ex)
        {
            string targetState = DescribeTarget(command);
            logger.RejectedHealthTransition(ex, camera);
            return Failure(ReportStreamHealthFailures.InvalidStateTransition(
                    from: stream.State.Value,
                    to: targetState,
                    reason: ex.Message));
        }

        await streams.SaveAsync(cancellationToken);

        return Success(stream.State);
    }

    private static string DescribeTarget(ReportStreamHealthCommand command)
    {
        if (command.DeclareOffline)
        {
            return "Offline";
        }

        return command.Observation.IsReady ? "Healthy" : "Degraded";
    }
}
