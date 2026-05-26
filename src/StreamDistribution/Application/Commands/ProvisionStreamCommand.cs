using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Provisions a stream for a camera. Idempotent: if a stream already exists
/// for the camera, returns the existing identifier without re-registering
/// the MediaMTX path (FR-011).
/// </summary>
public sealed record ProvisionStreamCommand(
    CameraIdentifier Camera,
    string RtspSourceUrl,
    OperatorIdentifier ProvisionedBy)
    : ICommand<Result<StreamIdentifier, ProvisionStreamError>>;
