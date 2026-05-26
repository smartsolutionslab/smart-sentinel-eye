using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class ReportStreamHealthCommandHandler(
    IStreamRepository streams,
    IClock clock,
    ILogger<ReportStreamHealthCommandHandler> log)
    : ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>
{
    public async Task<Result<StreamState, ReportStreamHealthError>> HandleAsync(
        ReportStreamHealthCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Stream> existing = await streams
            .GetByCameraAsync(command.Camera, cancellationToken)
            .ConfigureAwait(false);

        if (!existing.HasValue)
        {
            return Result<StreamState, ReportStreamHealthError>.Failure(
                new ReportStreamHealthError.StreamNotFound(command.Camera.Value));
        }

        Stream stream = existing.Value;

        try
        {
            if (command.DeclareOffline)
            {
                stream.ReportOffline(
                    command.Observation.LastError ?? "offline (no frames within retry window)",
                    clock);
            }
            else if (command.Observation.IsReady)
            {
                stream.ReportHealthy(command.Observation.DetectedMode, clock);
            }
            else
            {
                stream.ReportDegraded(
                    command.Observation.LastError ?? "no frame within the health-watcher window",
                    clock);
            }
        }
        catch (InvalidOperationException ex)
        {
            string targetState = DescribeTarget(command);
            log.LogWarning(ex,
                "Rejected health transition for camera {Camera}.",
                command.Camera);
            return Result<StreamState, ReportStreamHealthError>.Failure(
                new ReportStreamHealthError.InvalidStateTransition(
                    From: stream.State.Value,
                    To: targetState,
                    Reason: ex.Message));
        }

        await streams.SaveAsync(cancellationToken).ConfigureAwait(false);

        return Result<StreamState, ReportStreamHealthError>.Success(stream.State);
    }

    private static string DescribeTarget(ReportStreamHealthCommand command)
    {
        if (command.DeclareOffline) return "Offline";
        return command.Observation.IsReady ? "Healthy" : "Degraded";
    }
}
