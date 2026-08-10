namespace SmartSentinelEye.SystemVariables.Application.DTOs;

/// <summary>
/// Read-side projection of a system variable returned by
/// <c>GET /system-variables/{name}</c> and embedded in the list
/// response. Wire-string for the value per FR-007; <c>null</c> when
/// the variable is <c>Unset</c>.
///
/// <para>
/// <c>Fab</c> is on the wire because a multi-fab operator's listing can
/// otherwise hold two rows with the same name and nothing to tell them
/// apart (spec 014).
/// </para>
///
/// <para>
/// <c>Version</c> is the optimistic-concurrency version (ADR-0113),
/// echoed back via <c>If-Match</c> to mutate. The single-variable read
/// also returns it as an <c>ETag</c>; it is on the body so the list
/// endpoint hands every row a version without a per-row fetch.
/// </para>
/// </summary>
public sealed record VariableDto(
    Guid VariableIdentifier,
    int Version,
    string Fab,
    string Name,
    string Type,
    string State,
    string? Value,
    string? TruthyLabel,
    string? FalsyLabel,
    DateTimeOffset CreatedAt,
    Guid CreatedBy);
