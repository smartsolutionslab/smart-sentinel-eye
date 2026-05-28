using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Tests.Fakes;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
