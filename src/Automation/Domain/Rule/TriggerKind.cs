using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// What kind of event a rule triggers on. Distinct from
/// <see cref="TriggerSource"/> — see that type for why the pair is typed.
/// </summary>
public sealed record TriggerKind : StringValueObject
{
    /// <summary>
    /// Matches the column width in the EF configuration. The two must agree: a
    /// bound narrower than its column refuses values the database would accept,
    /// and a wider one hands Postgres a write it will reject.
    /// </summary>
    public const int MaximumLength = 128;

    private TriggerKind(string value)
        : base(value)
    {
    }

    public static TriggerKind From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength);

        return new TriggerKind(value);
    }
}
