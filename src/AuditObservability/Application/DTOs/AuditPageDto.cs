namespace SmartSentinelEye.AuditObservability.Application.DTOs;

/// <summary>
/// Cursor-paginated page of audit rows. <see cref="NextCursor"/>
/// is <c>null</c> when the caller has reached the end of the
/// matching slice.
/// </summary>
public sealed record AuditPageDto(
    IReadOnlyList<AuditRowDto> Rows,
    string? NextCursor);
