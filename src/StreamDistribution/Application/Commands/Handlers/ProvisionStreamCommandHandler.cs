using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class ProvisionStreamCommandHandler(
    IStreamRepository streams,
    IRtspGateway rtsp,
    IClock clock,
    ILogger<ProvisionStreamCommandHandler> log)
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
            log.LogInformation(
                "Stream already exists for camera {Camera}; skipping provision (idempotent).",
                camera);
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
            log.LogWarning(ex,
                "MediaMTX path registration failed for camera {Camera}.",
                camera);
            return Result<StreamIdentifier, ProvisionStreamError>.Failure(
                new ProvisionStreamError.RtspGatewayUnavailable(ex.Message));
        }

        await streams.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Provisioned stream {Stream} for camera {Camera} at path {Path}.",
            stream.Id, stream.Camera, stream.Path);

        return Result<StreamIdentifier, ProvisionStreamError>.Success(stream.Id);
    }
}
