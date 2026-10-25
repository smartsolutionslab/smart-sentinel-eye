using System.Diagnostics.CodeAnalysis;
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
        Ensure.That(query).IsNotNull();

        (var fab, var callerFabs, var actor, var actorUsername, var eventKind, var resourceKind,
            var resourceIdentifier, var since, var until, var rawPageSize, var rawCursor) = query;

        var pageSize = rawPageSize <= 0 ? DefaultPageSize : rawPageSize;
        if (pageSize > MaximumPageSize)
        {
            return Failure(SearchAuditFailures.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        if (resourceKind is { } rk && DomainResourceKind.All.All(kind => kind.Value != rk))
        {
            return Failure(SearchAuditFailures.InvalidResourceKind(rk));
        }

        if (IsUndecodableCursor(rawCursor, out var cursor))
        {
            return Failure(SearchAuditFailures.InvalidCursor(rawCursor));
        }

        var source = ScopeToFabs(events.AuditEvents, fab, callerFabs);
        if (!TryFilterByActor(source, actor, actorUsername, out var actorFiltered))
        {
            List<AuditEventEntity> none = [];
            return Success(none.ToAuditPageDto(null));
        }

        source = FilterByKindAndResource(actorFiltered, eventKind, resourceKind, resourceIdentifier);
        source = FilterByWindowAndCursor(source, since, until, cursor);

        var rows = await source
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            var last = rows[pageSize - 1];
            nextCursor = AuditCursor.Encode(last.OccurredAt, last.Id.Value);
            rows = rows.Take(pageSize).ToList();
        }

        return Success(rows.ToAuditPageDto(nextCursor));
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

    private static IQueryable<AuditEventEntity> ScopeToFabs(
        IQueryable<AuditEventEntity> source,
        string? fab,
        IReadOnlyList<string> callerFabs)
    {
        var scope = fab is not null
            ? AuditReadScope.Named(FabIdentifier.From(fab))
            : AuditReadScope.ForCaller(callerFabs);

        return scope.Apply(source);
    }

    private static bool TryFilterByActor(
        IQueryable<AuditEventEntity> source,
        Guid? actor,
        string? actorUsername,
        out IQueryable<AuditEventEntity> filtered)
    {
        filtered = source;
        if (actor is { } actorValue)
        {
            var actorId = ActorIdentifier.From(actorValue);
            filtered = filtered.Where(auditEvent => auditEvent.Actor == actorId);
        }
        if (actorUsername is not null)
        {
            // A search term too long to be an ActorUsername matched no rows while
            // this column was a string, and still should. Parsing it unguarded —
            // as the actor filter above does — would turn an over-long term into
            // a 500, which is a status this refactor must not introduce.
            ActorUsername parsedActorUsername;
            try
            {
                parsedActorUsername = ActorUsername.From(actorUsername);
            }
            catch (ArgumentException)
            {
                return false;
            }

            filtered = filtered.Where(auditEvent => auditEvent.ActorUsername == parsedActorUsername);
        }

        return true;
    }

    private static IQueryable<AuditEventEntity> FilterByKindAndResource(
        IQueryable<AuditEventEntity> source,
        string? eventKind,
        string? resourceKind,
        string? resourceIdentifier)
    {
        // Compare value objects directly (not their `.Value`): EF Core
        // translates equality on a value-converted property to a column
        // comparison, but cannot translate member access on the converted
        // CLR type (`a.EventKind.Value == x` throws "could not be translated").
        if (eventKind is not null)
        {
            var kind = EventKind.From(eventKind);
            source = source.Where(auditEvent => auditEvent.EventKind == kind);
        }
        if (resourceKind is not null)
        {
            var resourceKindFilter = DomainResourceKind.From(resourceKind);
            source = source.Where(auditEvent => auditEvent.ResourceKind == resourceKindFilter);
        }
        if (resourceIdentifier is not null)
        {
            var resId = ResourceIdentifier.From(resourceIdentifier);
            source = source.Where(auditEvent => auditEvent.ResourceIdentifier == resId);
        }

        return source;
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
            // Strict 'less than' for descending order; tuple compare
            // breaks ties on AuditIdentifier so concurrent inserts
            // sharing the same OccurredAt don't shift the window.
            source = source.Where(auditEvent =>
                auditEvent.OccurredAt < c.OccurredAt ||
                (auditEvent.OccurredAt == c.OccurredAt && ((Guid)auditEvent.Id).CompareTo(c.AuditIdentifier) < 0));
        }

        return source;
    }
}
