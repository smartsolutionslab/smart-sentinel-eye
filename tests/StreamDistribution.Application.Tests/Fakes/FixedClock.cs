using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

public sealed class FixedClock(DateTimeOffset moment) : IClock
{
    public DateTimeOffset UtcNow { get; } = moment;
}
