namespace SmartSentinelEye.StreamDistribution.Infrastructure;

/// <summary>
/// Spec 409 (#3013): the throttle rule for a Warning a periodic caller would
/// otherwise repeat on every tick. A first or changed outcome warns; the same
/// outcome repeats at most every ten minutes, measured on the monotonic
/// timestamp. Safe to share across threads, because one instance can be a
/// singleton used by every scope.
/// </summary>
internal sealed class WarningRepeatGate<TOutcome>(TimeProvider timeProvider)
    where TOutcome : struct, IEquatable<TOutcome>
{
    private readonly TimeSpan repeatInterval = TimeSpan.FromMinutes(10);

    private readonly Lock gate = new();
    private TOutcome? lastOutcome;
    private long lastWarnedAt;

    public bool ShouldWarn(TOutcome outcome)
    {
        lock (gate)
        {
            if (lastOutcome is { } last
                && last.Equals(outcome)
                && timeProvider.GetElapsedTime(lastWarnedAt) < repeatInterval)
            {
                return false;
            }

            lastOutcome = outcome;
            lastWarnedAt = timeProvider.GetTimestamp();
            return true;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            lastOutcome = null;
        }
    }
}
