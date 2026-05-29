namespace SmartSentinelEye.Shared.Contracts.SystemVariables;

/// <summary>
/// Integration event published when a system variable's value changes
/// (admin sets it explicitly in v1; spec 006 adds event-driven sources
/// without modifying this contract).
///
/// <para>
/// <c>Value</c> is the culture-invariant wire string per FR-007:
/// </para>
/// <list type="bullet">
/// <item><c>Type == "String"</c>: the raw text, as-typed.</item>
/// <item><c>Type == "Number"</c>: invariant decimal, e.g. <c>82.4</c>.</item>
/// <item><c>Type == "Boolean"</c>: <c>"true"</c> or <c>"false"</c>.</item>
/// </list>
/// </summary>
public sealed record SystemVariableValueChangedV1(
    Guid Variable,
    string Name,
    string Type,
    string Value,
    DateTimeOffset ChangedAt,
    Guid ChangedBy,
    EventMetadata Metadata) : IIntegrationEvent;
