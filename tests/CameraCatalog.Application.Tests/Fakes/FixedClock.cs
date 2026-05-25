using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;

public sealed class FixedClock(DateTimeOffset moment) : IClock
{
    public DateTimeOffset UtcNow { get; } = moment;
}
