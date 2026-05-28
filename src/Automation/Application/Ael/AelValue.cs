namespace SmartSentinelEye.Automation.Application.Ael;

/// <summary>
/// Runtime value union for the Automation Expression Language
/// (ADR-0099 / spec 007 FR-013). Each variant carries a primitive
/// CLR type; arithmetic follows the int → decimal promotion rules
/// described in <see cref="AelInterpreter"/>.
/// </summary>
public abstract record AelValue
{
    public sealed record IntValue(long Value) : AelValue;

    public sealed record DecimalValue(decimal Value) : AelValue;

    public sealed record StringValue(string Value) : AelValue;

    public sealed record BoolValue(bool Value) : AelValue;

    /// <summary>
    /// Result of a missing field access (<c>$.payload.missing</c>).
    /// Propagates through most operations; comparisons with
    /// <see cref="NullValue"/> return <c>false</c> except <c>== null</c>
    /// vs <c>!= null</c> (not in v1 — null literal not yet supported).
    /// </summary>
    public sealed record NullValue : AelValue
    {
        public static NullValue Instance { get; } = new();
    }

    // S3060 fires because the type-switch lives on the base; the
    // closed discriminated union is the whole point of having a
    // single render-via-pattern-match method (mirrors
    // SystemVariables/Domain/Variable/VariableValue.cs).
#pragma warning disable S3060
    public bool IsTruthy() => this switch
    {
        BoolValue b => b.Value,
        _ => false,
    };

    /// <summary>
    /// Wire representation used when the value is the result of a
    /// <c>SetVariableValue</c> action. Decimals round-trip via
    /// <c>InvariantCulture</c>; bools as <c>"true"</c>/<c>"false"</c>.
    /// </summary>
    public string ToWireString() => this switch
    {
        IntValue i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DecimalValue d => d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StringValue s => s.Value,
        BoolValue b => b.Value ? "true" : "false",
        NullValue => string.Empty,
        _ => throw new InvalidOperationException($"Unhandled AelValue case: {GetType().Name}"),
    };
#pragma warning restore S3060
}
