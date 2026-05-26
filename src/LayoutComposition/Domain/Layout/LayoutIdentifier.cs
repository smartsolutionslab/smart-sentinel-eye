using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Stable, sortable, client-generatable identifier for a logical layout
/// chain (ADR-0039 + ADR-0090). The chain is the aggregate boundary; all
/// revisions of the same logical layout share this identifier.
/// </summary>
public readonly record struct LayoutIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static LayoutIdentifier New() => new(Guid.CreateVersion7());

    public static LayoutIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("LayoutIdentifier cannot be empty.", nameof(value))
            : new LayoutIdentifier(value);

    public override string ToString() => Value.ToString();
}
