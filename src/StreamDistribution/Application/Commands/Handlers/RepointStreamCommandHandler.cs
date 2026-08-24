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

        var (camera, rtspSourceUrl) = command;

        // Re-validated at the trust boundary: the address arrives as a
        // primitive from CameraCatalog, so its invariants are asserted again
        // rather than trusted because another context checked them.
        StreamSourceUrl sourceUrl;
        try
        {
            sourceUrl = StreamSourceUrl.From(rtspSourceUrl);
        }
        catch (ArgumentException ex)
        {
            return Failure(RepointStreamFailures.InvalidRtspSource(ex.Message));
        }

        Option<Stream> existing = await streams.GetByCameraAsync(camera, cancellationToken);

        if (!existing.HasValue)
        {
            // Not a failure: a camera registered without a resolvable fab is
            // never provisioned a stream (spec 016 FR-004), and a failure here
            // would have the outbox redeliver the correction forever.
            logger.NoStreamToRepoint(camera);
            return Success(Option<StreamIdentifier>.None);
        }

        Stream stream = existing.Value;

        if (stream.State == StreamState.Retired)
        {
            // The camera's address changed after its stream was retired. The
            // aggregate would refuse, and rightly — but that is not an error
            // worth redelivering, because no retry will make it succeed.
            logger.SkippedRepointOfRetiredStream(camera);
            return Success(Option<StreamIdentifier>.None);
        }

        stream.RepointTo(sourceUrl, clock);

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
            logger.PathRepointFailed(ex, camera);
            return Failure(RepointStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        logger.RepointedStream(stream.Id, camera, stream.Path);

        return Success(Option<StreamIdentifier>.Some(stream.Id));
    }
}
