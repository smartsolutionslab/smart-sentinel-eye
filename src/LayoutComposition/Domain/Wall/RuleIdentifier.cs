using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// LayoutComposition's own copy of Automation's rule identifier (spec 258
/// US2, ADR-0157 §Consequences: a rule-driven switch must name "which
/// rule"). No cross-context reference per §III — mirrors how this context
/// already declares its own <c>OverlayIdentifier</c> for Automation's
/// overlay-highlight requests.
/// </summary>
public readonly record struct RuleIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<RuleIdentifier>
{
    public static RuleIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public static implicit operator Guid(RuleIdentifier id) => id.Value;

    public int CompareTo(RuleIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
