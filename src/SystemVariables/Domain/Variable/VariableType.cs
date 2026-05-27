using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Declared type of a system variable, immutable post-creation
/// (spec 005 FR-002 + FR-003). Three cases match the wire shape on
/// <c>SystemVariableValueChangedV1.Type</c>.
/// </summary>
public sealed record VariableType(string Value) : IValueObject<string>
{
    // CA1720 fires on the static names because they collide with the
    // System.String/Boolean type identifiers. Suppressed deliberately:
    // the values mirror the wire-string used in V1 integration events
    // (`SystemVariableValueChangedV1.Type`); renaming would break that
    // round-trip symmetry.
#pragma warning disable CA1720
    public static VariableType String { get; } = new("String");

    public static VariableType Number { get; } = new("Number");

    public static VariableType Boolean { get; } = new("Boolean");
#pragma warning restore CA1720

    public static VariableType From(string value) =>
        value switch
        {
            "String" => String,
            "Number" => Number,
            "Boolean" => Boolean,
            _ => throw new ArgumentException(
                $"Unknown VariableType '{value}'. Expected: String | Number | Boolean.",
                nameof(value)),
        };

    public sealed override string ToString() => Value;
}
