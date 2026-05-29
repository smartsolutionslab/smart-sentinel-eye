using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Queries;

/// <summary>
/// Single audit row + full payload by id (spec 009 FR-010).
/// </summary>
public sealed record GetAuditEventQuery(Guid AuditIdentifier)
    : IQuery<Result<AuditRowDto, GetAuditEventError>>;
