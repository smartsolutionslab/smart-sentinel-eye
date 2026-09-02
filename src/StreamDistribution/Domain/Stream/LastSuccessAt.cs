using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// When the stream was last observed healthy, where it has been.
///
/// <para>
/// Normalized to UTC on construction and validated no further, following
/// <c>EventIngestion</c>'s <c>OccurredAt</c> and <c>IngestedAt</c>.
/// </para>
/// </summary>
public sealed record LastSuccessAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>, IComparable<LastSuccessAt>
{
    /// <summary>
    /// Instants are ordered, and /nothing/ orders a value object for free:
    /// Comparer<T>.Default throws "At least one object must implement
    /// IComparable" the moment a list of these is sorted in memory. EF hides it
    /// by translating OrderBy into SQL, so the gap only shows against a fake.
    /// </summary>
    public int CompareTo(LastSuccessAt? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(LastSuccessAt left, LastSuccessAt right) =>
        Comparer<LastSuccessAt>.Default.Compare(left, right) < 0;

    public static bool operator >(LastSuccessAt left, LastSuccessAt right) =>
        Comparer<LastSuccessAt>.Default.Compare(left, right) > 0;

    public static bool operator <=(LastSuccessAt left, LastSuccessAt right) =>
        Comparer<LastSuccessAt>.Default.Compare(left, right) <= 0;

    public static bool operator >=(LastSuccessAt left, LastSuccessAt right) =>
        Comparer<LastSuccessAt>.Default.Compare(left, right) >= 0;

    public static LastSuccessAt From(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to <see cref="DateTimeOffset"/> so EF Core can translate
    /// range comparisons and ordering on the value-converted column. Member
    /// access (<c>x.LastSuccessAt.Value</c>) does not translate and falls back to
    /// client evaluation, which passes tests while scanning the table.
    /// </summary>
    public static implicit operator DateTimeOffset(LastSuccessAt value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
