using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Shared.Kernel.Tests;

public class StringValueObjectTests
{
    private sealed record Sample(string Value) : StringValueObject(Value);

    [Fact]
    public void ToString_returns_the_underlying_value()
    {
        Sample sample = new("payload");

        sample.ToString().ShouldBe("payload");
    }

    [Fact]
    public void Two_values_with_the_same_payload_are_equal()
    {
        Sample first = new("payload");
        Sample second = new("payload");

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }
}
