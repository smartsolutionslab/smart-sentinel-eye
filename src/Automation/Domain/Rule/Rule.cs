using SmartSentinelEye.Automation.Domain.Rule.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Aggregate root for an automation rule (spec 007). Three-state
/// lifecycle: <c>Draft → Active → Archived</c>. The only path back
/// to <c>Draft</c> is to clone the rule (preserves the audit
/// trail); see spec FR-003.
///
/// <para>
/// Trigger source + kind are stored as plain strings — Automation
/// never references EventIngestion's <c>Source</c> / <c>Kind</c>
/// VOs. The Application layer parses + validates them when an
/// event flows through.
/// </para>
/// </summary>
public sealed class Rule : AggregateRoot<RuleIdentifier>
{
    public RuleName Name { get; private set; } = null!;

    public string TriggerSource { get; private set; } = string.Empty;

    public string TriggerKind { get; private set; } = string.Empty;

    public RulePredicate Predicate { get; private set; } = null!;

    public RuleAction Action { get; private set; } = null!;

    public RuleState State { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public OperatorIdentifier CreatedBy { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    private Rule() { }

    /// <summary>
    /// Mints a new rule in <see cref="RuleState.Draft"/>. Raises
    /// <see cref="RuleCreatedDomainEvent"/>.
    /// </summary>
    public static Rule Create(
        RuleName name,
        string triggerSource,
        string triggerKind,
        RulePredicate predicate,
        RuleAction action,
        OperatorIdentifier createdBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerKind);

        DateTimeOffset now = clock.UtcNow;
        Rule rule = new()
        {
            Id = RuleIdentifier.New(),
            Name = name,
            TriggerSource = triggerSource,
            TriggerKind = triggerKind,
            Predicate = predicate,
            Action = action,
            State = RuleState.Draft,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
        rule.Raise(new RuleCreatedDomainEvent(
            rule.Id, name, triggerSource, triggerKind, now, createdBy));
        return rule;
    }

    /// <summary>
    /// Flips <see cref="RuleState.Draft"/> → <see cref="RuleState.Active"/>.
    /// Idempotent on Active. Throws if Archived.
    /// </summary>
    public void Publish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State == RuleState.Active) return; // idempotent
        if (State == RuleState.Archived)
        {
            throw new InvalidOperationException(
                $"Rule {Id} is Archived; clone the rule to author a new one.");
        }
        State = RuleState.Active;
        PublishedAt = clock.UtcNow;
        Raise(new RulePublishedDomainEvent(Id, Name, PublishedAt.Value));
    }

    /// <summary>
    /// Flips <see cref="RuleState.Draft"/> or
    /// <see cref="RuleState.Active"/> → <see cref="RuleState.Archived"/>.
    /// Idempotent on Archived (no event raised).
    /// </summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State == RuleState.Archived) return; // idempotent
        State = RuleState.Archived;
        ArchivedAt = clock.UtcNow;
        Raise(new RuleArchivedDomainEvent(Id, Name, ArchivedAt.Value));
    }
}
