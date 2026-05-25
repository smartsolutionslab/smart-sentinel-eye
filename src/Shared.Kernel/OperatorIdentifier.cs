using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Identifies the human operator who performed an action. Cross-context;
/// lives in Shared.Kernel per ADR-0044's shared-kernel exception list.
/// </summary>
public readonly record struct OperatorIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static OperatorIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OperatorIdentifier cannot be empty.", nameof(value))
            : new OperatorIdentifier(value);

    public override string ToString() => Value.ToString();
}
