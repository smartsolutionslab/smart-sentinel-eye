using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands.Handlers;

public sealed class PublishRuleCommandHandler(
    IRuleRepository rules,
    IRuleCache cache,
    IClock clock,
    ILogger<PublishRuleCommandHandler> logger)
    : ICommandHandler<PublishRuleCommand, Result<RuleIdentifier, PublishRuleError>>
{
    public async Task<Result<RuleIdentifier, PublishRuleError>> HandleAsync(
        PublishRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Rule> found = await rules
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<RuleIdentifier, PublishRuleError>.Failure(
                new PublishRuleError.RuleNotFound(command.Name.Value));
        }

        Rule rule = found.Value;
        try
        {
            rule.Publish(clock);
        }
        catch (InvalidOperationException)
        {
            return Result<RuleIdentifier, PublishRuleError>.Failure(
                new PublishRuleError.RuleAlreadyArchived(command.Name.Value));
        }

        await rules.SaveAsync(cancellationToken).ConfigureAwait(false);

        // Live cache add so the rule is evaluated against the next
        // incoming event without waiting for a process restart.
        cache.Upsert(rule);

        Log.PublishedRule(logger, rule.Id, command.Name);

        return Result<RuleIdentifier, PublishRuleError>.Success(rule.Id);
    }
}
