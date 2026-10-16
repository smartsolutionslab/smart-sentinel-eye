using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application;

/// <summary>
/// Two stamps logged as one value, so a log method naming a camera, an origin
/// and both stamps stays within four parameters (ADR-0084).
/// </summary>
internal sealed record ChangedAtPair(
    SourceChangedAt Incoming,
    SourceChangedAt Counterpart)
{
    public override string ToString() => $"{Incoming}, {Counterpart}";
}
