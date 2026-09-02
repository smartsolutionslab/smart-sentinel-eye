using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// When the variable was defined.
///
/// <para>
/// Normalized to UTC on construction and validated no further, following
/// <c>EventIngestion</c>'s <c>OccurredAt</c> and <c>IngestedAt</c>.
/// </para>
/// </summary>
public sealed record CreatedAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>, IComparable<CreatedAt>
{
    /// <summary>
    /// Instants are ordered, and /nothing/ orders a value object for free:
    /// Comparer<T>.Default throws "At least one object must implement
    /// IComparable" the moment a list of these is sorted in memory. EF hides it
    /// by translating OrderBy into SQL, so the gap only shows against a fake.
    /// </summary>
    public int CompareTo(CreatedAt? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(CreatedAt left, CreatedAt right) =>
        Comparer<CreatedAt>.Default.Compare(left, right) < 0;

    public static bool operator >(CreatedAt left, CreatedAt right) =>
        Comparer<CreatedAt>.Default.Compare(left, right) > 0;

    public static bool operator <=(CreatedAt left, CreatedAt right) =>
        Comparer<CreatedAt>.Default.Compare(left, right) <= 0;

    public static bool operator >=(CreatedAt left, CreatedAt right) =>
        Comparer<CreatedAt>.Default.Compare(left, right) >= 0;

    public static CreatedAt From(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to <see cref="DateTimeOffset"/> so EF Core can translate
    /// range comparisons and ordering on the value-converted column. Member
    /// access (<c>x.CreatedAt.Value</c>) does not translate and falls back to
    /// client evaluation, which passes tests while scanning the table.
    /// </summary>
    public static implicit operator DateTimeOffset(CreatedAt value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
