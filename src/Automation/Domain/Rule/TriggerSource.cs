using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Where a rule's trigger comes from — the transport or subsystem that
/// delivered the event. Distinct from <see cref="TriggerKind"/>: both were
/// <c>string</c>, sat adjacent in <c>Rule.Create</c>, and transposing them
/// compiled cleanly.
/// </summary>
public sealed record TriggerSource : StringValueObject
{
    /// <summary>
    /// Matches the column width in the EF configuration. The two must agree: a
    /// bound narrower than its column refuses values the database would accept,
    /// and a wider one hands Postgres a write it will reject.
    /// </summary>
    public const int MaximumLength = 16;

    private TriggerSource(string value)
        : base(value)
    {
    }

    public static TriggerSource From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength);

        return new TriggerSource(value);
    }
}
