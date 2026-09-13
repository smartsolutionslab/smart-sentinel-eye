using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds. A registered type in another fab
/// does not appear.
/// </summary>
public sealed record ListEventTypesQuery(IReadOnlyList<FabIdentifier> Fabs)
    : IQuery<Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError>>;

public abstract record ListEventTypesError(string Code, string Message, System.Net.HttpStatusCode Status)
    : ApiError(Code, Message, Status);
