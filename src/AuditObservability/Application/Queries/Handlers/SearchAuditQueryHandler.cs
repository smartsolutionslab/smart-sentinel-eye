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

        var (fab, callerFabs, actor, actorUsername, eventKind, resourceKind,
            resourceIdentifier, since, until, rawPageSize, rawCursor) = query;

        int pageSize = rawPageSize <= 0 ? DefaultPageSize : rawPageSize;
        if (pageSize > MaximumPageSize)
        {
            return Result<AuditPageDto, SearchAuditError>.Failure(
                new SearchAuditError.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        if (resourceKind is { } rk && !DomainResourceKind.All.Any(kind => kind.Value == rk))
        {
            return Result<AuditPageDto, SearchAuditError>.Failure(
                new SearchAuditError.InvalidResourceKind(rk));
        }

        (DateTimeOffset OccurredAt, Guid AuditIdentifier)? cursor = null;
        if (rawCursor is not null)
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
        if (fab is not null)
        {
            FabIdentifier fabId = FabIdentifier.From(fab);
            source = source.Where(auditEvent => auditEvent.Fab == fabId);
        }
        else if (callerFabs.Count > 0)
        {
            List<FabIdentifier> allowed = [.. callerFabs.Select(FabIdentifier.From)];
            source = source.Where(auditEvent => auditEvent.Fab != null && allowed.Contains(auditEvent.Fab));
        }
        else
        {
            // A caller with no fab membership can only see cross-fab rows.
            source = source.Where(auditEvent => auditEvent.Fab == null);
        }

        if (actor is { } actorValue)
        {
            ActorIdentifier actorId = ActorIdentifier.From(actorValue);
            source = source.Where(auditEvent => auditEvent.Actor == actorId);
        }
        if (actorUsername is not null)
        {
            source = source.Where(auditEvent => auditEvent.ActorUsername == actorUsername);
        }
        if (eventKind is not null)
        {
            EventKind kind = EventKind.From(eventKind);
            source = source.Where(auditEvent => auditEvent.EventKind == kind);
        }
        if (resourceKind is not null)
        {
            DomainResourceKind resourceKindFilter = DomainResourceKind.From(resourceKind);
            source = source.Where(auditEvent => auditEvent.ResourceKind == resourceKindFilter);
        }
        if (resourceIdentifier is not null)
        {
            ResourceIdentifier resId = ResourceIdentifier.From(resourceIdentifier);
            source = source.Where(auditEvent => auditEvent.ResourceIdentifier == resId);
        }
        if (since is { } sinceFrom) source = source.Where(auditEvent => auditEvent.OccurredAt >= sinceFrom);
        if (until is { } untilTo) source = source.Where(auditEvent => auditEvent.OccurredAt < untilTo);

        if (cursor is { } c)
        {
            // Strict 'less than' for descending order; tuple compare
            // breaks ties on AuditIdentifier so concurrent inserts
            // sharing the same OccurredAt don't shift the window.
            source = source.Where(auditEvent =>
                auditEvent.OccurredAt < c.OccurredAt ||
                (auditEvent.OccurredAt == c.OccurredAt && ((Guid)auditEvent.Id).CompareTo(c.AuditIdentifier) < 0));
        }

        List<AuditEventEntity> rows = await source
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ThenByDescending(auditEvent => auditEvent.Id)
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
