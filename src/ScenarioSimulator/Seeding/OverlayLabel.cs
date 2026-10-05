namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// The element body for an overlay create (mirrors OverlayDesigner's
/// <c>ElementRequest</c>). The simulator only ever seeds text elements, so
/// <see cref="Kind"/> is always <c>"Text"</c> and <see cref="Color"/> the
/// default <c>#FFFFFFD9</c> (spec 300, #2349, ADR-0165 — renamed from the
/// text-only <c>Labels</c> body).
/// </summary>
public sealed record OverlayLabel(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx)
{
    public string Kind { get; } = "Text";

    public string Color { get; } = "#FFFFFFD9";
}
