namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// Wire shape for an overlay element at the trust boundary (spec 300,
/// #2349, ADR-0165 — renamed from <c>LabelRequest</c>). Primitive types
/// only; validation runs inside <c>ElementKind.From</c>,
/// <c>OverlayColor.From</c> and the matching
/// <see cref="SmartSentinelEye.OverlayDesigner.Domain.Overlay.OverlayElement"/>
/// factory. <c>Text</c>/<c>FontSizePx</c> are required for <c>Text</c> and
/// must be absent for <c>Box</c>/<c>Ellipse</c>. Reused by Create and Edit
/// request bodies.
/// </summary>
public sealed record ElementRequest(
    string Kind,
    string Color,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    string? Text,
    int? FontSizePx);
