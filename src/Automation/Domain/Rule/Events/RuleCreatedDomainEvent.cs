using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule.Events;

public sealed record RuleCreatedDomainEvent(
    RuleIdentifier Rule,
    RuleName Name,
    string TriggerSource,
    string TriggerKind,
    DateTimeOffset CreatedAt,
    OperatorIdentifier CreatedBy) : IDomainEvent;
