using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Lifecycle state of a system variable (spec 005 FR-005). Two-state
/// machine: <c>Defined</c> (may hold a value or be <c>Unset</c>) →
/// <c>Archived</c> (terminal; name freed for re-use).
/// </summary>
public sealed record VariableState(string Value) : IValueObject<string>
{
    public static VariableState Defined { get; } = new("Defined");

    public static VariableState Archived { get; } = new("Archived");

    public static VariableState From(string value) =>
        value switch
        {
            "Defined" => Defined,
            "Archived" => Archived,
            _ => throw new ArgumentException(
                $"Unknown VariableState '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
