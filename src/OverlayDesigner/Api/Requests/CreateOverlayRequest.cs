namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// POST /overlays request body. Primitive types at the trust boundary;
/// validation happens inside value-object constructors.
/// </summary>
public sealed record CreateOverlayRequest(string Name, LabelRequest Label);
