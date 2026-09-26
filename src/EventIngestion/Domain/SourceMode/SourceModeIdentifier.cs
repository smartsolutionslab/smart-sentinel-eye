using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode;

/// <summary>
/// Stable identifier for a <see cref="SourceMode"/> declaration (ADR-0090).
/// A surrogate required by <see cref="Shared.Kernel.AggregateRoot{TIdentifier}"/>,
/// not chosen for its own sake — the natural key is <c>(fab, source)</c>,
/// enforced by a unique index (spec.md FR-002, plan.md §2).
/// </summary>
public readonly record struct SourceModeIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<SourceModeIdentifier>
{
    public static SourceModeIdentifier New() => new(Guid.CreateVersion7());

    public static SourceModeIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public static implicit operator Guid(SourceModeIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(SourceModeIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(SourceModeIdentifier left, SourceModeIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(SourceModeIdentifier left, SourceModeIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(SourceModeIdentifier left, SourceModeIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(SourceModeIdentifier left, SourceModeIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
