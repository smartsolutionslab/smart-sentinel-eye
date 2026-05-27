namespace SmartSentinelEye.SystemVariables.Application.DTOs;

/// <summary>
/// Read-side projection of a system variable returned by
/// <c>GET /system-variables/{name}</c> and embedded in the list
/// response. Wire-string for the value per FR-007; <c>null</c> when
/// the variable is <c>Unset</c>.
/// </summary>
public sealed record VariableDto(
    Guid VariableIdentifier,
    string Name,
    string Type,
    string State,
    string? Value,
    string? TruthyLabel,
    string? FalsyLabel,
    DateTimeOffset CreatedAt,
    Guid CreatedBy);
