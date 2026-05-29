using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.Fakes;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
