using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class RetireStreamCommandHandler(
    IStreamRepository streams,
    IRtspGateway rtsp,
    IClock clock,
    ILogger<RetireStreamCommandHandler> logger)
    : ICommandHandler<RetireStreamCommand, Result<Option<StreamIdentifier>, RetireStreamError>>
{
    public async Task<Result<Option<StreamIdentifier>, RetireStreamError>> HandleAsync(
        RetireStreamCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        CameraIdentifier camera = command.Camera;

        Option<Stream> existing = await streams.GetByCameraAsync(camera, cancellationToken);

        if (!existing.HasValue)
        {
            logger.NoStreamToRetire(camera);
            return Success(Option<StreamIdentifier>.None);
        }

        Stream stream = existing.Value;

        stream.Retire(clock);

        // Saved before the path is touched, and that order is the whole of
        // FR-008a on this side. The row reaching its terminal state is what
        // stops the health watcher sweeping it; the path removal is cleanup. Do
        // it the other way round and a failed save leaves MediaMTX without the
        // path while the row is still live, so the watcher probes a path that no
        // longer exists and announces about it forever — worse than not having
        // retired at all.
        await streams.SaveAsync(cancellationToken);

        try
        {
            await rtsp.RemovePathAsync(stream.Path, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // The retirement is already durable, so this is not lost work — it
            // is unfinished cleanup. Reported as a failure so the outbox
            // redelivers; Retire is idempotent, so the retry removes the path
            // without announcing a second retirement.
            logger.PathRemovalFailed(ex, camera);
            return Failure(RetireStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        logger.RetiredStream(stream.Id, camera, stream.Path);

        return Success(Option<StreamIdentifier>.Some(stream.Id));
    }
}
