using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Truthy / falsy render strings carried on a <c>Boolean</c> variable
/// (spec 005 FR-006). Defaults to <c>("Yes", "No")</c> when the admin
/// doesn't specify. Both strings must be non-empty and ≤ 64 chars so
/// they fit comfortably into rendered overlay labels.
/// </summary>
public sealed record BooleanLabels(string TruthyLabel, string FalsyLabel) : IValueObject
{
    public const int MaximumLength = 64;

    public static BooleanLabels Default { get; } = new("Yes", "No");

    public static BooleanLabels From(string truthyLabel, string falsyLabel)
    {
        string truthy = Ensure.That(truthyLabel, nameof(truthyLabel))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .AndReturn();
        string falsy = Ensure.That(falsyLabel, nameof(falsyLabel))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .AndReturn();
        return new BooleanLabels(truthy, falsy);
    }
}
