using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Reads one variable by name, within the fabs the caller holds (spec 014).
///
/// <para>
/// <c>Fabs</c> is a list, not a single fab: a read does not have to choose
/// (FR-008). A name held in more than one of them is the caller's own
/// ambiguity to resolve with <c>?fabId=</c>, not something to tie-break.
/// </para>
/// </summary>
public sealed record GetVariableQuery(IReadOnlyList<FabIdentifier> Fabs, VariableName Name)
    : IQuery<Result<VariableDto, GetVariableError>>;
