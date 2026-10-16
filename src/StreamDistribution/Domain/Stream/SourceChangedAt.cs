using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// When CameraCatalog made the address change a stream last applied (spec 385).
/// Not when this context handled it: redelivery and retries reorder handling,
/// never the change itself.
///
/// <para>
/// Normalized to UTC on construction and validated no further, following
/// <see cref="LastSuccessAt"/>.
/// </para>
/// </summary>
public sealed record SourceChangedAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>, IComparable<SourceChangedAt>
{
    /// <summary>
    /// Instants are ordered, and /nothing/ orders a value object for free:
    /// Comparer<T>.Default throws "At least one object must implement
    /// IComparable" the moment a list of these is sorted in memory. EF hides it
    /// by translating OrderBy into SQL, so the gap only shows against a fake.
    /// </summary>
    public int CompareTo(SourceChangedAt? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(SourceChangedAt left, SourceChangedAt right) =>
        Comparer<SourceChangedAt>.Default.Compare(left, right) < 0;

    public static bool operator >(SourceChangedAt left, SourceChangedAt right) =>
        Comparer<SourceChangedAt>.Default.Compare(left, right) > 0;

    public static bool operator <=(SourceChangedAt left, SourceChangedAt right) =>
        Comparer<SourceChangedAt>.Default.Compare(left, right) <= 0;

    public static bool operator >=(SourceChangedAt left, SourceChangedAt right) =>
        Comparer<SourceChangedAt>.Default.Compare(left, right) >= 0;

    public static SourceChangedAt From(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to <see cref="DateTimeOffset"/> so EF Core can translate
    /// range comparisons and ordering on the value-converted column. Member
    /// access (<c>x.SourceChangedAt.Value</c>) does not translate and falls back to
    /// client evaluation, which passes tests while scanning the table.
    /// </summary>
    public static implicit operator DateTimeOffset(SourceChangedAt value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
