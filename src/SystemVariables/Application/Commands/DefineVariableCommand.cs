using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands;

/// <summary>
/// Defines a new system variable (spec 005 US1). The name must be
/// unique across non-Archived variables; archived names are released
/// for re-use. BooleanLabels must be supplied iff Type is Boolean.
/// Initial value is optional — omitted means the variable starts
/// <c>Unset</c>.
/// </summary>
public sealed record DefineVariableCommand(
    VariableName Name,
    VariableType Type,
    VariableValue? InitialValue,
    BooleanLabels? BooleanLabels,
    OperatorIdentifier DefinedBy)
    : ICommand<Result<VariableIdentifier, DefineVariableError>>;
