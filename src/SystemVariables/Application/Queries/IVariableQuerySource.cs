using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Read-side IQueryable seam for the Variable aggregate (ADR-0041).
/// Infrastructure provides a concrete impl backed by the DbContext;
/// Application stays EF-Core-free at the call site so query-handler
/// tests can substitute an in-memory IQueryable.
/// </summary>
public interface IVariableQuerySource
{
    IQueryable<Variable> Variables { get; }
}
