using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands.Handlers;

public sealed class CreateRuleCommandHandler(
    IRuleRepository rules,
    IClock clock,
    ILogger<CreateRuleCommandHandler> logger)
    : ICommandHandler<CreateRuleCommand, Result<RuleIdentifier, CreateRuleError>>
{
    public async Task<Result<RuleIdentifier, CreateRuleError>> HandleAsync(
        CreateRuleCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (var fab, var name, var triggerSource, var triggerKind, var predicate, var action, var createdBy) = command;

        // Name uniqueness (FR-002). Archived names are released for
        // re-use; the repository's GetByNameAsync ignores Archived.
        // Scoped to the fab: the same name in another fab is not a clash.
        var existing = await rules
            .GetByNameAsync(fab, name, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(CreateRuleFailures.RuleNameTaken(fab.Value, name.Value));
        }

        var refusal = RefuseUnparseable(predicate, action);
        if (refusal.HasValue)
        {
            return Failure(refusal.Value);
        }

        var rule = Rule.Create(
            fab, name, triggerSource, triggerKind,
            predicate, action, createdBy, clock);

        rules.Add(rule);
        await rules.SaveAsync(cancellationToken);

        logger.CreatedRule(rule.Id, name, triggerSource.Value, triggerKind.Value, createdBy);

        return Success(rule.Id);
    }

    private static Option<CreateRuleError> RefuseUnparseable(RulePredicate predicate, RuleAction action)
    {
        // Parse the predicate at command-time so a typo surfaces as a
        // 400 with position info rather than a runtime failure later.
        try
        {
            _ = AelParser.Parse(predicate.Value);
        }
        catch (AelParseException ex)
        {
            return Option<CreateRuleError>.Some(CreateRuleFailures.PredicateParseFailed(ex.Message, ex.Position));
        }

        // SetVariableValue's value expression is parsed up-front so
        // a typo surfaces as a typed 400 here. HighlightOverlay
        // actions carry no expression and skip this check.
        if (action is RuleAction.SetVariableValue setValue)
        {
            try
            {
                _ = AelParser.Parse(setValue.ValueExpression);
            }
            catch (AelParseException ex)
            {
                return Option<CreateRuleError>.Some(
                    CreateRuleFailures.ActionExpressionParseFailed(ex.Message, ex.Position));
            }
        }

        return Option<CreateRuleError>.None;
    }
}
