namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// PATCH /layouts/{id}/revisions/{n} body. v1 only edits the
/// camera per spec 003 edge cases.
/// </summary>
public sealed record EditDraftRequest(Guid CameraIdentifier);
