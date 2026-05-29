using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;
using DomainResourceKind = SmartSentinelEye.AuditObservability.Domain.AuditEvent.ResourceKind;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

public sealed class GetResourceTimelineQueryHandler(IAuditEventQuerySource events)
    : IQueryHandler<GetResourceTimelineQuery, Result<AuditPageDto, GetResourceTimelineError>>
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public async Task<Result<AuditPageDto, GetResourceTimelineError>> HandleAsync(
        GetResourceTimelineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!DomainResourceKind.All.Any(k => k.Value == query.ResourceKind))
        {
            return Result<AuditPageDto, GetResourceTimelineError>.Failure(
                new GetResourceTimelineError.UnknownResourceKind(query.ResourceKind));
        }

        int pageSize = query.PageSize <= 0 ? DefaultPageSize : query.PageSize;
        if (pageSize > MaximumPageSize)
        {
            return Result<AuditPageDto, GetResourceTimelineError>.Failure(
                new GetResourceTimelineError.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor = null;
        if (query.Cursor is { } rawCursor)
        {
            cursor = AuditCursor.TryDecode(rawCursor);
            if (cursor is null)
            {
                return Result<AuditPageDto, GetResourceTimelineError>.Failure(
                    new GetResourceTimelineError.InvalidCursor(rawCursor));
            }
        }

        IQueryable<AuditEventEntity> source = events.AuditEvents
            .Where(a => a.ResourceKind.HasValue && a.ResourceKind.Value.Value == query.ResourceKind)
            .Where(a => a.ResourceIdentifier.HasValue && a.ResourceIdentifier.Value.Value == query.ResourceIdentifier)
            .Where(a => a.Fab.HasValue && a.Fab.Value.Value == query.Fab);

        if (query.Since is { } since) source = source.Where(a => a.OccurredAt >= since);
        if (query.Until is { } until) source = source.Where(a => a.OccurredAt < until);

        if (cursor is { } c)
        {
            // Ascending order — strict 'greater than' for the tuple.
            source = source.Where(a =>
                a.OccurredAt > c.OccurredAt ||
                (a.OccurredAt == c.OccurredAt && a.Id.Value.CompareTo(c.AuditIdentifier) > 0));
        }

        List<AuditEventEntity> rows = await source
            .OrderBy(a => a.OccurredAt)
            .ThenBy(a => a.Id.Value)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            AuditEventEntity last = rows[pageSize - 1];
            nextCursor = AuditCursor.Encode(last.OccurredAt, last.Id.Value);
            rows = rows.Take(pageSize).ToList();
        }

        AuditRowDto[] dtos = rows.Select(AuditRowMapper.Map).ToArray();
        return Result<AuditPageDto, GetResourceTimelineError>.Success(new AuditPageDto(dtos, nextCursor));
    }
}
