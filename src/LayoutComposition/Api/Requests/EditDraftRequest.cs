namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// PATCH /layouts/{id}/revisions/{n} body. Spec 003 only edited the
/// camera; spec 004 adds the optional overlay binding via a nested
/// nullable object — present-with-identifier sets, present-with-null
/// clears, absent leaves the binding unchanged.
/// </summary>
public sealed record EditDraftRequest(Guid CameraIdentifier, OverlayBindingUpdate? Overlay = null);

/// <summary>
/// JSON-shape companion for the tri-state ``OverlayChange``:
/// <c>{ "identifier": "..." }</c> sets, <c>{ "identifier": null }</c>
/// clears, the wrapper being absent leaves the existing binding
/// alone.
/// </summary>
public sealed record OverlayBindingUpdate(Guid? Identifier);
