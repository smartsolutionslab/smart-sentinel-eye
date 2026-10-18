using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class RepointStreamCommandHandler(
    IStreamRepository streams,
    IRtspGateway rtsp,
    IClock clock,
    ILogger<RepointStreamCommandHandler> logger)
    : ICommandHandler<RepointStreamCommand, Result<Option<StreamIdentifier>, RepointStreamError>>
{
    public async Task<Result<Option<StreamIdentifier>, RepointStreamError>> HandleAsync(
        RepointStreamCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (camera, rtspSourceUrl, changedAt, cameraVersion) = command;

        var parsedSource = ParseSource(rtspSourceUrl);

        if (parsedSource.IsFailure)
        {
            return Failure(parsedSource.Error);
        }

        var sourceUrl = parsedSource.Value;

        var repointable = StreamToRepoint(await streams.GetByCameraAsync(camera, cancellationToken), camera);

        if (!repointable.HasValue)
        {
            return Success(Option<StreamIdentifier>.None);
        }

        var stream = repointable.Value;

        if (!stream.RepointTo(sourceUrl, changedAt, cameraVersion, clock))
        {
            return IgnoreOlderRepoint(stream, rtspSourceUrl, changedAt, cameraVersion);
        }

        return await SaveAndRepointGatewayAsync(stream, rtspSourceUrl, cancellationToken);
    }

    private async Task<Result<Option<StreamIdentifier>, RepointStreamError>> SaveAndRepointGatewayAsync(
        Stream stream,
        string rtspSourceUrl,
        CancellationToken cancellationToken)
    {
        // Saved before the gateway is touched, the same ordering the retirement
        // handler uses and for the same reason: the durable record of where the
        // stream should point is what the startup reconciler restores from. If
        // the gateway succeeded and the save failed, the reconciler would
        // faithfully put the old address back.
        await streams.SaveAsync(cancellationToken);

        try
        {
            await rtsp.RepointPathAsync(stream.Path, rtspSourceUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Unfinished cleanup rather than lost work: the aggregate already
            // holds the new address, so the outbox retry finishes the re-point
            // rather than redoing it. RepointTo is idempotent.
            logger.PathRepointFailed(ex, stream.Camera);
            return Failure(RepointStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        logger.RepointedStream(stream.Id, stream.Camera, stream.Path);

        return Success(Option<StreamIdentifier>.Some(stream.Id));
    }

    private Result<Option<StreamIdentifier>, RepointStreamError> IgnoreOlderRepoint(
        Stream stream,
        string rtspSourceUrl,
        SourceChangedAt changedAt,
        Option<SourceCameraVersion> cameraVersion)
    {
        // Acked rather than failed: no retry will make an older change newer,
        // so failing would redeliver it forever.
        var origin = UrlRedaction.ToOrigin(rtspSourceUrl);

        if (cameraVersion.HasValue)
        {
            logger.IgnoredOlderVersionedRepoint(stream.Camera, origin, cameraVersion.Value, stream.SourceCameraVersion);
        }
        else
        {
            logger.IgnoredOlderRepoint(stream.Camera, origin, changedAt);
        }

        return Success(Option<StreamIdentifier>.None);
    }

    private static Result<StreamSourceUrl, RepointStreamError> ParseSource(string rtspSourceUrl)
    {
        // Re-validated at the trust boundary: the address arrives as a
        // primitive from CameraCatalog, so its invariants are asserted again
        // rather than trusted because another context checked them.
        try
        {
            return Success(StreamSourceUrl.From(rtspSourceUrl));
        }
        catch (ArgumentException ex)
        {
            return Failure(RepointStreamFailures.InvalidRtspSource(ex.Message));
        }
    }

    private Option<Stream> StreamToRepoint(
        Option<Stream> existing,
        CameraIdentifier camera)
    {
        if (!existing.HasValue)
        {
            // Not a failure: a camera registered without a resolvable fab is
            // never provisioned a stream (spec 016 FR-004), and a failure here
            // would have the outbox redeliver the correction forever.
            logger.NoStreamToRepoint(camera);
            return Option<Stream>.None;
        }

        var stream = existing.Value;

        if (stream.State == StreamState.Retired)
        {
            // The camera's address changed after its stream was retired. The
            // aggregate would refuse, and rightly — but that is not an error
            // worth redelivering, because no retry will make it succeed.
            logger.SkippedRepointOfRetiredStream(camera);
            return Option<Stream>.None;
        }

        return Option<Stream>.Some(stream);
    }
}
