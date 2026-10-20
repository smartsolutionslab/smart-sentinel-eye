using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

public sealed class GetAuditEventQueryHandler(IAuditEventQuerySource events)
    : IQueryHandler<GetAuditEventQuery, Result<AuditRowDto, GetAuditEventError>>
{
    public async Task<Result<AuditRowDto, GetAuditEventError>> HandleAsync(
        GetAuditEventQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var (auditIdentifier, callerFabs) = query;

        var row = await events.AuditEvents
            .FirstOrDefaultAsync(auditEvent => auditEvent.Id == auditIdentifier, cancellationToken);

        // A row the caller may not read answers exactly as an unknown id does,
        // so the response does not tell the caller that the row exists.
        return row is null || !AuditReadScope.ForCaller(callerFabs).Admits(row)
            ? Failure(GetAuditEventFailures.AuditEventNotFound(auditIdentifier.Value))
            : Success(AuditRowMapper.Map(row));
    }
}
