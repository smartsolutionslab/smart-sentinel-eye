using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class ProvisioningTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Carries_both_halves()
    {
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Provisioning provisioning = Provisioning.From(ProvisionedAt.From(Moment), by);

        provisioning.At.Value.ShouldBe(Moment);
        provisioning.By.ShouldBe(by);
    }

    [Fact]
    public void Refuses_a_missing_moment()
    {
        Should.Throw<ArgumentException>(() =>
            Provisioning.From(null, OperatorIdentifier.From(Guid.CreateVersion7())));
    }

    [Fact]
    public void Two_provisionings_of_the_same_moment_and_operator_are_equal()
    {
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Provisioning.From(ProvisionedAt.From(Moment), by)
            .ShouldBe(Provisioning.From(ProvisionedAt.From(Moment), by));
    }

    [Fact]
    public void A_different_operator_is_a_different_provisioning()
    {
        ProvisionedAt at = ProvisionedAt.From(Moment);

        Provisioning.From(at, OperatorIdentifier.From(Guid.CreateVersion7()))
            .ShouldNotBe(Provisioning.From(at, OperatorIdentifier.From(Guid.CreateVersion7())));
    }
}
