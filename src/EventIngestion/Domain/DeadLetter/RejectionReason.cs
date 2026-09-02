using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// Why a delivery was rejected, in terms an operator can post-mortem.
/// </summary>
public sealed record RejectionReason : StringValueObject
{
    /// <summary>
    /// Matches the column width in the EF configuration. The two must agree: a
    /// bound narrower than its column refuses values the database would accept,
    /// and a wider one hands Postgres a write it will reject.
    /// </summary>
    public const int MaximumLength = 512;

    private RejectionReason(string value)
        : base(value)
    {
    }

    public static RejectionReason From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength);

        return new RejectionReason(value);
    }
}
