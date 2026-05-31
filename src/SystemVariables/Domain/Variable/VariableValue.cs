using System.Globalization;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Discriminated value-object union for a system variable's current
/// value (spec 005 FR-002 + FR-004 + FR-006 + FR-007). Four cases:
/// <list type="bullet">
/// <item><see cref="Unset"/> — never set / cleared on archive.</item>
/// <item><see cref="StringValue"/> — raw text, rendered verbatim.</item>
/// <item><see cref="NumberValue"/> — IEEE-754 double, rendered
///   culture-invariantly with no thousands separator.</item>
/// <item><see cref="BooleanValue"/> — rendered via
///   <see cref="BooleanLabels"/> from the owning variable.</item>
/// </list>
/// </summary>
public abstract record VariableValue : IValueObject
{
    private VariableValue() { }

    /// <summary>
    /// Materialises a typed value from the wire-string representation
    /// (spec 005 FR-007). Throws <see cref="ArgumentException"/> on
    /// type mismatch; the application layer maps it to a
    /// <c>VARIABLE_TYPE_MISMATCH</c> <see cref="Shared.Kernel.ApiError"/>.
    /// </summary>
    public static VariableValue From(VariableType type, string raw)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(raw);

        if (type == VariableType.String)
        {
            return new StringValue(raw);
        }
        if (type == VariableType.Number)
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                throw new ArgumentException(
                    $"'{raw}' is not a valid invariant-culture decimal for a Number variable.",
                    nameof(raw));
            }
            return new NumberValue(parsed);
        }
        if (type == VariableType.Boolean)
        {
            return raw switch
            {
                "true" => new BooleanValue(true),
                "false" => new BooleanValue(false),
                _ => throw new ArgumentException(
                    $"'{raw}' is not a valid Boolean literal; expected 'true' or 'false'.",
                    nameof(raw)),
            };
        }
        throw new ArgumentException($"Unknown VariableType '{type}'.", nameof(type));
    }

    /// <summary>
    /// Wire-string representation (spec 005 FR-007). Inverse of
    /// <see cref="From(VariableType, string)"/>.
    /// </summary>
    /// <remarks>
    /// S3060 is suppressed for this method and <see cref="Render"/>
    /// below — VariableValue is a closed discriminated union (sealed
    /// nested records) and pattern-match-in-base is the canonical C#
    /// shape; four near-empty per-subclass overrides would obscure
    /// the single-line cases.
    /// </remarks>
#pragma warning disable S3060
    public string ToWireString() => this switch
    {
        Unset => string.Empty,
        StringValue stringValue => stringValue.Value,
        NumberValue numberValue => numberValue.Value.ToString("G17", CultureInfo.InvariantCulture),
        BooleanValue booleanValue => booleanValue.Value ? "true" : "false",
        _ => throw new InvalidOperationException("Unreachable VariableValue case."),
    };

    /// <summary>
    /// Renders this value to its display string for overlay labels.
    /// Boolean uses the supplied <paramref name="booleanLabels"/>.
    /// </summary>
    /// <remarks>
    /// Caller must NOT invoke this on <see cref="Unset"/> — the resolver
    /// checks the state first and substitutes the literal placeholder.
    /// </remarks>
    public string Render(BooleanLabels booleanLabels) => this switch
    {
        StringValue stringValue => stringValue.Value,
        NumberValue numberValue => numberValue.Value.ToString(CultureInfo.InvariantCulture),
        BooleanValue booleanValue => booleanValue.Value ? booleanLabels.TruthyLabel : booleanLabels.FalsyLabel,
        Unset => throw new InvalidOperationException(
            "Unset cannot be rendered; resolver must check state first."),
        _ => throw new InvalidOperationException("Unreachable VariableValue case."),
    };
#pragma warning restore S3060

    public sealed record Unset : VariableValue
    {
        public static Unset Instance { get; } = new();
    }

    public sealed record StringValue(string Value) : VariableValue;

    public sealed record NumberValue(double Value) : VariableValue;

    public sealed record BooleanValue(bool Value) : VariableValue;
}
