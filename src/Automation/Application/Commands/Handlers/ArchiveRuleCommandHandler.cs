using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands.Handlers;

public sealed class ArchiveRuleCommandHandler(
    IRuleRepository rules,
    IRuleCache cache,
    IClock clock,
    ILogger<ArchiveRuleCommandHandler> logger)
    : ICommandHandler<ArchiveRuleCommand, Result<RuleIdentifier, ArchiveRuleError>>
{
    public async Task<Result<RuleIdentifier, ArchiveRuleError>> HandleAsync(
        ArchiveRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Rule> found = await rules
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<RuleIdentifier, ArchiveRuleError>.Failure(
                new ArchiveRuleError.RuleNotFound(command.Name.Value));
        }

        Rule rule = found.Value;
        rule.Archive(clock);
        await rules.SaveAsync(cancellationToken).ConfigureAwait(false);

        // Live cache eviction so the next matching event is not
        // evaluated against the archived rule.
        cache.Remove(rule.Id);

        Log.ArchivedRule(logger, rule.Id, command.Name);

        return Result<RuleIdentifier, ArchiveRuleError>.Success(rule.Id);
    }
}
