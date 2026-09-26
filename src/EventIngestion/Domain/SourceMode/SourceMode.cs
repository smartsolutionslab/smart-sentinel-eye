using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode;

/// <summary>
/// Aggregate root declaring the event-type admission policy for one
/// <c>(fab, Source)</c> pair (decision 018, spec.md FR-001/FR-002). Not a
/// column on <c>RegisteredEventType</c>, which is keyed <c>(fab, kind)</c>
/// and carries no <see cref="Source"/>; not a field on <c>WebhookIntegration</c>,
/// which is a credential that MQTT and manual ingress never touch
/// (spec.md §9 A1).
/// </summary>
public sealed class SourceMode : AggregateRoot<SourceModeIdentifier>
{
    public FabIdentifier Fab { get; private set; } = null!;

    public Source Source { get; private set; } = null!;

    public EventTypeMode Mode { get; private set; } = null!;

    public Declaration Declaration { get; private set; } = null!;

    private SourceMode() { }

    /// <summary>
    /// Mints a new declaration.
    ///
    /// <para>
    /// Withheld for phase A (tasks.md T001): leaves <see cref="Mode"/> and
    /// <see cref="Declaration"/> at <c>null!</c> and raises nothing. Phase C
    /// (T004) fills in the assignment and the
    /// <see cref="SourceModeDeclaredDomainEvent"/>.
    /// </para>
    /// </summary>
    public static SourceMode Declare(
        FabIdentifier fab, Source source, EventTypeMode mode, OperatorIdentifier declaredBy, IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(source).IsNotNull();
        Ensure.That(mode).IsNotNull();
        Ensure.That(clock).IsNotNull();

        SourceMode sourceMode = new()
        {
            Id = SourceModeIdentifier.New(),
            Fab = fab,
            Source = source,
        };

        return sourceMode;
    }

    /// <summary>
    /// Changes the declared mode.
    ///
    /// <para>
    /// Withheld for phase A (tasks.md T001): guards only. Phase C (T004)
    /// fills in the idempotent early return, the flip and the
    /// <see cref="SourceModeChangedDomainEvent"/> (spec.md FR-011).
    /// </para>
    /// </summary>
    public void Change(EventTypeMode mode, OperatorIdentifier changedBy, IClock clock)
    {
        Ensure.That(mode).IsNotNull();
        Ensure.That(clock).IsNotNull();

        // Touches instance state so this stays an instance method rather than
        // one CA1822/S2325 would flag as static — T004 fills in the idempotent
        // comparison against this same property (plan.md §2).
        _ = Mode;
    }
}
