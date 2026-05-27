namespace SmartSentinelEye.OverlayDesigner.Api.Requests;

/// <summary>
/// PATCH /overlays/{id}/revisions/{n} body. The full Label rides along
/// because an overlay edit always replaces the Label wholesale —
/// partial updates aren't a use case the kiosk side can do anything
/// useful with.
/// </summary>
public sealed record EditDraftRequest(LabelRequest Label);
