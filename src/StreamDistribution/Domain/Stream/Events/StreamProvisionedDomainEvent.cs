using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

/// <summary>
/// In-process domain event raised when a Stream aggregate is provisioned.
/// Never crosses the bounded-context boundary; the Application layer logs it
/// for audit and observability. <see cref="StreamHealthChangedDomainEvent"/>
/// covers state transitions that downstream contexts care about.
/// </summary>
public sealed record StreamProvisionedDomainEvent(
    StreamIdentifier Stream,
    CameraIdentifier Camera,
    MediaMtxPath Path,
    DateTimeOffset ProvisionedAt,
    OperatorIdentifier ProvisionedBy) : IDomainEvent;
