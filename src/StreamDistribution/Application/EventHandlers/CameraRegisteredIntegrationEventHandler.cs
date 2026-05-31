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
public sealed class CameraRegisteredIntegrationEventHandler(
    ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>> handler,
    ILogger<CameraRegisteredIntegrationEventHandler> logger)
{
    public async Task Handle(CameraRegisteredV1 message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        CameraIdentifier camera = CameraIdentifier.From(message.Camera);
        OperatorIdentifier provisionedBy = OperatorIdentifier.From(message.RegisteredBy);

        ProvisionStreamCommand command = new(
            Camera: camera,
            RtspSourceUrl: message.Url,
            ProvisionedBy: provisionedBy);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Provision attempt failed for camera {Camera}: {Code} {Message}.",
                camera, result.Error.Code, result.Error.Message);
            // Wolverine treats an exception as a retry signal. Failures here
            // (e.g. RtspGatewayUnavailable) are transient — re-throw so the
            // outbox re-delivers after MediaMTX recovers.
            throw new InvalidOperationException(
                $"ProvisionStreamCommand failed for camera {camera}: {result.Error.Code}");
        }
    }
}
