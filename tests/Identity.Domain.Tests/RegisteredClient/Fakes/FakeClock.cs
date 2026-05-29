using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient.Fakes;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
