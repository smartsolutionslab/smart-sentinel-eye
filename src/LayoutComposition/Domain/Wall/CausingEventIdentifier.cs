using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// The plant-floor event that, via a rule, caused a scene switch (spec 258
/// US2, ADR-0157 §Consequences). Context-local, mirroring
/// <c>OverlayHighlightRequestedV1.CausingEventIdentifier</c>'s role — carried
/// for audit / replay correlation, never dereferenced here.
/// </summary>
public readonly record struct CausingEventIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<CausingEventIdentifier>
{
    public static CausingEventIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public static implicit operator Guid(CausingEventIdentifier id) => id.Value;

    public int CompareTo(CausingEventIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(CausingEventIdentifier left, CausingEventIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(CausingEventIdentifier left, CausingEventIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(CausingEventIdentifier left, CausingEventIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(CausingEventIdentifier left, CausingEventIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
