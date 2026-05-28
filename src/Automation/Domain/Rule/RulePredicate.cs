using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Predicate text of a rule (spec 007 FR-007). v1 stores the raw
/// AEL source text; the parsed expression tree is computed and
/// cached in <c>Automation.Application.Evaluation.CompiledRule</c>
/// at publish-time. The Domain layer only enforces "non-empty and
/// bounded" — full grammar validation happens at the Application
/// edge (so a parse failure surfaces as a typed
/// <c>CreateRuleError.PredicateParseFailed</c> instead of crashing
/// the aggregate factory).
/// </summary>
public sealed record RulePredicate : StringValueObject
{
    public const int MinimumLength = 1;
    public const int MaximumLength = 4096;

    private RulePredicate(string value) : base(value) { }

    public static RulePredicate From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMinLength(MinimumLength)
            .HasMaxLength(MaximumLength)
            .AndReturn();
        return new RulePredicate(validated);
    }
}
