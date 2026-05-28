using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Stable, sortable identifier for an automation rule (ADR-0090).
/// </summary>
public readonly record struct RuleIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static RuleIdentifier New() => new(Guid.CreateVersion7());

    public static RuleIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("RuleIdentifier cannot be empty.", nameof(value))
            : new RuleIdentifier(value);

    public override string ToString() => Value.ToString();
}
