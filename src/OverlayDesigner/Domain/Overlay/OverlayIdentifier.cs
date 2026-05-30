using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Stable, sortable, client-generatable identifier for a logical
/// Overlay chain (ADR-0039 + ADR-0090). The chain is the aggregate
/// boundary; every revision of the same logical overlay shares this
/// identifier.
/// </summary>
public readonly record struct OverlayIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<OverlayIdentifier>
{
    public static OverlayIdentifier New() => new(Guid.CreateVersion7());

    public static OverlayIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OverlayIdentifier cannot be empty.", nameof(value))
            : new OverlayIdentifier(value);

    public static implicit operator Guid(OverlayIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(OverlayIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
