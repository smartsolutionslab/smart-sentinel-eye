using SmartSentinelEye.EventIngestion.Domain.Event;
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

    /// <summary>
    /// Composes the envelope-level topic grammar (<c>event/{fab}/{source}/{device}</c>),
    /// shared by both ingest handlers and the persistence loop's refusal path
    /// (spec 317, #2325, plan.md §6.3) rather than interpolated separately in
    /// three places.
    /// </summary>
    public static DeliveryTopic ForEnvelope(FabIdentifier fab, Source source, DeviceIdentifier device)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(source).IsNotNull();
        Ensure.That(device).IsNotNull();

        return From($"event/{fab.Value}/{source.Value}/{device.Value}");
    }
}
