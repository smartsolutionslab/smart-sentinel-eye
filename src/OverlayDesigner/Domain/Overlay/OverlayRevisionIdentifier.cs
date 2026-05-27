using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Per-revision identifier, distinct from the chain's
/// <see cref="OverlayIdentifier"/>.
/// </summary>
public readonly record struct OverlayRevisionIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static OverlayRevisionIdentifier New() => new(Guid.CreateVersion7());

    public static OverlayRevisionIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OverlayRevisionIdentifier cannot be empty.", nameof(value))
            : new OverlayRevisionIdentifier(value);

    public override string ToString() => Value.ToString();
}
