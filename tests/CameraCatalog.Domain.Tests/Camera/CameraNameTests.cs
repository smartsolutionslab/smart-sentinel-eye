using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

public class CameraNameTests
{
    [Fact]
    public void From_with_a_valid_name_returns_the_trimmed_value()
    {
        var name = CameraName.From("  Line-1-Entrance  ");

        name.Value.ShouldBe("Line-1-Entrance");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void From_with_empty_or_whitespace_input_fails(string input)
    {
        Action act = () => CameraName.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_with_input_longer_than_the_maximum_fails()
    {
        string tooLong = new('x', CameraName.MaximumLength + 1);

        Action act = () => CameraName.From(tooLong);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_with_input_at_the_exact_maximum_length_succeeds()
    {
        string atLimit = new('x', CameraName.MaximumLength);

        var name = CameraName.From(atLimit);

        name.Value.Length.ShouldBe(CameraName.MaximumLength);
    }

    [Fact]
    public void Two_names_differing_only_in_case_are_equal()
    {
        var upper = CameraName.From("LINE-1-ENTRANCE");
        var lower = CameraName.From("line-1-entrance");
        var mixed = CameraName.From("Line-1-Entrance");

        upper.ShouldBe(lower);
        upper.ShouldBe(mixed);
        upper.GetHashCode().ShouldBe(lower.GetHashCode());
    }

    [Fact]
    public void Names_preserve_their_original_casing_for_display()
    {
        var name = CameraName.From("Line-1-Entrance");

        name.Value.ShouldBe("Line-1-Entrance");
        name.ToString().ShouldBe("Line-1-Entrance");
    }

    [Fact]
    public void NormalizedValue_is_uppercase_invariant_for_uniqueness_comparison()
    {
        var name = CameraName.From("Line-1-Entrance");

        name.NormalizedValue.ShouldBe("LINE-1-ENTRANCE");
    }

    [Fact]
    public void Names_with_different_letters_are_not_equal()
    {
        var first = CameraName.From("Cam-A");
        var second = CameraName.From("Cam-B");

        first.ShouldNotBe(second);
    }

    [Fact]
    public void CompareTo_orders_by_normalized_value_so_case_does_not_affect_sort()
    {
        var apple = CameraName.From("apple");
        var banana = CameraName.From("Banana");
        var cherry = CameraName.From("cherry");

        // APPLE < BANANA < CHERRY in normalized form
        apple.CompareTo(banana).ShouldBeLessThan(0);
        banana.CompareTo(cherry).ShouldBeLessThan(0);
        cherry.CompareTo(apple).ShouldBeGreaterThan(0);
        apple.CompareTo(CameraName.From("APPLE")).ShouldBe(0);
    }

    [Fact]
    public void CompareTo_null_returns_positive()
    {
        var name = CameraName.From("Cam-A");

        name.CompareTo(null).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Comparison_operators_use_normalized_value()
    {
        var apple = CameraName.From("apple");
        var banana = CameraName.From("Banana");

        (apple < banana).ShouldBeTrue();
        (banana > apple).ShouldBeTrue();
        (apple <= CameraName.From("APPLE")).ShouldBeTrue();
        (banana >= apple).ShouldBeTrue();
    }
}
