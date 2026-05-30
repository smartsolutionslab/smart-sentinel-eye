using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Rule lifecycle state (spec 007 FR-003). Three singletons.
/// Allowed transitions: <c>Draft → Active</c> (Publish),
/// <c>Draft → Archived</c> (cancel), <c>Active → Archived</c>
/// (Archive). No other transitions are valid.
/// </summary>
public sealed record RuleState(string Value) : IValueObject<string>
{
    public static RuleState Draft { get; } = new("Draft");

    public static RuleState Active { get; } = new("Active");

    public static RuleState Archived { get; } = new("Archived");

    public static RuleState From(string value) =>
        value switch
        {
            "Draft" => Draft,
            "Active" => Active,
            "Archived" => Archived,
            _ => throw new ArgumentException($"Unknown RuleState '{value}'. Expected: Draft | Active | Archived.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
