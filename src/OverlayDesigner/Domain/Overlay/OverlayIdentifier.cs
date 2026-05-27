using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Stable, sortable, client-generatable identifier for a logical
/// Overlay chain (ADR-0039 + ADR-0090). The chain is the aggregate
/// boundary; every revision of the same logical overlay shares this
/// identifier.
/// </summary>
public readonly record struct OverlayIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static OverlayIdentifier New() => new(Guid.CreateVersion7());

    public static OverlayIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OverlayIdentifier cannot be empty.", nameof(value))
            : new OverlayIdentifier(value);

    public override string ToString() => Value.ToString();
}
