using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode;

/// <summary>
/// When a <see cref="SourceMode"/> was last declared or changed. Shares a
/// name and shape with <c>Domain/RegisteredEventType/RegisteredAt.cs</c> —
/// different namespaces, different aggregates, same concept. ADR-0092's
/// per-aggregate folder is what makes this correct rather than a collision.
/// Do not extract a shared one.
///
/// </summary>
public sealed record DeclaredAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>, IComparable<DeclaredAt>
{
    /// <summary>
    /// Instants are ordered, and nothing orders a value object for free:
    /// Comparer&lt;T&gt;.Default throws the moment a list of these is sorted
    /// in memory (mirrors <c>RegisteredEventType/RegisteredAt.cs</c>).
    /// </summary>
    public int CompareTo(DeclaredAt? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(DeclaredAt left, DeclaredAt right) =>
        Comparer<DeclaredAt>.Default.Compare(left, right) < 0;

    public static bool operator >(DeclaredAt left, DeclaredAt right) =>
        Comparer<DeclaredAt>.Default.Compare(left, right) > 0;

    public static bool operator <=(DeclaredAt left, DeclaredAt right) =>
        Comparer<DeclaredAt>.Default.Compare(left, right) <= 0;

    public static bool operator >=(DeclaredAt left, DeclaredAt right) =>
        Comparer<DeclaredAt>.Default.Compare(left, right) >= 0;

    public static DeclaredAt From(DateTimeOffset value) => new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to <see cref="DateTimeOffset"/> so EF Core can
    /// translate range comparisons and ordering on the value-converted
    /// column. Member access (<c>x.DeclaredAt.Value</c>) does not translate
    /// and falls back to client evaluation.
    /// </summary>
    public static implicit operator DateTimeOffset(DeclaredAt value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
