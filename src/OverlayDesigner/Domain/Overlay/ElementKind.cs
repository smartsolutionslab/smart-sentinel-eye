using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// The closed set of overlay primitive kinds (spec 300, #2349, ADR-0165 §1):
/// a text label, a box or an ellipse. Follows
/// <see cref="OverlayRevisionState"/>'s shape exactly — named singletons and
/// a <c>From</c> that throws on anything else — rather than a type
/// hierarchy, because EF Core owned collections do not map inheritance and
/// the three kinds share nearly everything (geometry, colour, ordinal).
/// </summary>
public sealed record ElementKind(string Value) : IValueObject<string>
{
    public static ElementKind Text { get; } = new("Text");

    public static ElementKind Box { get; } = new("Box");

    public static ElementKind Ellipse { get; } = new("Ellipse");

    public static ElementKind From(string value) =>
        value switch
        {
            "Text" => Text,
            "Box" => Box,
            "Ellipse" => Ellipse,
            _ => throw new ArgumentException(
                $"Unknown ElementKind '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
