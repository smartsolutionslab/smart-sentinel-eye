using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule.Events;

public sealed record RuleArchivedDomainEvent(
    RuleIdentifier Rule,
    RuleName Name,
    DateTimeOffset ArchivedAt) : IDomainEvent;
