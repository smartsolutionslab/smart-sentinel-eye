using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 258 plan.md §2: <c>WallName</c> is a <c>StringValueObject</c> whose
/// <c>MaximumLength</c> mirrors <c>LayoutName</c>'s (mirrors
/// <c>LayoutNameTests</c>).
/// </summary>
public class WallNameTests
{
    [Fact]
    public void From_accepts_a_normal_name()
    {
        WallName name = WallName.From("Line 3 rotation");
        name.Value.ShouldBe("Line 3 rotation");
    }

    [Fact]
    public void From_trims_leading_and_trailing_whitespace()
    {
        WallName name = WallName.From("  Line 3 rotation  ");
        name.Value.ShouldBe("Line 3 rotation");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_rejects_blank_input(string raw)
    {
        Action act = () => WallName.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_rejects_input_above_the_maximum_length()
    {
        string tooLong = new('a', WallName.MaximumLength + 1);
        Action act = () => WallName.From(tooLong);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Names_differing_only_in_case_are_not_equal()
    {
        WallName upper = WallName.From("Line 3 Rotation");
        WallName lower = WallName.From("line 3 rotation");

        upper.ShouldNotBe(lower);
    }
}
