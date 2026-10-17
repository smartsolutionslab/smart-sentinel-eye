using System.Diagnostics.CodeAnalysis;
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
        Ensure.That(query).IsNotNull();

        (var resourceKind, var resourceIdentifier, var fab, var since, var until, var rawPageSize, var rawCursor) = query;

        if (DomainResourceKind.All.All(kind => kind.Value != resourceKind))
        {
            return Failure(GetResourceTimelineFailures.UnknownResourceKind(resourceKind));
        }

        var pageSize = rawPageSize <= 0 ? DefaultPageSize : rawPageSize;
        if (pageSize > MaximumPageSize)
        {
            return Failure(GetResourceTimelineFailures.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        if (IsUndecodableCursor(rawCursor, out var cursor))
        {
            return Failure(GetResourceTimelineFailures.InvalidCursor(rawCursor));
        }

        // Compare value objects directly so EF translates to column
        // comparisons (member access on the converted type does not translate).
        var resourceKindFilter = DomainResourceKind.From(resourceKind);
        var resourceIdentifierFilter = resourceIdentifier;
        var fabFilter = fab;

        var source = events.AuditEvents
            .Where(auditEvent => auditEvent.ResourceKind == resourceKindFilter)
            .Where(auditEvent => auditEvent.ResourceIdentifier == resourceIdentifierFilter);

        source = AuditReadScope.Named(fabFilter).Apply(source);

        source = FilterByWindowAndCursor(source, since, until, cursor);

        var rows = await source
            .OrderBy(auditEvent => auditEvent.OccurredAt)
            .ThenBy(auditEvent => auditEvent.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        return Success(ToPage(rows, pageSize));
    }

    private static bool IsUndecodableCursor(
        [NotNullWhen(true)] string? rawCursor,
        out (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor)
    {
        cursor = null;
        if (rawCursor is not null)
        {
            cursor = AuditCursor.TryDecode(rawCursor);
            return cursor is null;
        }

        return false;
    }

    private static IQueryable<AuditEventEntity> FilterByWindowAndCursor(
        IQueryable<AuditEventEntity> source,
        DateTimeOffset? since,
        DateTimeOffset? until,
        (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor)
    {
        if (since is { } sinceFrom)
        {
            source = source.Where(auditEvent => auditEvent.OccurredAt >= sinceFrom);
        }

        if (until is { } untilTo)
        {
            source = source.Where(auditEvent => auditEvent.OccurredAt < untilTo);
        }

        if (cursor is { } c)
        {
            // Ascending order — strict 'greater than' for the tuple.
            source = source.Where(auditEvent =>
                auditEvent.OccurredAt > c.OccurredAt ||
                (auditEvent.OccurredAt == c.OccurredAt && ((Guid)auditEvent.Id).CompareTo(c.AuditIdentifier) > 0));
        }

        return source;
    }

    private static AuditPageDto ToPage(
        List<AuditEventEntity> rows,
        int pageSize)
    {
        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            var last = rows[pageSize - 1];
            nextCursor = AuditCursor.Encode(last.OccurredAt, last.Id.Value);
            rows = rows.Take(pageSize).ToList();
        }

        var dtos = rows.Select(AuditRowMapper.Map).ToArray();
        return new AuditPageDto(dtos, nextCursor);
    }
}
