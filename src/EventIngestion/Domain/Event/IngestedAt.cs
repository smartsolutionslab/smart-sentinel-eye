using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Wall-clock moment at which the EventIngestion edge accepted the
/// event (spec 006 FR-003). Server-minted from <see cref="IClock"/>;
/// always UTC.
/// </summary>
public sealed record IngestedAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>
{
    public static IngestedAt From(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to the underlying <see cref="DateTimeOffset"/> so EF Core
    /// can translate range comparisons and ordering on the value-converted
    /// column in the read API (<c>e.IngestedAt &lt; x</c> maps to the
    /// <c>ingested_at</c> column; member access on <c>e.IngestedAt.Value</c>
    /// does not translate).
    /// </summary>
    public static implicit operator DateTimeOffset(IngestedAt ingestedAt) => ingestedAt.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
