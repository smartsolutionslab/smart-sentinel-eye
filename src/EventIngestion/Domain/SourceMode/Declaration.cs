using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode;

/// <summary>
/// Who declared or last changed a <see cref="SourceMode"/>, and when — spec
/// 058's travel-together composite, mirroring
/// <c>Domain/RegisteredEventType/Registration.cs</c>.
/// </summary>
public sealed record Declaration(DeclaredAt DeclaredAt, OperatorIdentifier DeclaredBy) : IValueObject
{
    public static Declaration From(DeclaredAt declaredAt, OperatorIdentifier declaredBy)
    {
        Ensure.That(declaredAt).IsNotNull();
        return new(declaredAt, declaredBy);
    }
}
