using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that translates the cross-context
/// <c>CameraRegisteredV1</c> integration event into a
/// <see cref="ProvisionStreamCommand"/>. The command handler is itself
/// idempotent on the camera identifier, so re-delivery via the outbox is
/// safe (FR-001 + FR-011). The Wolverine queue is namespaced
/// <c>stream-distribution.SmartSentinelEye.Shared.Contracts.CameraCatalog.CameraRegisteredV1</c>
/// per ADR-0088's per-module queue isolation.
/// </summary>
public sealed class CameraRegisteredIntegrationEventHandler(ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>> handler, ILogger<CameraRegisteredIntegrationEventHandler> logger)
{
    public async Task Handle(CameraRegisteredV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (cameraId, _, url, _, registeredBy, metadata) = message;

        CameraIdentifier camera = CameraIdentifier.From(cameraId);

        if (string.IsNullOrWhiteSpace(metadata.Fab))
        {
            // FR-004. Guessing a fab would put a camera's video in front of the
            // wrong plant's operators, so the stream is not provisioned at all
            // — and the drop is recorded, because silence here is
            // indistinguishable from success. A malformed fab is a different
            // case and deliberately still throws: CameraCatalog accepted it
            // under the identical grammar, so it cannot occur without one of
            // the two copies having drifted.
            logger.CameraRegisteredWithoutFab(camera);
            return;
        }

        OperatorIdentifier provisionedBy = OperatorIdentifier.From(registeredBy);

        ProvisionStreamCommand command = new(
            Fab: FabIdentifier.From(metadata.Fab),
            Camera: camera,
            RtspSourceUrl: url,
            ProvisionedBy: provisionedBy);

        Result<StreamIdentifier, ProvisionStreamError> result = await handler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            logger.ProvisionAttemptFailed(camera, result.Error.Code, result.Error.Message);
            // Wolverine treats an exception as a retry signal. Failures here
            // (e.g. RtspGatewayUnavailable) are transient — re-throw so the
            // outbox re-delivers after MediaMTX recovers.
            throw new InvalidOperationException($"ProvisionStreamCommand failed for camera {camera}: {result.Error.Code}");
        }
    }
}
