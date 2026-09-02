using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// When a camera was registered, and by whom — spec 058.
///
/// <para>
/// Identity declares its own <c>Registration</c> with the same shape, and that
/// duplication is deliberate (§III, FR-002): each context's
/// <see cref="RegisteredAt"/> is its own type, and one shared composite would
/// either cross a context boundary or take a bare <c>DateTimeOffset</c> and
/// undo spec 057.
/// </para>
/// </summary>
public sealed record Registration(RegisteredAt At, OperatorIdentifier By) : IValueObject
{
    public static Registration From(RegisteredAt at, OperatorIdentifier by)
    {
        Ensure.That(at).IsNotNull();
        return new(at, by);
    }
}
