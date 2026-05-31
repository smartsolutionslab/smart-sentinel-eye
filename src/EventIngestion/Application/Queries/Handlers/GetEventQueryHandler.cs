using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Queries.Handlers;

public sealed class GetEventQueryHandler(IEventQuerySource events)
    : IQueryHandler<GetEventQuery, Result<EventDto, GetEventError>>
{
    public async Task<Result<EventDto, GetEventError>> HandleAsync(
        GetEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (fab, identifier) = query;

        EventAggregate? found = await events.Events
            .Where(e => e.Fab == fab && e.Id == identifier)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return Result<EventDto, GetEventError>.Failure(
                new GetEventError.EventNotFound(identifier.Value));
        }

        return Result<EventDto, GetEventError>.Success(Map(found));
    }

    internal static EventDto Map(EventAggregate @event) =>
        new(
            EventIdentifier: @event.Id.Value,
            Fab: @event.Fab.Value,
            Source: @event.Source.Value,
            Device: @event.Device.Value,
            Kind: @event.Kind.Value,
            OccurredAt: @event.OccurredAt.Value,
            IngestedAt: @event.IngestedAt.Value,
            Payload: @event.Payload.Value);
}
