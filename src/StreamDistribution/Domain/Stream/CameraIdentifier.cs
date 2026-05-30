using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Reference to a camera registered in the Camera Catalog bounded context.
/// Value-copy across contexts per ADR-0040: the Stream aggregate carries the
/// camera's identifier as a typed wrapper without project-referencing
/// CameraCatalog.Domain (forbidden by ADR-0027). The wire format on the
/// integration bus is a raw <see cref="Guid"/>; this type wraps it inside
/// StreamDistribution's domain.
/// </summary>
public readonly record struct CameraIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<CameraIdentifier>
{
    public static CameraIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CameraIdentifier cannot be empty.", nameof(value))
            : new CameraIdentifier(value);

    public static implicit operator Guid(CameraIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(CameraIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(CameraIdentifier left, CameraIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(CameraIdentifier left, CameraIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(CameraIdentifier left, CameraIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(CameraIdentifier left, CameraIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
