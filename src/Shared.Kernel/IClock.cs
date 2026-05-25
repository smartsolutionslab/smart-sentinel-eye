namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Abstracts UtcNow so domain code is deterministic in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
