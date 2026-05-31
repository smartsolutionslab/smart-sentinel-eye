using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

public sealed class GetVariableQueryHandler(IVariableQuerySource variables)
    : IQueryHandler<GetVariableQuery, Result<VariableDto, GetVariableError>>
{
    public async Task<Result<VariableDto, GetVariableError>> HandleAsync(
        GetVariableQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Variable? variable = await variables.Variables
            .SingleOrDefaultAsync(candidate => candidate.Name == query.Name, cancellationToken)
            .ConfigureAwait(false);

        if (variable is null)
        {
            return Result<VariableDto, GetVariableError>.Failure(
                new GetVariableError.VariableNotFound(query.Name.Value));
        }

        return Result<VariableDto, GetVariableError>.Success(Map(variable));
    }

    internal static VariableDto Map(Variable variable) =>
        new(
            VariableIdentifier: variable.Id.Value,
            Name: variable.Name.Value,
            Type: variable.Type.Value,
            State: variable.State.Value,
            Value: variable.Value is VariableValue.Unset ? null : variable.Value.ToWireString(),
            TruthyLabel: variable.BooleanLabels?.TruthyLabel,
            FalsyLabel: variable.BooleanLabels?.FalsyLabel,
            CreatedAt: variable.CreatedAt,
            CreatedBy: variable.CreatedBy.Value);
}
