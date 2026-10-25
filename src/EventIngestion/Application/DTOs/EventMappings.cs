using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.DTOs;

internal static class EventMappings
{
    public static EventDto ToDto(this EventAggregate @event) =>
        new(
            EventIdentifier: @event.Id.Value,
            Fab: @event.Fab.Value,
            Source: @event.Source.Value,
            Device: @event.Device.Value,
            Kind: @event.Kind.Value,
            OccurredAt: @event.OccurredAt.Value,
            IngestedAt: @event.IngestedAt.Value,
            Payload: @event.Payload.Value);

    public static EventPageDto ToEventPageDto(
        this IEnumerable<EventAggregate> rows,
        string? nextCursor)
    {
        var items = rows.Select(ToDto).ToArray();
        return new EventPageDto(items, nextCursor);
    }
}
