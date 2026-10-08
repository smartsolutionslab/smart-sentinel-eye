using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (spec 018 FR-009). A rejected
/// delivery from anywhere else does not appear, and neither does one whose
/// plant was never established — <c>NULL</c> satisfies no <c>IN</c>, which is
/// FR-011 falling out of the query rather than needing to be remembered.
///
/// <para>
/// <c>Reason</c> and <c>State</c> narrow the listing (spec 317, #2325,
/// FR-008) — present or absent independently, combined with AND when both
/// are present.
/// </para>
/// </summary>
public sealed record ListDeadLettersQuery(
    IReadOnlyList<FabIdentifier> Fabs, int Limit, Option<DeadLetterReason> Reason, Option<HoldState> State)
    : IQuery<Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError>>;

public abstract record ListDeadLettersError(string Code, string Message, System.Net.HttpStatusCode Status)
    : ApiError(Code, Message, Status);
