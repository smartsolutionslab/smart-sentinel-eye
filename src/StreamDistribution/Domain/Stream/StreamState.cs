using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Stream lifecycle state per spec 002 FR-004. The Stream aggregate enforces
/// the legal transitions between these values:
///
///   Provisioning -> Healthy (first frame decoded)
///   Provisioning -> Degraded (no frame within ~10 s)
///   Healthy      -> Degraded (10 s without a frame)
///   Degraded     -> Healthy  (3 consecutive frames)
///   Degraded     -> Offline  (5 min stuck in Degraded)
///   Offline      -> Healthy  (3 consecutive frames)
///   any          -> Retired  (its camera was retired; spec 028 FR-008)
///
/// <para>
/// <see cref="Retired"/> is the first terminal value here — every other state
/// is one the health watcher can move a stream out of. That is why the watcher
/// has to exclude it from the sweep rather than merely be able to report it:
/// since #1801 the watcher announces every health change, so a retired stream
/// still being probed would announce forever about hardware that is gone.
/// </para>
/// </summary>
public sealed record StreamState(string Value) : IValueObject<string>
{
    public static StreamState Provisioning { get; } = new("Provisioning");

    public static StreamState Healthy { get; } = new("Healthy");

    public static StreamState Degraded { get; } = new("Degraded");

    public static StreamState Offline { get; } = new("Offline");

    public static StreamState Retired { get; } = new("Retired");

    public static StreamState From(string value) =>
        value switch
        {
            "Provisioning" => Provisioning,
            "Healthy" => Healthy,
            "Degraded" => Degraded,
            "Offline" => Offline,
            "Retired" => Retired,
            _ => throw new ArgumentException($"Unknown StreamState '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
