using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber translating the cross-context
/// <c>CameraAddressChangedV1</c> into a <see cref="RepointStreamCommand"/>
/// (spec 029 FR-013). Mirrors
/// <see cref="CameraRetiredIntegrationEventHandler"/>; the queue is namespaced
/// per ADR-0088's per-module queue isolation.
/// </summary>
/// <remarks>
/// No fab check, for the same reason retirement needs none: this looks up a
/// stream that already exists by its camera, and a camera cannot change fab
/// (spec 015 FR-004), so the stream's fab is already its camera's. Only the
/// handler that <em>creates</em> a stream has to care.
/// </remarks>
public sealed class CameraAddressChangedIntegrationEventHandler(
    ICommandHandler<RepointStreamCommand, Result<Option<StreamIdentifier>, RepointStreamError>> handler,
    ILogger<CameraAddressChangedIntegrationEventHandler> logger)
{
    public async Task Handle(CameraAddressChangedV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (cameraId, _, _, url, _, _, _) = message;

        CameraIdentifier camera = CameraIdentifier.From(cameraId);

        Result<Option<StreamIdentifier>, RepointStreamError> result =
            await handler.HandleAsync(new RepointStreamCommand(camera, url), cancellationToken);

        if (result.IsFailure)
        {
            logger.RepointAttemptFailed(camera, result.Error.Code, result.Error.Message);

            // Wolverine treats an exception as a retry signal. The gateway
            // failure is transient and the aggregate already holds the new
            // address, so the retry finishes the re-point. An invalid source is
            // not transient, but it cannot occur without CameraCatalog having
            // accepted an address this context rejects — one of the two copies
            // of the grammar having drifted, which is worth surfacing loudly.
            throw new InvalidOperationException(
                $"RepointStreamCommand failed for camera {camera}: {result.Error.Code}");
        }
    }
}
