using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Fakes;

public sealed class FakeClock : IClock
{
    private DateTimeOffset now;

    public FakeClock(DateTimeOffset now) => this.now = now;

    public DateTimeOffset UtcNow => now;

    public void Advance(TimeSpan by) => now = now.Add(by);
}
