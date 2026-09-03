using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

public sealed class ListVariablesQueryHandler(IVariableQuerySource variables)
    : IQueryHandler<ListVariablesQuery, Result<IReadOnlyList<VariableDto>, ListVariablesError>>
{
    public async Task<Result<IReadOnlyList<VariableDto>, ListVariablesError>> HandleAsync(
        ListVariablesQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        IQueryable<Variable> source = variables.Variables
            .Where(variable => query.Fabs.Contains(variable.Fab));

        if (query.State is not null)
        {
            // An exact state wins outright: a caller asking for Archived has
            // been specific, and making that also depend on IncludeArchived
            // would mean two flags to say one thing.
            source = source.Where(variable => variable.State == query.State);
        }
        else if (!query.IncludeArchived)
        {
            // The default, and the whole of #2015: without this the archive
            // flow hid nothing. A variable could be archived and the listing
            // came back identical, so the one remedy for a mistaken or
            // decommissioned variable changed nothing an operator could see.
            source = source.Where(variable => variable.State != VariableState.Archived);
        }

        List<Variable> rows = await source.ToListAsync(cancellationToken);

        // Fab breaks the tie: a multi-fab listing can now hold two rows of one
        // name, and ordering by name alone leaves their relative order to the
        // database.
        IReadOnlyList<VariableDto> dtos = rows
            .Select(GetVariableQueryHandler.Map)
            .OrderBy(dto => dto.Name, StringComparer.Ordinal)
            .ThenBy(dto => dto.Fab, StringComparer.Ordinal)
            .ToList();

        return Success(dtos);
    }
}
