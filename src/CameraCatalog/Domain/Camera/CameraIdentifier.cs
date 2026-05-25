using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// Stable, sortable, client-generatable identifier for a registered camera
/// (ADR-0039 + ADR-0090). Backed by Guid v7 so the timestamp portion gives
/// us natural index ordering in Postgres.
/// </summary>
public readonly record struct CameraIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static CameraIdentifier New() => new(Guid.CreateVersion7());

    public static CameraIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CameraIdentifier cannot be empty.", nameof(value))
            : new CameraIdentifier(value);

    public override string ToString() => Value.ToString();
}
