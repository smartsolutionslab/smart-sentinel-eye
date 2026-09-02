using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// The address a rejected delivery arrived on.
/// </summary>
public sealed record DeliveryTopic : StringValueObject
{
    /// <summary>
    /// Matches the column width in the EF configuration. The two must agree: a
    /// bound narrower than its column refuses values the database would accept,
    /// and a wider one hands Postgres a write it will reject.
    /// </summary>
    public const int MaximumLength = 256;

    private DeliveryTopic(string value)
        : base(value)
    {
    }

    public static DeliveryTopic From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength);

        return new DeliveryTopic(value);
    }
}
