using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

public sealed class GetAuditEventQueryHandler(IAuditEventQuerySource events)
    : IQueryHandler<GetAuditEventQuery, Result<AuditRowDto, GetAuditEventError>>
{
    public async Task<Result<AuditRowDto, GetAuditEventError>> HandleAsync(
        GetAuditEventQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        AuditEventEntity? row = await events.AuditEvents
            .FirstOrDefaultAsync(auditEvent => auditEvent.Id == query.AuditIdentifier, cancellationToken);

        return row is null
            ? Failure(GetAuditEventFailures.AuditEventNotFound(query.AuditIdentifier.Value))
            : Success(AuditRowMapper.Map(row));
    }
}
