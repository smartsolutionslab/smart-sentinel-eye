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
        ArgumentNullException.ThrowIfNull(query);

        AuditEventEntity? row = await events.AuditEvents
            .FirstOrDefaultAsync(a => a.Id.Value == query.AuditIdentifier, cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? Result<AuditRowDto, GetAuditEventError>.Failure(
                new GetAuditEventError.AuditEventNotFound(query.AuditIdentifier))
            : Result<AuditRowDto, GetAuditEventError>.Success(AuditRowMapper.Map(row));
    }
}
