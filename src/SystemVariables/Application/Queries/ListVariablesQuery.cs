using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Lists system variables in the fabs the caller holds, optionally filtered
/// by state (spec 014 FR-008).
///
/// <para>
/// Spans all of the caller's fabs when they name none — the deliberate
/// asymmetry with the write path, which must choose. A listing that refused
/// a multi-fab operator would make the endpoint unusable for exactly the
/// people it exists for.
/// </para>
/// </summary>
public sealed record ListVariablesQuery(IReadOnlyList<FabIdentifier> Fabs, VariableState? State)
    : IQuery<Result<IReadOnlyList<VariableDto>, ListVariablesError>>;
