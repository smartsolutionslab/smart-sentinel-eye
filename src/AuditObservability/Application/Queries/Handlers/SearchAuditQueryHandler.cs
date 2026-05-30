using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
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

        // Compare value objects directly (not their `.Value`): EF Core
        // translates equality on a value-converted property to a column
        // comparison, but cannot translate member access on the converted
        // CLR type (`a.EventKind.Value == x` throws "could not be translated").
        if (query.Fab is { } fab)
        {
            FabIdentifier fabId = FabIdentifier.From(fab);
            source = source.Where(a => a.Fab == fabId);
        }
        else if (query.CallerFabs.Count > 0)
        {
            List<FabIdentifier> allowed = [.. query.CallerFabs.Select(FabIdentifier.From)];
            source = source.Where(a => a.Fab != null && allowed.Contains(a.Fab));
        }
        else
        {
            // A caller with no fab membership can only see cross-fab rows.
            source = source.Where(a => a.Fab == null);
        }

        if (query.Actor is { } actor)
        {
            ActorIdentifier actorId = ActorIdentifier.From(actor);
            source = source.Where(a => a.Actor == actorId);
        }
        if (query.ActorUsername is { } actorUsername)
        {
            source = source.Where(a => a.ActorUsername == actorUsername);
        }
        if (query.EventKind is { } eventKind)
        {
            EventKind kind = EventKind.From(eventKind);
            source = source.Where(a => a.EventKind == kind);
        }
        if (query.ResourceKind is { } resourceKind)
        {
            DomainResourceKind resourceKindFilter = DomainResourceKind.From(resourceKind);
            source = source.Where(a => a.ResourceKind == resourceKindFilter);
        }
        if (query.ResourceIdentifier is { } resourceIdentifier)
        {
            ResourceIdentifier resId = ResourceIdentifier.From(resourceIdentifier);
            source = source.Where(a => a.ResourceIdentifier == resId);
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
                (a.OccurredAt == c.OccurredAt && ((Guid)a.Id).CompareTo(c.AuditIdentifier) < 0));
        }

        List<AuditEventEntity> rows = await source
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
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
