using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

/// <summary>
/// Who registered a <see cref="RegisteredEventType"/>, and when — spec 058's
/// travel-together composite, mirroring <c>SystemVariables.Domain.Variable.Creation</c>.
/// </summary>
public sealed record Registration(RegisteredAt RegisteredAt, OperatorIdentifier RegisteredBy) : IValueObject
{
    public static Registration From(RegisteredAt registeredAt, OperatorIdentifier registeredBy)
    {
        Ensure.That(registeredAt).IsNotNull();
        return new(registeredAt, registeredBy);
    }
}
