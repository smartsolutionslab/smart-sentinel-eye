namespace SmartSentinelEye.Shared.Kernel.Primitives;

/// <summary>
/// An aggregate root's optimistic-concurrency version (ADR-0043 as amended by
/// ADR-0113), and the value a caller echoes back in <c>If-Match</c>.
///
/// <para>
/// <b>A <c>record</c>, and that is load-bearing rather than stylistic.</b> EF
/// Core derives the concurrency value comparer from the type's equality, and
/// compares the original against the current value through it. A <c>class</c>
/// without value equality would compare references, so every check would find a
/// difference where there is none — or none where there is one — and stale
/// writes would pass silently. Nothing would fail to compile, and no unit test
/// that fakes the repository would notice.
/// </para>
///
/// <para>
/// Admissible in <c>Shared.Kernel</c>, which holds no domain, on the same
/// footing as <c>Result&lt;T, E&gt;</c> and <c>Option&lt;T&gt;</c>: a version is
/// a language-level concept about persisted state, not vocabulary belonging to
/// any bounded context.
/// </para>
///
/// <para>
/// Deliberately not <c>IComparable</c>. Versions are only ever compared for
/// equality — a write is stale or it is not — and nothing in this codebase
/// orders them. The timestamp value objects needed ordering and the omission
/// showed up immediately as a failing sort; this type has no such caller.
/// </para>
/// </summary>
public sealed record AggregateVersion(int Value) : IValueObject<int>
{
    /// <summary>
    /// The version an aggregate carries before its first write.
    /// </summary>
    public static AggregateVersion Initial { get; } = new(0);

    public static AggregateVersion From(int value)
    {
        Ensure.That(value).AtLeast(0);

        return new AggregateVersion(value);
    }

    /// <summary>
    /// Implicit unwrap to <see cref="int"/> so EF Core can translate comparisons
    /// on the value-converted column, and so the many call sites that already
    /// hold an <c>int</c> keep reading naturally.
    /// </summary>
    public static implicit operator int(AggregateVersion version) => version.Value;

    public sealed override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
