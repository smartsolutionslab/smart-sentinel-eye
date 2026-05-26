using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Per-revision identifier, distinct from the chain's
/// <see cref="LayoutIdentifier"/>. Lets us address a specific revision
/// in the EF row mapping and over the wire.
/// </summary>
public readonly record struct LayoutRevisionIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static LayoutRevisionIdentifier New() => new(Guid.CreateVersion7());

    public static LayoutRevisionIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("LayoutRevisionIdentifier cannot be empty.", nameof(value))
            : new LayoutRevisionIdentifier(value);

    public override string ToString() => Value.ToString();
}
