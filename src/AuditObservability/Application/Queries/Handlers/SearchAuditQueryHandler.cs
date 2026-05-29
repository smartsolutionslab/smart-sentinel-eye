using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;
using DomainResourceKind = SmartSentinelEye.AuditObservability.Domain.AuditEvent.ResourceKind;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

public sealed class SearchAuditQueryHandler(IAuditEventQuerySource events)
    : IQueryHandler<SearchAuditQuery, Result<AuditPageDto, SearchAuditError>>
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public async Task<Result<AuditPageDto, SearchAuditError>> HandleAsync(
        SearchAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int pageSize = query.PageSize <= 0 ? DefaultPageSize : query.PageSize;
        if (pageSize > MaximumPageSize)
        {
            return Result<AuditPageDto, SearchAuditError>.Failure(
                new SearchAuditError.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        if (query.ResourceKind is { } rk && !DomainResourceKind.All.Any(k => k.Value == rk))
        {
            return Result<AuditPageDto, SearchAuditError>.Failure(
                new SearchAuditError.InvalidResourceKind(rk));
        }

        (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor = null;
        if (query.Cursor is { } rawCursor)
        {
            cursor = AuditCursor.TryDecode(rawCursor);
            if (cursor is null)
            {
                return Result<AuditPageDto, SearchAuditError>.Failure(
                    new SearchAuditError.InvalidCursor(rawCursor));
            }
        }

        IQueryable<AuditEventEntity> source = events.AuditEvents;

        if (query.Fab is { } fab)
        {
            source = source.Where(a => a.Fab.HasValue && a.Fab.Value.Value == fab);
        }
        else if (query.CallerFabs.Count > 0)
        {
            HashSet<string> allowed = query.CallerFabs.ToHashSet(StringComparer.Ordinal);
            source = source.Where(a =>
                a.Fab.HasValue && allowed.Contains(a.Fab.Value.Value));
        }
        else
        {
            // A caller with no fab membership can only see cross-fab rows.
            source = source.Where(a => !a.Fab.HasValue);
        }

        if (query.Actor is { } actor)
        {
            source = source.Where(a => a.Actor.Value == actor);
        }
        if (query.ActorUsername is { } actorUsername)
        {
            source = source.Where(a =>
                a.ActorUsername.HasValue && a.ActorUsername.Value == actorUsername);
        }
        if (query.EventKind is { } eventKind)
        {
            source = source.Where(a => a.EventKind.Value == eventKind);
        }
        if (query.ResourceKind is { } resourceKind)
        {
            source = source.Where(a =>
                a.ResourceKind.HasValue && a.ResourceKind.Value.Value == resourceKind);
        }
        if (query.ResourceIdentifier is { } resourceIdentifier)
        {
            source = source.Where(a =>
                a.ResourceIdentifier.HasValue && a.ResourceIdentifier.Value.Value == resourceIdentifier);
        }
        if (query.Since is { } since) source = source.Where(a => a.OccurredAt >= since);
        if (query.Until is { } until) source = source.Where(a => a.OccurredAt < until);

        if (cursor is { } c)
        {
            // Strict 'less than' for descending order; tuple compare
            // breaks ties on AuditIdentifier so concurrent inserts
            // sharing the same OccurredAt don't shift the window.
            source = source.Where(a =>
                a.OccurredAt < c.OccurredAt ||
                (a.OccurredAt == c.OccurredAt && a.Id.Value.CompareTo(c.AuditIdentifier) < 0));
        }

        List<AuditEventEntity> rows = await source
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id.Value)
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
        return Result<AuditPageDto, SearchAuditError>.Success(new AuditPageDto(dtos, nextCursor));
    }
}
