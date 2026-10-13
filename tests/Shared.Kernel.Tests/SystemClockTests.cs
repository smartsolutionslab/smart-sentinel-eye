namespace SmartSentinelEye.Shared.Kernel.Tests;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_returns_a_value_close_to_real_wall_time()
    {
        SystemClock clock = new();
        var before = DateTimeOffset.UtcNow;

        var reading = clock.UtcNow;

        var after = DateTimeOffset.UtcNow;
        reading.ShouldBeGreaterThanOrEqualTo(before);
        reading.ShouldBeLessThanOrEqualTo(after);
    }
}
