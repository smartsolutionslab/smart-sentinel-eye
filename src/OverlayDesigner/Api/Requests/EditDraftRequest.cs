namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// PATCH /overlays/{id}/revisions/{n} body. The full element set rides
/// along because an overlay edit always replaces it wholesale (FR-006) —
/// partial updates aren't a use case the kiosk side can do anything
/// useful with. Renamed from <c>Labels</c> by spec 300 (#2349, ADR-0165).
/// </summary>
public sealed record EditDraftRequest(IReadOnlyList<ElementRequest> Elements);
