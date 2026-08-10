using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class ArchiveVariableCommandHandler(
    IVariableRepository variables,
    IClock clock,
    ILogger<ArchiveVariableCommandHandler> logger)
    : ICommandHandler<ArchiveVariableCommand, Result<VariableIdentifier, ArchiveVariableError>>
{
    public async Task<Result<VariableIdentifier, ArchiveVariableError>> HandleAsync(
        ArchiveVariableCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (FabIdentifier? fab, VariableName? name, OperatorIdentifier archivedBy, int expectedVersion) = command;

        // The fab check runs before the version comparison below, so the
        // concurrency layer never gets to reveal that another fab's variable
        // exists: a 409 here would distinguish it from a name that was never
        // used, which is what FR-009 forbids.
        Option<Variable> found = await variables.GetByNameAsync(fab, name, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(ArchiveVariableFailures.VariableNotFound(name.Value));
        }

        Variable variable = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the variable that
        // has since moved. Checked before any mutation so nothing is applied on
        // top of stale intent.
        if (variable.Version != expectedVersion)
        {
            return Failure(ArchiveVariableFailures.VariableStale(name.Value, expectedVersion, variable.Version));
        }
        variable.Archive(archivedBy, clock);
        await variables.SaveAsync(cancellationToken);

        logger.ArchivedVariable(variable.Id, name, archivedBy);

        return Success(variable.Id);
    }
}
