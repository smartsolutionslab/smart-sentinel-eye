using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

/// <summary>
/// Read-side seam: hands query handlers an EF Core <see cref="IQueryable{T}"/>
/// over the system_variables table. <c>AsNoTracking</c> by default.
/// </summary>
public sealed class VariableQuerySource(SystemVariablesDbContext dbContext) : IVariableQuerySource
{
    public IQueryable<Variable> Variables =>
        dbContext.Variables.AsNoTracking();
}
