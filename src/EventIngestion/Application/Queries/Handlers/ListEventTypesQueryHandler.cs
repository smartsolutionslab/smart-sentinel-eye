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

        var rows = await eventTypes.RegisteredEventTypes
            .Where(eventType =>
                query.Fabs.Contains(eventType.Fab) && eventType.State == RegistrationState.Registered)
            .ToListAsync(cancellationToken);

        IReadOnlyList<RegisteredEventTypeDto> dtos = rows
            .Select(eventType => eventType.ToDto())
            .OrderBy(dto => dto.Kind, StringComparer.Ordinal)
            .ToArray();

        return Success(dtos);
    }
}
