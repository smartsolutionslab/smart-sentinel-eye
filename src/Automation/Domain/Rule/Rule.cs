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
    /// <summary>
    /// The fab this rule belongs to (spec 013). Fixed at creation: a rule is
    /// never moved between fabs, because doing so would silently change which
    /// plant's events it acts on. Relocating means re-authoring.
    ///
    /// <para>
    /// Load-bearing for evaluation, not only for access. Before this existed,
    /// an event from one fab was matched against every fab's rules and the
    /// resulting change was attributed to the ingesting fab (#1252).
    /// </para>
    /// </summary>
    public FabIdentifier Fab { get; private set; } = null!;

    public RuleName Name { get; private set; } = null!;

    public TriggerSource TriggerSource { get; private set; } = null!;

    public TriggerKind TriggerKind { get; private set; } = null!;

    public RulePredicate Predicate { get; private set; } = null!;

    public RuleAction Action { get; private set; } = null!;

    public RuleState State { get; private set; } = null!;

    public CreatedAt CreatedAt { get; private set; } = null!;

    public OperatorIdentifier CreatedBy { get; private set; }

    public PublishedAt? PublishedAt { get; private set; }

    public ArchivedAt? ArchivedAt { get; private set; }

    private Rule() { }

    /// <summary>
    /// Mints a new rule in <see cref="RuleState.Draft"/>. Raises
    /// <see cref="RuleCreatedDomainEvent"/>.
    /// </summary>
    public static Rule Create(
        FabIdentifier fab,
        RuleName name,
        TriggerSource triggerSource,
        TriggerKind triggerKind,
        RulePredicate predicate,
        RuleAction action,
        OperatorIdentifier createdBy,
        IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        Ensure.That(predicate).IsNotNull();
        Ensure.That(action).IsNotNull();
        Ensure.That(clock).IsNotNull();
        Ensure.That(triggerSource).IsNotNull();
        Ensure.That(triggerKind).IsNotNull();

        DateTimeOffset now = clock.UtcNow;
        Rule rule = new()
        {
            Id = RuleIdentifier.New(),
            Fab = fab,
            Name = name,
            TriggerSource = triggerSource,
            TriggerKind = triggerKind,
            Predicate = predicate,
            Action = action,
            State = RuleState.Draft,
            CreatedAt = CreatedAt.From(now),
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
        Ensure.That(clock).IsNotNull();
        if (State == RuleState.Active)
        {
            return; // idempotent
        }

        if (State == RuleState.Archived)
        {
            throw new InvalidOperationException($"Rule {Id} is Archived; clone the rule to author a new one.");
        }
        State = RuleState.Active;
        PublishedAt = PublishedAt.From(clock.UtcNow);
        Raise(new RulePublishedDomainEvent(Id, Name, PublishedAt.Value));
    }

    /// <summary>
    /// Flips <see cref="RuleState.Draft"/> or
    /// <see cref="RuleState.Active"/> → <see cref="RuleState.Archived"/>.
    /// Idempotent on Archived (no event raised).
    /// </summary>
    public void Archive(IClock clock)
    {
        Ensure.That(clock).IsNotNull();
        if (State == RuleState.Archived)
        {
            return; // idempotent
        }

        State = RuleState.Archived;
        ArchivedAt = ArchivedAt.From(clock.UtcNow);
        Raise(new RuleArchivedDomainEvent(Id, Name, ArchivedAt.Value));
    }
}
