using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// Monotonic counter of applied scene switches on a <see cref="Wall"/>
/// (spec 258 PD-2, FR-004). Starts at <see cref="Initial"/> and advances by
/// exactly one per switch that actually changes <c>Showing</c> — a no-op
/// switch leaves it untouched. Lets the kiosk discard an out-of-order
/// <c>WallSceneChanged</c> frame, the same device
/// <c>ResolvedOverlayTextChangedNotification.Version</c> already uses.
///
/// <para>
/// Deliberately a distinct value from <c>Wall.Version</c> (the EF
/// concurrency token): that one also moves on a scene-set edit that leaves
/// <c>Showing</c> unchanged, so it cannot serve as the kiosk's ordering key.
/// </para>
/// </summary>
public sealed record SceneVersion : IValueObject<long>
{
    public static SceneVersion Initial { get; } = new(0);

    public long Value { get; }

    private SceneVersion(long value) => Value = value;

    public static SceneVersion From(long value)
    {
        // Ensure.That has no `long` overload (only string/object/Guid/int/decimal,
        // ADR-0105), but `long` implicitly converts to `decimal`, so the
        // `decimal` overload's AtLeast(0) applies here — the same guard
        // AggregateVersion.From uses via EnsuredValue<int>.
        Ensure.That(value).AtLeast(0);

        return new SceneVersion(value);
    }

    public SceneVersion Next() => new(Value + 1);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
