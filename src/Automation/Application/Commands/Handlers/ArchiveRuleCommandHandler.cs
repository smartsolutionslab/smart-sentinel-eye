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
        ArchiveRuleCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        Option<Rule> found = await rules
            .GetByNameAsync(command.Fab, command.Name, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(ArchiveRuleFailures.RuleNotFound(command.Name.Value));
        }

        Rule rule = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the rule that has
        // since moved. Checked before any mutation so nothing is applied on top
        // of stale intent.
        if (rule.Version != command.ExpectedVersion)
        {
            return Failure(ArchiveRuleFailures.RuleStale(command.Name.Value, command.ExpectedVersion, rule.Version));
        }
        rule.Archive(clock);
        await rules.SaveAsync(cancellationToken);

        // Live cache eviction so the next matching event is not
        // evaluated against the archived rule.
        cache.Remove(rule.Id);

        logger.ArchivedRule(rule.Id, command.Name);

        return Success(rule.Id);
    }
}
