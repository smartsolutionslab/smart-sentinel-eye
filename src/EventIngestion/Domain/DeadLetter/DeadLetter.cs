using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// A rejected MQTT message captured so operators can post-mortem
/// without a redeploy (spec 006 FR-015). Audit-only — no fan-out.
/// </summary>
public sealed class DeadLetter : AggregateRoot<DeadLetterIdentifier>
{
    public string Topic { get; private set; } = string.Empty;

    public string RawPayload { get; private set; } = string.Empty;

    public string Error { get; private set; } = string.Empty;

    public DateTimeOffset RejectedAt { get; private set; }

    private DeadLetter() { }

    public static DeadLetter Capture(string topic, string rawPayload, string error, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(rawPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentNullException.ThrowIfNull(clock);
        return new DeadLetter
        {
            Id = DeadLetterIdentifier.New(),
            Topic = topic,
            RawPayload = rawPayload,
            Error = error,
            RejectedAt = clock.UtcNow,
        };
    }
}
