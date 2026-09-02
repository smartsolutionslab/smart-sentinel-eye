using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// The username of whoever caused an audited event, where the envelope
/// carried one. Absent for system-originated events.
/// </summary>
public sealed record ActorUsername : StringValueObject
{
    /// <summary>
    /// Matches the column width in the EF configuration. The two must agree: a
    /// bound narrower than its column refuses values the database would accept,
    /// and a wider one hands Postgres a write it will reject.
    /// </summary>
    public const int MaximumLength = 255;

    private ActorUsername(string value)
        : base(value)
    {
    }

    public static ActorUsername From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength);

        return new ActorUsername(value);
    }
}
