using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class ArchiveVariableCommandHandler(IVariableRepository variables, IClock clock, ILogger<ArchiveVariableCommandHandler> logger)
    : ICommandHandler<ArchiveVariableCommand, Result<VariableIdentifier, ArchiveVariableError>>
{
    public async Task<Result<VariableIdentifier, ArchiveVariableError>> HandleAsync(ArchiveVariableCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (VariableName? name, OperatorIdentifier archivedBy, int expectedVersion) = command;

        Option<Variable> found = await variables.GetByNameAsync(name, cancellationToken);
        if (!found.HasValue)
        {
            return Result<VariableIdentifier, ArchiveVariableError>.Failure(new ArchiveVariableError.VariableNotFound(name.Value));
        }

        Variable variable = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the variable that
        // has since moved. Checked before any mutation so nothing is applied on
        // top of stale intent.
        if (variable.Version != expectedVersion)
        {
            return Result<VariableIdentifier, ArchiveVariableError>.Failure(
                new ArchiveVariableError.VariableStale(name.Value, expectedVersion, variable.Version));
        }
        variable.Archive(archivedBy, clock);
        await variables.SaveAsync(cancellationToken);

        logger.ArchivedVariable(variable.Id, name, archivedBy);

        return Result<VariableIdentifier, ArchiveVariableError>.Success(variable.Id);
    }
}
