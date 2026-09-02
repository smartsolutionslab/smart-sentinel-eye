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

    /// <summary>
    /// Builds an error from text this system did not author — a gateway or
    /// transport message — clipping it to <see cref="MaximumLength"/> rather
    /// than refusing it.
    ///
    /// <para>
    /// The health watcher runs in a background loop and reports whatever the SFU
    /// hands it. Before this type existed, an over-long message was accepted by
    /// the aggregate and rejected by Postgres, so the health report was lost to a
    /// <c>DbUpdateException</c>. Refusing it here instead would throw
    /// <c>ArgumentException</c> into that loop and lose the report the same way,
    /// for the same input. Clipping keeps the report — a truncated reason is
    /// worth more to an operator than no state change at all.
    /// </para>
    ///
    /// <para>
    /// Separate from <see cref="From"/> on purpose: silently shortening a caller's
    /// value is the right answer only where the caller is an external system, and
    /// the name has to say so at the call site.
    /// </para>
    /// </summary>
    public static StreamError Truncating(string value)
    {
        Ensure.That(value, nameof(value)).IsNotNullOrWhiteSpace();

        return new StreamError(
            value.Length <= MaximumLength ? value : value[..MaximumLength]);
    }
}
