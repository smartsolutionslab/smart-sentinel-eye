using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class HighlightDurationTests
{
    [Theory]
    [InlineData(HighlightDuration.MinimumMs)]
    [InlineData(10_000)]
    [InlineData(HighlightDuration.MaximumMs)]
    public void Accepts_durations_within_the_window(int milliseconds)
    {
        HighlightDuration.From(milliseconds).Value.ShouldBe(milliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(HighlightDuration.MinimumMs - 1)]
    [InlineData(HighlightDuration.MaximumMs + 1)]
    [InlineData(int.MinValue)]
    public void Rejects_durations_outside_it(int milliseconds)
    {
        Should.Throw<ArgumentException>(() => HighlightDuration.From(milliseconds));
    }

    [Fact]
    public void Two_windows_of_the_same_length_are_equal()
    {
        HighlightDuration.From(5_000).ShouldBe(HighlightDuration.From(5_000));
    }

    [Fact]
    public void Renders_as_its_millisecond_count()
    {
        HighlightDuration.From(5_000).ToString().ShouldBe("5000");
    }
}
