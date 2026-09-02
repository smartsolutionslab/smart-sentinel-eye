using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// The last failure a stream reported. Absent where it has never failed.
///
/// <para>
/// The bound is the property most likely to be hit in this feature: the values
/// are gateway and transport errors whose length nobody controls.
/// </para>
/// </summary>
public sealed record StreamError : StringValueObject
{
    /// <summary>
    /// Matches the column width in the EF configuration. The two must agree: a
    /// bound narrower than its column refuses values the database would accept,
    /// and a wider one hands Postgres a write it will reject.
    /// </summary>
    public const int MaximumLength = 1024;

    private StreamError(string value)
        : base(value)
    {
    }

    public static StreamError From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength);

        return new StreamError(value);
    }
}
