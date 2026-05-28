using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class ArchiveVariableCommandHandler(
    IVariableRepository variables,
    IClock clock,
    ILogger<ArchiveVariableCommandHandler> log)
    : ICommandHandler<ArchiveVariableCommand, Result<VariableIdentifier, ArchiveVariableError>>
{
    public async Task<Result<VariableIdentifier, ArchiveVariableError>> HandleAsync(
        ArchiveVariableCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Variable> found = await variables
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<VariableIdentifier, ArchiveVariableError>.Failure(
                new ArchiveVariableError.VariableNotFound(command.Name.Value));
        }

        Variable variable = found.Value;
        variable.Archive(command.ArchivedBy, clock);
        await variables.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Archived variable {Variable} '{Name}' by {Operator}.",
            variable.Id, command.Name, command.ArchivedBy);

        return Result<VariableIdentifier, ArchiveVariableError>.Success(variable.Id);
    }
}
