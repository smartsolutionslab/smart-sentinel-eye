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

        List<Variable> matches = await variables.Variables
            .Where(candidate => query.Fabs.Contains(candidate.Fab))
            .Where(candidate => candidate.Name == query.Name)
            // FR-005: a released name is free for re-use, so an Archived row
            // must not count as a live match — agrees with
            // VariableRepository.GetByNameAsync.
            .Where(candidate => candidate.State != VariableState.Archived)
            .ToListAsync(cancellationToken);

        // FR-009: a variable in a fab the caller lacks is reported exactly as
        // one that never existed. A 403 would confirm it is there and let an
        // operator enumerate another fab's names one guess at a time.
        if (matches.Count == 0)
        {
            return Failure(GetVariableFailures.VariableNotFound(query.Name.Value));
        }

        // Distinct fabs, not raw match count: with Archived excluded and the
        // fab/name unique index, two rows in one fab cannot happen, but the
        // condition holds independently of the index too.
        IReadOnlyList<string> fabsHolding =
            [.. matches.Select(match => match.Fab.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        // Not tie-broken: whichever row won would be arbitrary, and a caller
        // acting on it would be editing a fab they did not choose.
        if (fabsHolding.Count > 1)
        {
            return Failure(GetVariableFailures.VariableFabAmbiguous(query.Name.Value, fabsHolding));
        }

        return Success(Map(matches[0]));
    }

    internal static VariableDto Map(Variable variable) =>
        new(
            VariableIdentifier: variable.Id.Value,
            Version: variable.Version,
            Fab: variable.Fab.Value,
            Name: variable.Name.Value,
            Type: variable.Type.Value,
            State: variable.State.Value,
            Value: variable.Value is VariableValue.Unset ? null : variable.Value.ToWireString(),
            TruthyLabel: variable.BooleanLabels?.TruthyLabel,
            FalsyLabel: variable.BooleanLabels?.FalsyLabel,
            CreatedAt: variable.Creation.At,
            CreatedBy: variable.Creation.By.Value);
}
