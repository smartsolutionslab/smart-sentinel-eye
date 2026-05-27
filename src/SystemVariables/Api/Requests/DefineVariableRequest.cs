namespace SmartSentinelEye.SystemVariables.Api.Requests;

/// <summary>
/// POST /system-variables body. Type is the wire string per FR-007
/// (<c>"String" | "Number" | "Boolean"</c>). InitialValue is the
/// optional wire-string for the value; null means start <c>Unset</c>.
/// BooleanLabels (truthy/falsy) must be supplied iff Type=Boolean.
/// </summary>
public sealed record DefineVariableRequest(
    string Name,
    string Type,
    string? InitialValue,
    string? TruthyLabel,
    string? FalsyLabel);
