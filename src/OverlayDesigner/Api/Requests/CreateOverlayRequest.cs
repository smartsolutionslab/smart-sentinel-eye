namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// POST /overlays request body. Primitive types at the trust boundary;
/// validation happens inside value-object constructors. Spec 150 (#2345):
/// <c>Elements</c> carries 1..8 elements, published atomically. Spec 300
/// (#2349, ADR-0165) widened the set from text-only labels to a closed
/// kind + colour model — renamed from <c>Labels</c>.
/// </summary>
public sealed record CreateOverlayRequest(string Name, IReadOnlyList<ElementRequest> Elements);
