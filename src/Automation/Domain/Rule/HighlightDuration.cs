using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// How long a kiosk holds an overlay highlight before auto-reverting, in
/// milliseconds.
///
/// <para>
/// The range is the type's, not the action's. It was
/// <c>HighlightOverlay.From</c>'s guard, which meant an action assembled any
/// other way carried an unchecked number, and the bound had to be re-stated
/// wherever a duration was handled. Milliseconds are the unit the wire
/// contract and the browser both use, so the type keeps them rather than
/// converting at every edge.
/// </para>
/// </summary>
public readonly record struct HighlightDuration(int Value) : IValueObject<int>
{
    public const int MinimumMs = 500;
    public const int MaximumMs = 60_000;

    public static HighlightDuration From(int milliseconds)
    {
        Ensure.That(milliseconds).InRange(MinimumMs, MaximumMs);
        return new(milliseconds);
    }

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
