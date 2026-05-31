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
        ArgumentNullException.ThrowIfNull(command);

        var (name, triggerSource, triggerKind, predicate, action, createdBy) = command;

        // Name uniqueness (FR-002). Archived names are released for
        // re-use; the repository's GetByNameAsync ignores Archived.
        Option<Rule> existing = await rules
            .GetByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<RuleIdentifier, CreateRuleError>.Failure(
                new CreateRuleError.RuleNameTaken(name.Value));
        }

        // Parse the predicate at command-time so a typo surfaces as a
        // 400 with position info rather than a runtime failure later.
        try
        {
            _ = AelParser.Parse(predicate.Value);
        }
        catch (AelParseException ex)
        {
            return Result<RuleIdentifier, CreateRuleError>.Failure(
                new CreateRuleError.PredicateParseFailed(ex.Message, ex.Position));
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
                return Result<RuleIdentifier, CreateRuleError>.Failure(
                    new CreateRuleError.ActionExpressionParseFailed(ex.Message, ex.Position));
            }
        }

        Rule rule = Rule.Create(
            name, triggerSource, triggerKind,
            predicate, action, createdBy, clock);

        rules.Add(rule);
        await rules.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.CreatedRule(logger, rule.Id, name, triggerSource, triggerKind, createdBy);

        return Result<RuleIdentifier, CreateRuleError>.Success(rule.Id);
    }
}
