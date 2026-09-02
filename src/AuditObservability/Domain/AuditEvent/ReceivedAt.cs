using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// When this context received the event off the bus.
///
/// <para>
/// Normalized to UTC on construction and validated no further, following
/// <c>EventIngestion</c>'s <c>OccurredAt</c> and <c>IngestedAt</c>.
/// </para>
/// </summary>
public sealed record ReceivedAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>, IComparable<ReceivedAt>
{
    /// <summary>
    /// Instants are ordered, and /nothing/ orders a value object for free:
    /// Comparer<T>.Default throws "At least one object must implement
    /// IComparable" the moment a list of these is sorted in memory. EF hides it
    /// by translating OrderBy into SQL, so the gap only shows against a fake.
    /// </summary>
    public int CompareTo(ReceivedAt? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(ReceivedAt left, ReceivedAt right) =>
        Comparer<ReceivedAt>.Default.Compare(left, right) < 0;

    public static bool operator >(ReceivedAt left, ReceivedAt right) =>
        Comparer<ReceivedAt>.Default.Compare(left, right) > 0;

    public static bool operator <=(ReceivedAt left, ReceivedAt right) =>
        Comparer<ReceivedAt>.Default.Compare(left, right) <= 0;

    public static bool operator >=(ReceivedAt left, ReceivedAt right) =>
        Comparer<ReceivedAt>.Default.Compare(left, right) >= 0;

    public static ReceivedAt From(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to <see cref="DateTimeOffset"/> so EF Core can translate
    /// range comparisons and ordering on the value-converted column. Member
    /// access (<c>x.ReceivedAt.Value</c>) does not translate and falls back to
    /// client evaluation, which passes tests while scanning the table.
    /// </summary>
    public static implicit operator DateTimeOffset(ReceivedAt value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
