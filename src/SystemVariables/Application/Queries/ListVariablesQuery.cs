using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Lists every system variable, optionally filtered by state.
/// </summary>
public sealed record ListVariablesQuery(VariableState? State)
    : IQuery<Result<IReadOnlyList<VariableDto>, ListVariablesError>>;
