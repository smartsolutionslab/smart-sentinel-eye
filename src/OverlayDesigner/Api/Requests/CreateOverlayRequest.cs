namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// POST /overlays request body. Primitive types at the trust boundary;
/// validation happens inside value-object constructors. Spec 150 (#2345):
/// <c>Labels</c> carries 1..8 labels, published atomically.
/// </summary>
public sealed record CreateOverlayRequest(string Name, IReadOnlyList<LabelRequest> Labels);
