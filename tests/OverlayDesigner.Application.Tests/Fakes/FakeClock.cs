using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;

public sealed class FakeClock(DateTimeOffset moment) : IClock
{
    public DateTimeOffset UtcNow { get; } = moment;
}
