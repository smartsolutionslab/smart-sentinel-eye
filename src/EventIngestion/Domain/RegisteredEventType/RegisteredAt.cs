using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

/// <summary>
/// When a <see cref="RegisteredEventType"/> was registered.
///
/// <para>
/// Shares a name with <c>Domain/WebhookIntegration/RegisteredAt.cs</c> —
/// different namespaces, different aggregates, same concept. ADR-0092's
/// per-aggregate folder is exactly the arrangement that makes this correct
/// rather than a collision (plan.md §2). Do not extract a shared one.
/// </para>
/// </summary>
public sealed record RegisteredAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>, IComparable<RegisteredAt>
{
    /// <summary>
    /// Instants are ordered, and nothing orders a value object for free:
    /// Comparer&lt;T&gt;.Default throws the moment a list of these is sorted
    /// in memory (mirrors <c>WebhookIntegration/RegisteredAt.cs</c>).
    /// </summary>
    public int CompareTo(RegisteredAt? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(RegisteredAt left, RegisteredAt right) =>
        Comparer<RegisteredAt>.Default.Compare(left, right) < 0;

    public static bool operator >(RegisteredAt left, RegisteredAt right) =>
        Comparer<RegisteredAt>.Default.Compare(left, right) > 0;

    public static bool operator <=(RegisteredAt left, RegisteredAt right) =>
        Comparer<RegisteredAt>.Default.Compare(left, right) <= 0;

    public static bool operator >=(RegisteredAt left, RegisteredAt right) =>
        Comparer<RegisteredAt>.Default.Compare(left, right) >= 0;

    public static RegisteredAt From(DateTimeOffset value) => new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to <see cref="DateTimeOffset"/> so EF Core can
    /// translate range comparisons and ordering on the value-converted
    /// column. Member access (<c>x.RegisteredAt.Value</c>) does not
    /// translate and falls back to client evaluation.
    /// </summary>
    public static implicit operator DateTimeOffset(RegisteredAt value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
