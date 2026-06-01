using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class ProvisionStreamCommandHandler(IStreamRepository streams, IRtspGateway rtsp, IClock clock, ILogger<ProvisionStreamCommandHandler> logger)
    : ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>>
{
    public async Task<Result<StreamIdentifier, ProvisionStreamError>> HandleAsync(ProvisionStreamCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (camera, rtspSourceUrl, provisionedBy) = command;

        if (string.IsNullOrWhiteSpace(rtspSourceUrl))
        {
            return Result<StreamIdentifier, ProvisionStreamError>.Failure(new ProvisionStreamError.InvalidRtspSource("source URL is required"));
        }

        var existing = await streams.GetByCameraAsync(camera, cancellationToken);

        if (existing.HasValue)
        {
            logger.StreamAlreadyExists(camera);
            return Result<StreamIdentifier, ProvisionStreamError>.Success(existing.Value.Id);
        }

        Stream stream = Stream.Provision(camera, provisionedBy, clock);
        streams.Add(stream);

        try
        {
            await rtsp.AddPathAsync(stream.Path, rtspSourceUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.PathRegistrationFailed(ex, camera);
            return Result<StreamIdentifier, ProvisionStreamError>.Failure(new ProvisionStreamError.RtspGatewayUnavailable(ex.Message));
        }

        await streams.SaveAsync(cancellationToken);

        logger.ProvisionedStream(stream.Id, stream.Camera, stream.Path);

        return Result<StreamIdentifier, ProvisionStreamError>.Success(stream.Id);
    }
}
