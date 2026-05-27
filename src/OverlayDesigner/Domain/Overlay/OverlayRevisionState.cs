using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Lifecycle state of a single Revision within an Overlay chain
/// (spec 004 FR-003). Same state graph as LayoutRevisionState — Draft
/// ↔ Published + Archived terminal per revision. Logical chain stays
/// addressable even after every revision is Archived.
/// </summary>
public sealed record OverlayRevisionState(string Value) : IValueObject<string>
{
    public static OverlayRevisionState Draft { get; } = new("Draft");

    public static OverlayRevisionState Published { get; } = new("Published");

    public static OverlayRevisionState Archived { get; } = new("Archived");

    public static OverlayRevisionState From(string value) =>
        value switch
        {
            "Draft" => Draft,
            "Published" => Published,
            "Archived" => Archived,
            _ => throw new ArgumentException(
                $"Unknown OverlayRevisionState '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
