using System.Globalization;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class CreationTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Carries_both_halves()
    {
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Creation creation = Creation.From(CreatedAt.From(Moment), by);

        creation.At.Value.ShouldBe(Moment);
        creation.By.ShouldBe(by);
    }

    [Fact]
    public void Refuses_a_missing_moment()
    {
        Should.Throw<ArgumentException>(() =>
            Creation.From(null!, OperatorIdentifier.From(Guid.CreateVersion7())));
    }

    [Fact]
    public void Two_creations_of_the_same_moment_and_operator_are_equal()
    {
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Creation.From(CreatedAt.From(Moment), by)
            .ShouldBe(Creation.From(CreatedAt.From(Moment), by));
    }

    [Fact]
    public void A_different_operator_is_a_different_creation()
    {
        CreatedAt at = CreatedAt.From(Moment);

        Creation.From(at, OperatorIdentifier.From(Guid.CreateVersion7()))
            .ShouldNotBe(Creation.From(at, OperatorIdentifier.From(Guid.CreateVersion7())));
    }
}
