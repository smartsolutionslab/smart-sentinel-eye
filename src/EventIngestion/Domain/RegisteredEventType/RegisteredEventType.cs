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
    /// </summary>
    public static RegisteredEventType Register(
        FabIdentifier fab, Kind kind, OperatorIdentifier registeredBy, IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(kind).IsNotNull();
        Ensure.That(clock).IsNotNull();

        DateTimeOffset now = clock.UtcNow;
        RegisteredAt registeredAt = RegisteredAt.From(now);
        RegisteredEventType eventType = new()
        {
            Id = RegisteredEventTypeIdentifier.New(),
            Fab = fab,
            Kind = kind,
            State = RegistrationState.Registered,
            Registration = Registration.From(registeredAt, registeredBy),
        };

        eventType.Raise(new EventTypeRegisteredDomainEvent(eventType.Id, fab, kind, now, registeredBy));

        return eventType;
    }

    /// <summary>
    /// Idempotent by early return when already <see cref="RegistrationState.Retired"/>
    /// (mirrors <c>Variable.Archive</c> and <c>WebhookIntegration.Revoke</c>).
    /// </summary>
    public void Retire(OperatorIdentifier retiredBy, IClock clock)
    {
        Ensure.That(clock).IsNotNull();

        if (State == RegistrationState.Retired)
        {
            return; // idempotent
        }

        State = RegistrationState.Retired;
        Raise(new EventTypeRetiredDomainEvent(Id, Fab, Kind, clock.UtcNow, retiredBy));
    }
}
