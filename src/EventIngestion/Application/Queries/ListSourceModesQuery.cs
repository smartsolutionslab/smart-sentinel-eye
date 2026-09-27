using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds. A declared mode in another fab
/// does not appear. An undeclared pair is discovery and is never listed
/// (spec.md FR-012, FR-003).
/// </summary>
public sealed record ListSourceModesQuery(IReadOnlyList<FabIdentifier> Fabs)
    : IQuery<Result<IReadOnlyList<SourceModeDto>, ListSourceModesError>>;

public abstract record ListSourceModesError(string Code, string Message, System.Net.HttpStatusCode Status)
    : ApiError(Code, Message, Status);
