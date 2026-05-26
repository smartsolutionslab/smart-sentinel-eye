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
/// </summary>
public sealed record StreamState(string Value) : IValueObject<string>
{
    public static StreamState Provisioning { get; } = new("Provisioning");

    public static StreamState Healthy { get; } = new("Healthy");

    public static StreamState Degraded { get; } = new("Degraded");

    public static StreamState Offline { get; } = new("Offline");

    public static StreamState From(string value) =>
        value switch
        {
            "Provisioning" => Provisioning,
            "Healthy" => Healthy,
            "Degraded" => Degraded,
            "Offline" => Offline,
            _ => throw new ArgumentException($"Unknown StreamState '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
