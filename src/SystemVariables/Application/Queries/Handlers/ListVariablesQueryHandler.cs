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
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Variable> source = variables.Variables;
        if (query.State is not null)
        {
            source = source.Where(v => v.State == query.State);
        }

        List<Variable> rows = await source
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<VariableDto> dtos = rows
            .Select(GetVariableQueryHandler.Map)
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<VariableDto>, ListVariablesError>.Success(dtos);
    }
}
