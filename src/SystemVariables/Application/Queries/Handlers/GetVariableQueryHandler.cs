using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

public sealed class GetVariableQueryHandler(IVariableQuerySource variables)
    : IQueryHandler<GetVariableQuery, Result<VariableDto, GetVariableError>>
{
    public async Task<Result<VariableDto, GetVariableError>> HandleAsync(GetVariableQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        // Placeholder fab (spec 014 T024 resolves the caller's fab). Scoping
        // this is not deferrable to T024: T009's index makes a name unique only
        // within a fab, so matching on the name alone would throw here the
        // moment a second fab uses one.
        FabIdentifier fab = FabIdentifier.From("munich");

        Variable? variable = await variables.Variables.SingleOrDefaultAsync(
            candidate => candidate.Fab == fab && candidate.Name == query.Name, cancellationToken);

        if (variable is null)
        {
            return Failure(GetVariableFailures.VariableNotFound(query.Name.Value));
        }

        return Success(Map(variable));
    }

    internal static VariableDto Map(Variable variable) =>
        new(
            VariableIdentifier: variable.Id.Value,
            Version: variable.Version,
            Name: variable.Name.Value,
            Type: variable.Type.Value,
            State: variable.State.Value,
            Value: variable.Value is VariableValue.Unset ? null : variable.Value.ToWireString(),
            TruthyLabel: variable.BooleanLabels?.TruthyLabel,
            FalsyLabel: variable.BooleanLabels?.FalsyLabel,
            CreatedAt: variable.CreatedAt,
            CreatedBy: variable.CreatedBy.Value);
}
