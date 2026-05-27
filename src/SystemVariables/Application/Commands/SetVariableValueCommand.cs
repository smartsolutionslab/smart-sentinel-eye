using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands;

/// <summary>
/// Sets the current value of an existing system variable (spec 005 US2).
/// The provided <paramref name="WireValue"/> is parsed against the
/// variable's declared type at handle time (FR-007/FR-008). Type
/// mismatch returns <see cref="SetVariableValueError.VariableTypeMismatch"/>.
/// </summary>
public sealed record SetVariableValueCommand(
    VariableName Name,
    string WireValue,
    OperatorIdentifier ChangedBy)
    : ICommand<Result<VariableIdentifier, SetVariableValueError>>;
