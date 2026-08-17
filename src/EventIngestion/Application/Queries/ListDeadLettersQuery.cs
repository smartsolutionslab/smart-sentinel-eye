using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (spec 018 FR-009). A rejected
/// delivery from anywhere else does not appear, and neither does one whose
/// plant was never established — <c>NULL</c> satisfies no <c>IN</c>, which is
/// FR-011 falling out of the query rather than needing to be remembered.
/// </summary>
public sealed record ListDeadLettersQuery(IReadOnlyList<FabIdentifier> Fabs, int Limit)
    : IQuery<Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError>>;

public abstract record ListDeadLettersError(string Code, string Message, System.Net.HttpStatusCode Status)
    : ApiError(Code, Message, Status);
