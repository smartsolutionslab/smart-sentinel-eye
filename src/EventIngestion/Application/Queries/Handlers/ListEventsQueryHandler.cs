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

        int pageSize = query.PageSize <= 0 ? DefaultPageSize : query.PageSize;
        if (pageSize > MaximumPageSize)
        {
            return Result<EventPageDto, ListEventsError>.Failure(
                new ListEventsError.PageSizeOutOfRange(pageSize, 1, MaximumPageSize));
        }

        (DateTimeOffset Ingested, Guid EventId)? cursor = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            cursor = TryDecodeCursor(query.Cursor);
            if (cursor is null)
            {
                return Result<EventPageDto, ListEventsError>.Failure(
                    new ListEventsError.InvalidCursor(query.Cursor));
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
        IQueryable<EventAggregate> source = events.Where(e => e.Fab == query.Fab);

        if (query.Source is not null) source = source.Where(e => e.Source == query.Source);
        if (query.Device is not null) source = source.Where(e => e.Device == query.Device);
        if (query.Kind is not null) source = source.Where(e => e.Kind == query.Kind);
        if (query.OccurredAfter is { } occurredAfter) source = source.Where(e => e.OccurredAt > occurredAfter);
        if (query.OccurredBefore is { } occurredBefore) source = source.Where(e => e.OccurredAt < occurredBefore);
        if (query.IngestedAfter is { } ingestedAfter) source = source.Where(e => e.IngestedAt > ingestedAfter);
        if (query.IngestedBefore is { } ingestedBefore) source = source.Where(e => e.IngestedAt < ingestedBefore);

        if (cursor is { } c)
        {
            // Strict 'less than' for descending order. IngestedAt is
            // microsecond-precision; the chance of two events colliding across
            // a page boundary is negligible at 1k/s. The eventId in the cursor
            // is reserved for a future tuple-compare tightening.
            source = source.Where(e => e.IngestedAt < c.Ingested);
        }

        return source
            .OrderByDescending(e => e.IngestedAt)
            .ThenByDescending(e => e.Id)
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
