using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// When the row reached the database, stamped by the database.
///
/// <para>
/// Normalized to UTC on construction and validated no further, following
/// <c>EventIngestion</c>'s <c>OccurredAt</c> and <c>IngestedAt</c>.
/// </para>
/// </summary>
public sealed record WrittenAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>, IComparable<WrittenAt>
{
    /// <summary>
    /// Instants are ordered, and /nothing/ orders a value object for free:
    /// Comparer<T>.Default throws "At least one object must implement
    /// IComparable" the moment a list of these is sorted in memory. EF hides it
    /// by translating OrderBy into SQL, so the gap only shows against a fake.
    /// </summary>
    public int CompareTo(WrittenAt? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(WrittenAt left, WrittenAt right) =>
        Comparer<WrittenAt>.Default.Compare(left, right) < 0;

    public static bool operator >(WrittenAt left, WrittenAt right) =>
        Comparer<WrittenAt>.Default.Compare(left, right) > 0;

    public static bool operator <=(WrittenAt left, WrittenAt right) =>
        Comparer<WrittenAt>.Default.Compare(left, right) <= 0;

    public static bool operator >=(WrittenAt left, WrittenAt right) =>
        Comparer<WrittenAt>.Default.Compare(left, right) >= 0;

    public static WrittenAt From(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to <see cref="DateTimeOffset"/> so EF Core can translate
    /// range comparisons and ordering on the value-converted column. Member
    /// access (<c>x.WrittenAt.Value</c>) does not translate and falls back to
    /// client evaluation, which passes tests while scanning the table.
    /// </summary>
    public static implicit operator DateTimeOffset(WrittenAt value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
