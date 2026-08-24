using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// Lifecycle status of a camera. Both values are reachable as of spec 028:
/// Decommissioned was reserved by spec 001 so the EF mapping would not need a
/// migration when a camera could finally reach it, and Camera.Retire is what
/// reaches it. Terminal — nothing leaves Decommissioned.
/// </summary>
public sealed record CameraStatus(string Value) : IValueObject<string>
{
    public static CameraStatus Registered { get; } = new("Registered");
    public static CameraStatus Decommissioned { get; } = new("Decommissioned");

    public static CameraStatus From(string value) =>
        value switch
        {
            "Registered" => Registered,
            "Decommissioned" => Decommissioned,
            _ => throw new ArgumentException($"Unknown CameraStatus '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
