namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>The label body for an overlay create (mirrors OverlayDesigner's LabelRequest).</summary>
public sealed record OverlayLabel(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx);
