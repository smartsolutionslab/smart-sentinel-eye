namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// PATCH /overlays/{id}/revisions/{n} body. The full label set rides along
/// because an overlay edit always replaces it wholesale (FR-006) —
/// partial updates aren't a use case the kiosk side can do anything
/// useful with.
/// </summary>
public sealed record EditDraftRequest(IReadOnlyList<LabelRequest> Labels);
