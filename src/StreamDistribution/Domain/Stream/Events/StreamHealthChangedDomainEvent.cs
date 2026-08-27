#nullable enable
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

/// <summary>
/// In-process domain event raised when a Stream transitions between health
/// states. Translated to <c>StreamHealthChangedV1</c> by the Application
/// layer and published via the Wolverine outbox (ADR-0040 + ADR-0088).
/// <c>Error</c> is populated for transitions into Degraded or Offline.
///
/// <para>
/// <c>Fab</c> rides along because the announcement is published from a
/// background loop, where there is no ambient fab to fall back on. Without it
/// the integration event reached audit with no fab at all, so a fab-scoped
/// audit query returned no stream-health row ever — and the check that was
/// supposed to prove a retired camera goes quiet returned zero whether or not
/// the watcher was announcing.
/// </para>
///
/// <para>
/// Nullable, like <see cref="Stream.Fab"/> itself: streams provisioned before
/// spec 016 have none yet and acquire it at runtime. A null here means the
/// stream genuinely has no fab, not that nobody passed one.
/// </para>
/// </summary>
public sealed record StreamHealthChangedDomainEvent(
    StreamIdentifier Stream,
    CameraIdentifier Camera,
    FabIdentifier? Fab,
    StreamState FromState,
    StreamState ToState,
    DateTimeOffset ChangedAt,
    string? Error) : IDomainEvent;
