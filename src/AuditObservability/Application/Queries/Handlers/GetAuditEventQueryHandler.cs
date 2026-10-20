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

        var row = await AuditReadScope.ForCaller(callerFabs).Apply(events.AuditEvents)
            .FirstOrDefaultAsync(auditEvent => auditEvent.Id == auditIdentifier, cancellationToken);

        // A row the caller may not read is filtered out by the same statement,
        // so it answers exactly as an unknown id does.
        return row is null
            ? Failure(GetAuditEventFailures.AuditEventNotFound(auditIdentifier.Value))
            : Success(AuditRowMapper.Map(row));
    }
}
