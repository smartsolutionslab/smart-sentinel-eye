using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Rule repository contract (ADR-0041). <see cref="GetByNameAsync"/>
/// ignores Archived rules so a recently-archived name is free for
/// re-use by a fresh <c>Create</c> (mirrors spec 005's
/// SystemVariables pattern).
/// </summary>
public interface IRuleRepository
{
    Task<Option<Rule>> GetByIdentifierAsync(RuleIdentifier rule, CancellationToken cancellationToken);

    Task<Option<Rule>> GetByNameAsync(RuleName name, CancellationToken cancellationToken);

    void Add(Rule rule);

    Task SaveAsync(CancellationToken cancellationToken);
}
