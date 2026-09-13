using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries.Handlers;

public sealed class ListEventTypesQueryHandler(IRegisteredEventTypeQuerySource eventTypes)
    : IQueryHandler<ListEventTypesQuery, Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError>>
{
    public async Task<Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError>> HandleAsync(
        ListEventTypesQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        List<RegisteredEventType> rows = await eventTypes.RegisteredEventTypes
            .Where(eventType =>
                query.Fabs.Contains(eventType.Fab) && eventType.State == RegistrationState.Registered)
            .ToListAsync(cancellationToken);

        IReadOnlyList<RegisteredEventTypeDto> dtos = rows
            .Select(eventType => new RegisteredEventTypeDto(
                eventType.Id.Value,
                eventType.Fab.Value,
                eventType.Kind.Value,
                eventType.State.Value,
                eventType.Registration.RegisteredAt,
                eventType.Registration.RegisteredBy,
                eventType.Version))
            .OrderBy(dto => dto.Kind, StringComparer.Ordinal)
            .ToArray();

        return Success(dtos);
    }
}
