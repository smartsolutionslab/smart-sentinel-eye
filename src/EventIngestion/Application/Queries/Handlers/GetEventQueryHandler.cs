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
        Ensure.That(query).IsNotNull();
        (IReadOnlyList<Domain.Event.FabIdentifier> fabs, Domain.Event.EventIdentifier identifier) = query;

        // FR-004: the fabs are part of the lookup rather than a check
        // afterwards, so an event outside them and an identifier that matches
        // nothing leave here identically and produce the same response.
        EventAggregate? found = await events.Events
            .Where(eventEntity => fabs.Contains(eventEntity.Fab) && eventEntity.Id == identifier)
            .FirstOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            return Failure(GetEventFailures.EventNotFound(identifier.Value));
        }

        return Success(Map(found));
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
