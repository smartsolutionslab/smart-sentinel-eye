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
    /// Mints a new declaration. Raises <see cref="SourceModeDeclaredDomainEvent"/>.
    /// </summary>
    public static SourceMode Declare(
        FabIdentifier fab, Source source, EventTypeMode mode, OperatorIdentifier declaredBy, IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(source).IsNotNull();
        Ensure.That(mode).IsNotNull();
        Ensure.That(clock).IsNotNull();

        DateTimeOffset now = clock.UtcNow;
        DeclaredAt declaredAt = DeclaredAt.From(now);
        SourceMode sourceMode = new()
        {
            Id = SourceModeIdentifier.New(),
            Fab = fab,
            Source = source,
            Mode = mode,
            Declaration = Declaration.From(declaredAt, declaredBy),
        };

        sourceMode.Raise(new SourceModeDeclaredDomainEvent(sourceMode.Id, fab, source, mode, now, declaredBy));

        return sourceMode;
    }

    /// <summary>
    /// Changes the declared mode. Idempotent by early return when the mode is
    /// unchanged (spec.md FR-011): no event, no field assignment, so the
    /// version does not bump.
    /// </summary>
    public void Change(EventTypeMode mode, OperatorIdentifier changedBy, IClock clock)
    {
        Ensure.That(mode).IsNotNull();
        Ensure.That(clock).IsNotNull();

        if (mode == Mode)
        {
            return; // idempotent
        }

        EventTypeMode previousMode = Mode;
        DateTimeOffset now = clock.UtcNow;
        Mode = mode;
        Declaration = Declaration.From(DeclaredAt.From(now), changedBy);

        Raise(new SourceModeChangedDomainEvent(Id, Fab, Source, previousMode, mode, now, changedBy));
    }
}
