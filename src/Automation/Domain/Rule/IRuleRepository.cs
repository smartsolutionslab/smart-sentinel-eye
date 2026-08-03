using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Rule repository contract (ADR-0041). <see cref="GetByNameAsync"/>
/// ignores Archived rules so a recently-archived name is free for
/// re-use by a fresh <c>Create</c> (mirrors spec 005's
/// SystemVariables pattern).
///
/// <para>
/// <see cref="GetByNameAsync"/> takes a fab because a name is only unique
/// within one (spec 013). Without it a lookup could return another fab's
/// rule — and the caller would then compare its <c>If-Match</c> version
/// against the wrong aggregate entirely (ADR-0113).
/// </para>
/// </summary>
public interface IRuleRepository
{
    Task<Option<Rule>> GetByIdentifierAsync(RuleIdentifier rule, CancellationToken cancellationToken);

    Task<Option<Rule>> GetByNameAsync(FabIdentifier fab, RuleName name, CancellationToken cancellationToken);

    void Add(Rule rule);

    Task SaveAsync(CancellationToken cancellationToken);
}
