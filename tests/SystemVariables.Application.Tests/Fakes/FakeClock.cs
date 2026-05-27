using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

public sealed class FakeClock(DateTimeOffset moment) : IClock
{
    public DateTimeOffset UtcNow { get; } = moment;
}
