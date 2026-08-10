using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands;

/// <summary>
/// Archives a system variable (spec 005 US4). Idempotent on
/// already-Archived. The name is released for re-use on the next
/// <c>DefineVariable</c>.
/// </summary>
public sealed record ArchiveVariableCommand(
    FabIdentifier Fab,
    VariableName Name,
    OperatorIdentifier ArchivedBy,
    int ExpectedVersion)
    : ICommand<Result<VariableIdentifier, ArchiveVariableError>>;
