using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 403 (#3008). The recorded camera version is a value object because
/// constitution section II bans <c>int</c> as domain state, and it must order:
/// nothing orders a value object for free.
/// </summary>
public class SourceCameraVersionTests
{
    [Fact]
    public void A_version_keeps_the_value_it_was_made_from()
    {
        SourceCameraVersion.From(7).Value.ShouldBe(7);
        SourceCameraVersion.From(0).Value.ShouldBe(0);
        SourceCameraVersion.From(int.MaxValue).Value.ShouldBe(int.MaxValue);
    }

    [Fact]
    public void A_negative_version_is_refused()
    {
        Should.Throw<ArgumentException>(() => SourceCameraVersion.From(-1));
    }

    [Fact]
    public void Versions_order_by_value()
    {
        List<SourceCameraVersion> versions =
        [
            SourceCameraVersion.From(4),
            SourceCameraVersion.From(0),
            SourceCameraVersion.From(9),
        ];

        versions.Order().Select(version => version.Value).ShouldBe([0, 4, 9]);
    }

    [Fact]
    public void The_operators_agree_with_CompareTo()
    {
        var three = SourceCameraVersion.From(3);
        var four = SourceCameraVersion.From(4);
        var anotherThree = SourceCameraVersion.From(3);

        (three < four).ShouldBeTrue();
        (four < three).ShouldBeFalse();
        (three < anotherThree).ShouldBeFalse();
        (four > three).ShouldBeTrue();
        (three > four).ShouldBeFalse();
        (three > anotherThree).ShouldBeFalse();
        (three <= four).ShouldBeTrue();
        (three <= anotherThree).ShouldBeTrue();
        (four <= three).ShouldBeFalse();
        (four >= three).ShouldBeTrue();
        (three >= anotherThree).ShouldBeTrue();
        (three >= four).ShouldBeFalse();
        three.CompareTo(four).ShouldBeLessThan(0);
        four.CompareTo(three).ShouldBeGreaterThan(0);
        three.CompareTo(anotherThree).ShouldBe(0);
    }

    [Fact]
    public void Anything_sorts_after_nothing()
    {
        SourceCameraVersion.From(0).CompareTo(null).ShouldBe(1);
    }

    [Fact]
    public void Versions_with_the_same_value_are_equal()
    {
        SourceCameraVersion.From(3).ShouldBe(SourceCameraVersion.From(3));
        SourceCameraVersion.From(3).ShouldNotBe(SourceCameraVersion.From(4));
    }

    [Fact]
    public void A_version_unwraps_implicitly_to_an_int_and_prints_invariantly()
    {
        int unwrapped = SourceCameraVersion.From(12);

        unwrapped.ShouldBe(12);
        SourceCameraVersion.From(12).ToString().ShouldBe("12");
    }
}
