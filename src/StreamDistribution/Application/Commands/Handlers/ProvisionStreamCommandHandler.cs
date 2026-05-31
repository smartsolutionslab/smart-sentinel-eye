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
        ArgumentNullException.ThrowIfNull(command);

        var (camera, rtspSourceUrl, provisionedBy) = command;

        if (string.IsNullOrWhiteSpace(rtspSourceUrl))
        {
            return Result<StreamIdentifier, ProvisionStreamError>.Failure(
                new ProvisionStreamError.InvalidRtspSource("source URL is required"));
        }

        Option<Stream> existing = await streams
            .GetByCameraAsync(camera, cancellationToken)
            .ConfigureAwait(false);

        if (existing.HasValue)
        {
            Log.StreamAlreadyExists(logger, camera);
            return Result<StreamIdentifier, ProvisionStreamError>.Success(existing.Value.Id);
        }

        Stream stream = Stream.Provision(camera, provisionedBy, clock);
        streams.Add(stream);

        try
        {
            await rtsp.AddPathAsync(stream.Path, rtspSourceUrl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Log.PathRegistrationFailed(logger, ex, camera);
            return Result<StreamIdentifier, ProvisionStreamError>.Failure(
                new ProvisionStreamError.RtspGatewayUnavailable(ex.Message));
        }

        await streams.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.ProvisionedStream(logger, stream.Id, stream.Camera, stream.Path);

        return Result<StreamIdentifier, ProvisionStreamError>.Success(stream.Id);
    }
}
