using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

public sealed class InMemoryVariableRepository : IVariableRepository
{
    private readonly List<Variable> _variables = new();

    public IReadOnlyList<Variable> Variables => _variables;

    public Task<Option<Variable>> GetByIdentifierAsync(VariableIdentifier variable, CancellationToken cancellationToken)
    {
        Variable? found = _variables.SingleOrDefault(v => v.Id == variable);
        return Task.FromResult(found is null ? Option<Variable>.None : Option<Variable>.Some(found));
    }

    public Task<Option<Variable>> GetByNameAsync(VariableName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        // Archived names are released for re-use; only return non-Archived rows.
        Variable? found = _variables.SingleOrDefault(v =>
            v.Name == name && v.State != VariableState.Archived);
        return Task.FromResult(found is null ? Option<Variable>.None : Option<Variable>.Some(found));
    }

    public void Add(Variable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        _variables.Add(variable);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (Variable v in _variables)
        {
            v.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
