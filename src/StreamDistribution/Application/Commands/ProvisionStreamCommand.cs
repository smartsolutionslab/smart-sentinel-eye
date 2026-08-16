using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Provisions a stream for a camera. Idempotent: if a stream already exists
/// for the camera, returns the existing identifier without re-registering
/// the MediaMTX path (FR-011).
///
/// <para>
/// <see cref="Fab"/> comes from the camera-registered event, never from a
/// caller (spec 016 FR-003). No operator-driven write reaches this command,
/// so there is nowhere a fab could be named.
/// </para>
/// </summary>
public sealed record ProvisionStreamCommand(
    FabIdentifier Fab,
    CameraIdentifier Camera,
    string RtspSourceUrl,
    OperatorIdentifier ProvisionedBy)
    : ICommand<Result<StreamIdentifier, ProvisionStreamError>>;
