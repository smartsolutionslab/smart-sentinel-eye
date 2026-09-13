using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.Events;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.RegisteredEventType;

/// <summary>
/// Phase 4a (spec 143 T003a). Exercises the aggregate against the
/// signatures T001 introduced with their behaviour withheld — every
/// assertion here is expected to fail until T004 fills the bodies in,
/// except the two null-guard cases (T001 wrote those guards already; see
/// each test's remark).
/// </summary>
public class RegisteredEventTypeTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Register_puts_the_type_in_the_Registered_state()
    {
        Domain.RegisteredEventType.RegisteredEventType eventType = new RegisteredEventTypeBuilder().Build();

        eventType.State.ShouldBe(Domain.RegisteredEventType.RegistrationState.Registered);
    }

    [Fact]
    public void Register_records_who_registered_it_and_when()
    {
        OperatorIdentifier registeredBy = OperatorIdentifier.From(Guid.CreateVersion7());

        Domain.RegisteredEventType.RegisteredEventType eventType = new RegisteredEventTypeBuilder()
            .RegisteredBy(registeredBy)
            .At(Now)
            .Build();

        eventType.Registration.ShouldBe(
            Domain.RegisteredEventType.Registration.From(
                Domain.RegisteredEventType.RegisteredAt.From(Now), registeredBy));
    }

    [Fact]
    public void Register_raises_an_EventTypeRegisteredDomainEvent_naming_the_fab_and_kind()
    {
        Domain.RegisteredEventType.RegisteredEventType eventType = new RegisteredEventTypeBuilder()
            .WithFab("dresden")
            .WithKind("PersonInRestrictedZone")
            .Build();

        EventTypeRegisteredDomainEvent raised = eventType.PendingEvents
            .OfType<EventTypeRegisteredDomainEvent>()
            .ShouldHaveSingleItem();

        raised.Fab.ShouldBe(FabIdentifier.From("dresden"));
        raised.Kind.ShouldBe(Kind.From("PersonInRestrictedZone"));
    }

    [Fact]
    public void Retire_flips_the_state_to_Retired()
    {
        Domain.RegisteredEventType.RegisteredEventType eventType = new RegisteredEventTypeBuilder().Build();

        eventType.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));

        eventType.State.ShouldBe(Domain.RegisteredEventType.RegistrationState.Retired);
    }

    [Fact]
    public void Retire_raises_an_EventTypeRetiredDomainEvent()
    {
        Domain.RegisteredEventType.RegisteredEventType eventType = new RegisteredEventTypeBuilder().Build();
        eventType.ClearPendingEvents();

        eventType.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));

        eventType.PendingEvents.OfType<EventTypeRetiredDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Retire_is_idempotent_on_an_already_retired_type()
    {
        Domain.RegisteredEventType.RegisteredEventType eventType = new RegisteredEventTypeBuilder().Build();
        eventType.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));
        eventType.ClearPendingEvents();

        eventType.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(2)));

        eventType.State.ShouldBe(Domain.RegisteredEventType.RegistrationState.Retired);
        eventType.PendingEvents.ShouldBeEmpty("a repeat retire must not raise a second event");
    }

    /// <summary>
    /// T001 wrote these guards fully — they are the plumbing, not the
    /// behaviour under test — so both are expected to pass on the first
    /// run (spec 143 tasks.md T003a.7).
    /// </summary>
    [Fact]
    public void Register_refuses_a_null_fab()
    {
        Should.Throw<ArgumentNullException>(() => Domain.RegisteredEventType.RegisteredEventType.Register(
            null!,
            Kind.From("PersonInRestrictedZone"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now)));
    }

    /// <summary>Expected to pass on the first run — see <see cref="Register_refuses_a_null_fab"/>.</summary>
    [Fact]
    public void Register_refuses_a_null_kind()
    {
        Should.Throw<ArgumentNullException>(() => Domain.RegisteredEventType.RegisteredEventType.Register(
            FabIdentifier.From("dresden"),
            null!,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now)));
    }
}
