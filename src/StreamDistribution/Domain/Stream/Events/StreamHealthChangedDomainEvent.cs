using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

/// <summary>
/// In-process domain event raised when a Stream transitions between health
/// states. Translated to <c>StreamHealthChangedV1</c> by the Application
/// layer and published via the Wolverine outbox (ADR-0040 + ADR-0088).
/// <c>Error</c> is populated for transitions into Degraded or Offline.
/// </summary>
public sealed record StreamHealthChangedDomainEvent(
    StreamIdentifier Stream,
    CameraIdentifier Camera,
    StreamState FromState,
    StreamState ToState,
    DateTimeOffset ChangedAt,
    Option<string> Error) : IDomainEvent;
