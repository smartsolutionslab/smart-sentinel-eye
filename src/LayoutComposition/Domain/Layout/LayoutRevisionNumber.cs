using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// 1-indexed monotonic revision number within a Layout chain. Each
/// successful "branch new draft" call mints the next integer; the chain
/// invariants (uniqueness, monotonicity) are enforced by the
/// <see cref="Layout"/> aggregate, not this value object.
/// </summary>
public readonly record struct LayoutRevisionNumber(int Value) : IValueObject<int>
{
    public static readonly LayoutRevisionNumber One = new(1);

    public static LayoutRevisionNumber From(int value) =>
        value < 1
            ? throw new ArgumentException(
                $"LayoutRevisionNumber must be >= 1; got {value}.", nameof(value))
            : new LayoutRevisionNumber(value);

    public LayoutRevisionNumber Next() => new(Value + 1);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
