using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that translates the cross-context
/// <c>CameraRetiredV1</c> integration event into a
/// <see cref="RetireStreamCommand"/> (spec 028 FR-008). Mirrors
/// <see cref="CameraRegisteredIntegrationEventHandler"/>; the queue is
/// namespaced per ADR-0088's per-module queue isolation.
///
/// <para>
/// No fab check here, unlike the registered handler. That one needs a fab
/// because it <em>creates</em> a stream and guessing wrong would put a camera's
/// video in front of the wrong plant's operators. This one looks up a stream
/// that already exists by its camera, and a camera cannot change fab (spec 015
/// FR-004), so the stream's fab is already whatever its camera's was. A camera
/// registered without a resolvable fab never got a stream at all, which the
/// command reports as <c>None</c> rather than a failure.
/// </para>
/// </summary>
public sealed class CameraRetiredIntegrationEventHandler(
    ICommandHandler<RetireStreamCommand, Result<Option<StreamIdentifier>, RetireStreamError>> handler,
    ILogger<CameraRetiredIntegrationEventHandler> logger)
{
    public async Task Handle(CameraRetiredV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (cameraId, _, _, _, _, _) = message;

        CameraIdentifier camera = CameraIdentifier.From(cameraId);

        RetireStreamCommand command = new(camera);

        Result<Option<StreamIdentifier>, RetireStreamError> result =
            await handler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            logger.RetireAttemptFailed(camera, result.Error.Code, result.Error.Message);

            // Wolverine treats an exception as a retry signal. The only failure
            // this command reports is the SFU being unreachable, and by then the
            // stream is already terminal — so the retry finishes the teardown
            // rather than redoing the retirement.
            throw new InvalidOperationException(
                $"RetireStreamCommand failed for camera {camera}: {result.Error.Code}");
        }
    }
}
