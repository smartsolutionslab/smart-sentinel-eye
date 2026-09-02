using System.Globalization;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

public class RegistrationTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-25T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Carries_both_halves()
    {
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Registration registration = Registration.From(RegisteredAt.From(Moment), by);

        registration.At.Value.ShouldBe(Moment);
        registration.By.ShouldBe(by);
    }

    [Fact]
    public void Refuses_a_missing_moment()
    {
        Should.Throw<ArgumentException>(() =>
            Registration.From(null!, OperatorIdentifier.From(Guid.CreateVersion7())));
    }

    [Fact]
    public void Two_registrations_of_the_same_moment_and_operator_are_equal()
    {
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Registration.From(RegisteredAt.From(Moment), by)
            .ShouldBe(Registration.From(RegisteredAt.From(Moment), by));
    }

    [Fact]
    public void A_different_operator_is_a_different_registration()
    {
        RegisteredAt at = RegisteredAt.From(Moment);

        Registration.From(at, OperatorIdentifier.From(Guid.CreateVersion7()))
            .ShouldNotBe(Registration.From(at, OperatorIdentifier.From(Guid.CreateVersion7())));
    }
}
