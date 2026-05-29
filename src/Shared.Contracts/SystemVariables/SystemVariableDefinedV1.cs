namespace SmartSentinelEye.Shared.Contracts.SystemVariables;

/// <summary>
/// Integration event published when a system variable is first defined
/// (spec 005). Versioned per ADR-0073; subscribers consume via
/// Wolverine RabbitMQ with per-module queue isolation (ADR-0088).
///
/// Primitive types only at the wire boundary per ADR-0040.
/// <c>Type</c> is the wire string for the variable's declared type:
/// <c>"String"</c>, <c>"Number"</c>, or <c>"Boolean"</c>.
/// </summary>
public sealed record SystemVariableDefinedV1(
    Guid Variable,
    string Name,
    string Type,
    DateTimeOffset DefinedAt,
    Guid DefinedBy,
    EventMetadata Metadata) : IIntegrationEvent;
