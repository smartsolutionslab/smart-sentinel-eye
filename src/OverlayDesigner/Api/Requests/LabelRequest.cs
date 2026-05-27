namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// Wire shape for an overlay Label at the trust boundary. Primitive
/// types only; validation runs inside <c>Label.From(...)</c>. Reused
/// by Create (PR D) and Edit (PR F) request bodies.
/// </summary>
public sealed record LabelRequest(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx);
