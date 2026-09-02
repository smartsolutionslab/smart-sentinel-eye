using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands;

/// <summary>
/// Creates a new rule in <see cref="RuleState.Draft"/> (spec 007
/// US1 / FR-023). Both <see cref="Predicate"/> and any
/// expression-bearing <see cref="Action"/> are parsed at
/// command-time; parse failures surface as typed
/// <see cref="CreateRuleError.PredicateParseFailed"/> /
/// <see cref="CreateRuleError.ActionExpressionParseFailed"/>.
/// </summary>
public sealed record CreateRuleCommand(
    FabIdentifier Fab,
    RuleName Name,
    TriggerSource TriggerSource,
    TriggerKind TriggerKind,
    RulePredicate Predicate,
    RuleAction Action,
    OperatorIdentifier CreatedBy)
    : ICommand<Result<RuleIdentifier, CreateRuleError>>;
