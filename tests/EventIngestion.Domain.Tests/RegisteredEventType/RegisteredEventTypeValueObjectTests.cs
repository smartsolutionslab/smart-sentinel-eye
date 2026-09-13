using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using RegisteredAt = SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.RegisteredAt;
using Registration = SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.Registration;
using RegisteredEventTypeIdentifier = SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.RegisteredEventTypeIdentifier;
using RegistrationState = SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.RegistrationState;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.RegisteredEventType;

/// <summary>
/// Phase 4a (spec 143 T003b). The four value objects.
///
/// <para>
/// Not every test here is red on arrival. T001 marks
/// <c>RegisteredEventTypeIdentifier</c>, the two domain events and
/// <c>Registration.From</c> "fully implemented — nothing to withhold" (plan.md
/// §9), and <c>RegisteredAt</c>'s ordering was never named as withheld either —
/// only its UTC normalisation is. A test against any of those is exercising
/// already-correct plumbing, the same category tasks.md's T003a.7 names for
/// the aggregate's null guards. See the phase 4a report for the full,
/// verified list — tasks.md's own green-on-arrival list (T003a.7, T003e.13)
/// does not enumerate these.
/// </para>
/// </summary>
public class RegisteredEventTypeValueObjectTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("Registered")]
    [InlineData("Retired")]
    public void RegistrationState_From_round_trips_each_known_state(string wire)
    {
        RegistrationState.From(wire).Value.ShouldBe(wire);
    }

    [Fact]
    public void RegistrationState_From_refuses_an_unknown_state()
    {
        Should.Throw<ArgumentException>(() => RegistrationState.From("Bogus"));
    }

    [Fact]
    public void RegisteredAt_stores_the_instant_in_UTC()
    {
        DateTimeOffset nonUtc = new(2026, 5, 28, 10, 14, 33, TimeSpan.FromHours(2));

        RegisteredAt at = RegisteredAt.From(nonUtc);

        at.Value.Offset.ShouldBe(TimeSpan.Zero, "a non-UTC instant must come back UTC");
        at.Value.ShouldBe(nonUtc.ToUniversalTime());
    }

    /// <summary>
    /// Green on arrival: <c>CompareTo</c> and the comparison operators compare
    /// <c>DateTimeOffset</c> instants, which is offset-agnostic in .NET — this
    /// was never part of the UTC-normalisation behaviour T001 withholds.
    /// </summary>
    [Fact]
    public void RegisteredAt_orders_by_instant()
    {
        RegisteredAt earlier = RegisteredAt.From(Now);
        RegisteredAt later = RegisteredAt.From(Now.AddMinutes(1));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }

    /// <summary>
    /// Green on arrival: <c>Registration.From</c> is "the guards and the
    /// construction, nothing else" per T001 — there is no behaviour to
    /// withhold, so this guard is already correct.
    /// </summary>
    [Fact]
    public void Registration_refuses_a_null_registeredAt()
    {
        Should.Throw<ArgumentNullException>(() => Registration.From(
            null!, OperatorIdentifier.From(Guid.CreateVersion7())));
    }

    /// <summary>
    /// Green on arrival: T001 marks the identifier "fully implemented — copy
    /// WebhookIntegrationIdentifier; it has no behaviour to withhold".
    /// </summary>
    [Fact]
    public void RegisteredEventTypeIdentifier_New_is_a_version_7_guid()
    {
        RegisteredEventTypeIdentifier id = RegisteredEventTypeIdentifier.New();

        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }
}
