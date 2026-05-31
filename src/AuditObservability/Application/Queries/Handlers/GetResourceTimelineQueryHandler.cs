using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
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

        var (resourceKind, resourceIdentifier, fab, since, until, rawPageSize, rawCursor) = query;

        if (!DomainResourceKind.All.Any(k => k.Value == resourceKind))
        {
            return Result<AuditPageDto, GetResourceTimelineError>.Failure(
                new GetResourceTimelineError.UnknownResourceKind(resourceKind));
        }

        int pageSize = rawPageSize <= 0 ? DefaultPageSize : rawPageSize;
        if (pageSize > MaximumPageSize)
        {
            return Result<AuditPageDto, GetResourceTimelineError>.Failure(
                new GetResourceTimelineError.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor = null;
        if (rawCursor is not null)
        {
            cursor = AuditCursor.TryDecode(rawCursor);
            if (cursor is null)
            {
                return Result<AuditPageDto, GetResourceTimelineError>.Failure(
                    new GetResourceTimelineError.InvalidCursor(rawCursor));
            }
        }

        // Compare value objects directly so EF translates to column
        // comparisons (member access on the converted type does not translate).
        DomainResourceKind resourceKindFilter = DomainResourceKind.From(resourceKind);
        ResourceIdentifier resourceIdentifierFilter = ResourceIdentifier.From(resourceIdentifier);
        FabIdentifier fabFilter = FabIdentifier.From(fab);

        IQueryable<AuditEventEntity> source = events.AuditEvents
            .Where(a => a.ResourceKind == resourceKindFilter)
            .Where(a => a.ResourceIdentifier == resourceIdentifierFilter)
            .Where(a => a.Fab == fabFilter);

        if (since is { } sinceFrom) source = source.Where(a => a.OccurredAt >= sinceFrom);
        if (until is { } untilTo) source = source.Where(a => a.OccurredAt < untilTo);

        if (cursor is { } c)
        {
            // Ascending order — strict 'greater than' for the tuple.
            source = source.Where(a =>
                a.OccurredAt > c.OccurredAt ||
                (a.OccurredAt == c.OccurredAt && ((Guid)a.Id).CompareTo(c.AuditIdentifier) > 0));
        }

        List<AuditEventEntity> rows = await source
            .OrderBy(a => a.OccurredAt)
            .ThenBy(a => a.Id)
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
