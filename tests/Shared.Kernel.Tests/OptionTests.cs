using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Shared.Kernel.Tests;

public class OptionTests
{
    [Fact]
    public void Some_with_value_has_HasValue_true_and_returns_the_value()
    {
        Option<string> option = Option<string>.Some("camera-1");

        option.HasValue.ShouldBeTrue();
        option.Value.ShouldBe("camera-1");
    }

    [Fact]
    public void None_has_no_value_and_accessing_Value_throws()
    {
        Option<string> option = Option<string>.None;

        option.HasValue.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => _ = option.Value);
    }

    [Fact]
    public void Some_with_null_throws()
    {
        Should.Throw<ArgumentNullException>(() => Option<string>.Some(null!));
    }

    [Fact]
    public void Match_runs_the_some_branch_when_a_value_is_present()
    {
        Option<int> option = Option<int>.Some(42);

        string result = option.Match(v => $"got-{v}", () => "none");

        result.ShouldBe("got-42");
    }

    [Fact]
    public void Match_runs_the_none_branch_when_empty()
    {
        Option<int> option = Option<int>.None;

        string result = option.Match(v => $"got-{v}", () => "none");

        result.ShouldBe("none");
    }

    [Fact]
    public void Map_transforms_a_present_value()
    {
        Option<int> option = Option<int>.Some(5);

        Option<string> mapped = option.Map(v => $"#{v}");

        mapped.HasValue.ShouldBeTrue();
        mapped.Value.ShouldBe("#5");
    }

    [Fact]
    public void Map_propagates_None()
    {
        Option<int> option = Option<int>.None;

        Option<string> mapped = option.Map(v => $"#{v}");

        mapped.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void GetOrDefault_returns_value_when_present_and_fallback_otherwise()
    {
        Option<string>.Some("a").GetOrDefault("fallback").ShouldBe("a");
        Option<string>.None.GetOrDefault("fallback").ShouldBe("fallback");
    }

    [Fact]
    public void Two_Somes_with_equal_values_are_equal()
    {
        Option<int> first = Option<int>.Some(7);
        Option<int> second = Option<int>.Some(7);

        first.ShouldBe(second);
        (first == second).ShouldBeTrue();
        (first != second).ShouldBeFalse();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Some_and_None_are_not_equal()
    {
        Option<int> some = Option<int>.Some(1);
        Option<int> none = Option<int>.None;

        some.ShouldNotBe(none);
        (some == none).ShouldBeFalse();
        (some != none).ShouldBeTrue();
    }

    [Fact]
    public void Two_Nones_are_equal()
    {
        Option<int> first = Option<int>.None;
        Option<int> second = Option<int>.None;

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_object_overload_returns_false_for_non_option_argument()
    {
        Option<int> some = Option<int>.Some(1);

        some.Equals((object)"not an option").ShouldBeFalse();
    }

    [Fact]
    public void ToString_distinguishes_Some_from_None()
    {
        Option<int>.Some(5).ToString().ShouldBe("Some(5)");
        Option<int>.None.ToString().ShouldBe("None");
    }
}
