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

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
