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

        (CameraIdentifier camera, string? rtspSourceUrl, OperatorIdentifier provisionedBy) = command;

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
            logger.StreamAlreadyExists(camera);
            return Success(existing.Value.Id);
        }

        Stream stream = Stream.Provision(camera, sourceUrl, provisionedBy, clock);
        streams.Add(stream);

        try
        {
            await rtsp.AddPathAsync(stream.Path, rtspSourceUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.PathRegistrationFailed(ex, camera);
            return Failure(ProvisionStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        await streams.SaveAsync(cancellationToken);

        logger.ProvisionedStream(stream.Id, stream.Camera, stream.Path);

        return Success(stream.Id);
    }
}
