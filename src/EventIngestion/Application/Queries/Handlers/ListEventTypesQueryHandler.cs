using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries.Handlers;

/// <summary>
/// Phase 4a prelude (spec 143 T002): the signature is the one plan.md §6
/// specifies. The body below answers an empty list and touches no query
/// source; T005 replaces it with the fab-and-state filter plan.md describes.
/// </summary>
public sealed class ListEventTypesQueryHandler(IRegisteredEventTypeQuerySource eventTypes)
    : IQueryHandler<ListEventTypesQuery, Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError>>
{
    public Task<Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError>> HandleAsync(
        ListEventTypesQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        // Phase 4a prelude: the injected collaborators are unused for now.
        // T005 replaces this whole body with the fab/state-filtered projection.
        _ = query;
        _ = eventTypes;

        return Task.FromResult<Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError>>(
            Success((IReadOnlyList<RegisteredEventTypeDto>)[]));
    }
}
