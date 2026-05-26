using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Reference to a camera registered in the CameraCatalog bounded context.
/// Value-copy across contexts per ADR-0040: the Layout aggregate carries
/// the camera's identifier as a typed wrapper without project-referencing
/// CameraCatalog.Domain (forbidden by ADR-0027). Mirrors the same pattern
/// in StreamDistribution.Domain.
/// </summary>
public readonly record struct CameraIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static CameraIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CameraIdentifier cannot be empty.", nameof(value))
            : new CameraIdentifier(value);

    public override string ToString() => Value.ToString();
}
