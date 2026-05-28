using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule.Fakes;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
