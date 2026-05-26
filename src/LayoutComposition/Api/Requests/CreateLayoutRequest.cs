namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// POST /layouts request body. Primitive types at the trust boundary;
/// validation happens inside value-object constructors.
/// </summary>
public sealed record CreateLayoutRequest(string Name, Guid CameraIdentifier);
