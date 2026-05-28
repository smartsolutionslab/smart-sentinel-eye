using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule.Events;

public sealed record RulePublishedDomainEvent(
    RuleIdentifier Rule,
    RuleName Name,
    DateTimeOffset PublishedAt) : IDomainEvent;
