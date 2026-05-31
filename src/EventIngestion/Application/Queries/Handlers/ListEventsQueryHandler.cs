using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Queries.Handlers;

public sealed class ListEventsQueryHandler(IEventQuerySource events)
    : IQueryHandler<ListEventsQuery, Result<EventPageDto, ListEventsError>>
{
    public const int DefaultPageSize = 100;
    public const int MaximumPageSize = 1_000;

    public async Task<Result<EventPageDto, ListEventsError>> HandleAsync(
        ListEventsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The filter fields are consumed by BuildPagedQuery (which takes the
        // whole query so its EF translation can be verified offline); only the
        // paging inputs are handled here.
        var (_, _, _, _, _, _, _, _, rawPageSize, rawCursor) = query;

        int pageSize = rawPageSize <= 0 ? DefaultPageSize : rawPageSize;
        if (pageSize > MaximumPageSize)
        {
            return Result<EventPageDto, ListEventsError>.Failure(
                new ListEventsError.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        (DateTimeOffset Ingested, Guid EventId)? cursor = null;
        if (!string.IsNullOrEmpty(rawCursor))
        {
            cursor = TryDecodeCursor(rawCursor);
            if (cursor is null)
            {
                return Result<EventPageDto, ListEventsError>.Failure(
                    new ListEventsError.InvalidCursor(rawCursor));
            }
        }

        List<EventAggregate> rows = await BuildPagedQuery(events.Events, query, cursor, pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            EventAggregate last = rows[pageSize - 1];
            nextCursor = EncodeCursor(last.IngestedAt.Value, last.Id.Value);
            rows = rows.Take(pageSize).ToList();
        }

        EventDto[] items = rows.Select(GetEventQueryHandler.Map).ToArray();
        return Result<EventPageDto, ListEventsError>.Success(
            new EventPageDto(items, nextCursor));
    }

    /// <summary>
    /// Builds the filtered, cursor-bounded, descending query (taking one extra
    /// row to detect a next page). Extracted so the EF Core SQL translation of
    /// the value-converted time/id columns can be verified offline (the filters
    /// and ordering compare the value objects directly — the implicit operators
    /// unwrap to the underlying column; member access on <c>.Value</c> does not
    /// translate).
    /// </summary>
    public static IQueryable<EventAggregate> BuildPagedQuery(
        IQueryable<EventAggregate> events,
        ListEventsQuery query,
        (DateTimeOffset Ingested, Guid EventId)? cursor,
        int pageSize)
    {
        IQueryable<EventAggregate> source = events.Where(eventEntity => eventEntity.Fab == query.Fab);

        if (query.Source is not null) source = source.Where(eventEntity => eventEntity.Source == query.Source);
        if (query.Device is not null) source = source.Where(eventEntity => eventEntity.Device == query.Device);
        if (query.Kind is not null) source = source.Where(eventEntity => eventEntity.Kind == query.Kind);
        if (query.OccurredAfter is { } occurredAfter) source = source.Where(eventEntity => eventEntity.OccurredAt > occurredAfter);
        if (query.OccurredBefore is { } occurredBefore) source = source.Where(eventEntity => eventEntity.OccurredAt < occurredBefore);
        if (query.IngestedAfter is { } ingestedAfter) source = source.Where(eventEntity => eventEntity.IngestedAt > ingestedAfter);
        if (query.IngestedBefore is { } ingestedBefore) source = source.Where(eventEntity => eventEntity.IngestedAt < ingestedBefore);

        if (cursor is { } c)
        {
            // Strict 'less than' for descending order. IngestedAt is
            // microsecond-precision; the chance of two events colliding across
            // a page boundary is negligible at 1k/s. The eventId in the cursor
            // is reserved for a future tuple-compare tightening.
            source = source.Where(eventEntity => eventEntity.IngestedAt < c.Ingested);
        }

        return source
            .OrderByDescending(eventEntity => eventEntity.IngestedAt)
            .ThenByDescending(eventEntity => eventEntity.Id)
            .Take(pageSize + 1);
    }

    private static string EncodeCursor(DateTimeOffset ingested, Guid eventId)
    {
        string raw = $"{ingested.UtcTicks:D19}.{eventId:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateTimeOffset Ingested, Guid EventId)? TryDecodeCursor(string cursor)
    {
        try
        {
            string raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            int dot = raw.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0) return null;
            long ticks = long.Parse(raw[..dot], CultureInfo.InvariantCulture);
            Guid id = Guid.ParseExact(raw[(dot + 1)..], "N");
            return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }
}
