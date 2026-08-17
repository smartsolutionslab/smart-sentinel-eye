using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (spec 018 FR-001). Before this the
/// query took a single fab straight off the request, so the filter below was a
/// parameter rather than scoping.
/// </summary>
public sealed record GetEventQuery(IReadOnlyList<FabIdentifier> Fabs, EventIdentifier Identifier)
    : IQuery<Result<EventDto, GetEventError>>;
