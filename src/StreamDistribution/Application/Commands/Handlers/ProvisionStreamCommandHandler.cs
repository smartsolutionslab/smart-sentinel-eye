using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class ProvisionStreamCommandHandler(
    IStreamRepository streams,
    IRtspGateway rtsp,
    IClock clock,
    ILogger<ProvisionStreamCommandHandler> logger)
    : ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>>
{
    public async Task<Result<StreamIdentifier, ProvisionStreamError>> HandleAsync(
        ProvisionStreamCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (FabIdentifier fab, CameraIdentifier camera, string? rtspSourceUrl, OperatorIdentifier provisionedBy) = command;

        if (string.IsNullOrWhiteSpace(rtspSourceUrl))
        {
            return Failure(ProvisionStreamFailures.InvalidRtspSource("source URL is required"));
        }

        // Re-validated at the trust boundary: the URL arrives as a primitive
        // from CameraCatalog, so its invariants are asserted again on the way in.
        StreamSourceUrl sourceUrl;
        try
        {
            sourceUrl = StreamSourceUrl.From(rtspSourceUrl);
        }
        catch (ArgumentException ex)
        {
            return Failure(ProvisionStreamFailures.InvalidRtspSource(ex.Message));
        }

        Option<Stream> existing = await streams.GetByCameraAsync(camera, cancellationToken);

        if (existing.HasValue)
        {
            Stream existingStream = existing.Value;
            logger.StreamAlreadyExists(camera);

            if (existingStream.State == StreamState.Retired)
            {
                return Success(existingStream.Id);
            }

            // A redelivery onto an already-saved row must re-assert the path
            // rather than short-circuit: after the reorder below, "row saved,
            // path not added" is the new partial state a failed add leaves
            // behind, and only a redelivery resolves it before the next
            // restart (spec 309 FR-004).
            return await RegisterPathAsync(existingStream, cancellationToken);
        }

        // Saved first so a failed save cannot strand a live MediaMTX path with
        // no row behind it, which the WHEP hook would admit for any fab
        // (spec 309 FR-001). A failed add after the save is unfinished work
        // the outbox redelivers, and the existing-row branch above finishes
        // it.
        Stream stream = Stream.Provision(fab, camera, sourceUrl, provisionedBy, clock);
        streams.Add(stream);
        await streams.SaveAsync(cancellationToken);

        return await RegisterPathAsync(stream, cancellationToken);
    }

    private async Task<Result<StreamIdentifier, ProvisionStreamError>> RegisterPathAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            await rtsp.AddPathAsync(stream.Path, stream.SourceUrl.Value, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.PathRegistrationFailed(ex, stream.Camera);
            return Failure(ProvisionStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        logger.ProvisionedStream(stream.Id, stream.Camera, stream.Path);

        return Success(stream.Id);
    }
}
