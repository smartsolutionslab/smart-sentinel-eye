using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// When the currently-showing scene last changed (spec 258 PD-2). Set on
/// <see cref="Wall.Create"/> and restated by every applied
/// <see cref="Wall.SwitchTo"/> — never by a no-op.
/// </summary>
public sealed record ShowingSince(DateTimeOffset Value) : IValueObject<DateTimeOffset>
{
    public static ShowingSince From(DateTimeOffset value) => new(value);
}
