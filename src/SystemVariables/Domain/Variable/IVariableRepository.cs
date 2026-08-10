using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Variable repository contract (ADR-0041). Implementation lives in
/// SystemVariables.Infrastructure; the Domain layer has no
/// persistence dependency.
///
/// <para>
/// <see cref="GetByNameAsync"/> ignores Archived variables so a
/// recently-archived name is free for re-use by a fresh
/// <c>Define</c>.
/// </para>
///
/// <para>
/// <see cref="GetByNameAsync"/> takes a fab because a name is only unique
/// within one (spec 014). Without it the lookup is ambiguous the moment two
/// fabs use the same name — which is precisely what this feature allows.
/// </para>
/// </summary>
public interface IVariableRepository
{
    Task<Option<Variable>> GetByIdentifierAsync(VariableIdentifier variable, CancellationToken cancellationToken);

    Task<Option<Variable>> GetByNameAsync(FabIdentifier fab, VariableName name, CancellationToken cancellationToken);

    void Add(Variable variable);

    Task SaveAsync(CancellationToken cancellationToken);
}
