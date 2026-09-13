using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

/// <summary>
/// Aggregate root for a fab-scoped registry entry declaring an event
/// <see cref="Domain.Event.Kind"/> the fab expects (spec 143, decision 018).
/// Carries no <see cref="Source"/>: decision 018's strict mode "rejects
/// unknown event types", and its promotion path promotes into the registry,
/// singular (spec.md FR-001).
/// </summary>
public sealed class RegisteredEventType : AggregateRoot<RegisteredEventTypeIdentifier>
{
    public FabIdentifier Fab { get; private set; } = null!;

    public Kind Kind { get; private set; } = null!;

    public RegistrationState State { get; private set; } = null!;

    public Registration Registration { get; private set; } = null!;

    private RegisteredEventType() { }

    /// <summary>
    /// Mints a new registry entry. Raises
    /// <see cref="EventTypeRegisteredDomainEvent"/>.
    ///
    /// <para>
    /// Phase 4a prelude (spec 143 T001): <see cref="State"/> and
    /// <see cref="Registration"/> are the behaviour under test and are left
    /// at their <c>null!</c> defaults here; nothing is raised. T004 fills
    /// both in. The guards below survive unchanged into that fill-in.
    /// </para>
    /// </summary>
    public static RegisteredEventType Register(
        FabIdentifier fab, Kind kind, OperatorIdentifier registeredBy, IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(kind).IsNotNull();
        Ensure.That(clock).IsNotNull();

        RegisteredEventType eventType = new()
        {
            Id = RegisteredEventTypeIdentifier.New(),
            Fab = fab,
            Kind = kind,
        };

        return eventType;
    }

    /// <summary>
    /// Idempotent by early return when already <see cref="RegistrationState.Retired"/>
    /// (mirrors <c>Variable.Archive</c> and <c>WebhookIntegration.Revoke</c>).
    ///
    /// <para>
    /// Phase 4a prelude (spec 143 T001): the body is the guard below and
    /// nothing else — no flip, no idempotent check, nothing raised. T004
    /// fills it in.
    /// </para>
    /// </summary>
    public void Retire(OperatorIdentifier retiredBy, IClock clock)
    {
        Ensure.That(clock).IsNotNull();

        // Touches instance state so CA1822/S2325 do not flag this as static
        // while the behaviour they would otherwise be marking correctly as
        // "does nothing yet" is still withheld (spec 143 T001).
        _ = Id;
    }
}
