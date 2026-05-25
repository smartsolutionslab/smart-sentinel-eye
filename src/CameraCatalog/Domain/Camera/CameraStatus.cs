using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// Lifecycle status of a camera. Only Registered is reachable in spec 001;
/// Decommissioned is reserved for a follow-up spec so the EF mapping does
/// not need to migrate when we add it.
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
