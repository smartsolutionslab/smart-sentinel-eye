using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

public sealed class InMemoryVariableRepository : IVariableRepository
{
    private readonly List<Variable> _variables = [];

    public IReadOnlyList<Variable> Variables => _variables;

    public Task<Option<Variable>> GetByIdentifierAsync(VariableIdentifier variable, CancellationToken cancellationToken)
    {
        Variable? found = _variables.SingleOrDefault(v => v.Id == variable);
        return Task.FromResult(found is null ? Option<Variable>.None : Option<Variable>.Some(found));
    }

    public Task<Option<Variable>> GetByNameAsync(FabIdentifier fab, VariableName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        // Archived names are released for re-use; only return non-Archived rows.
        // Fab is part of the match, not a filter applied afterwards: keyed on
        // the name alone this SingleOrDefault would throw the moment two fabs
        // use one name, which is the whole point of the feature.
        Variable? found = _variables.SingleOrDefault(v =>
            v.Fab == fab && v.Name == name && v.State != VariableState.Archived);
        return Task.FromResult(found is null ? Option<Variable>.None : Option<Variable>.Some(found));
    }

    public void Add(Variable variable)
    {
        Ensure.That(variable).IsNotNull();
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
