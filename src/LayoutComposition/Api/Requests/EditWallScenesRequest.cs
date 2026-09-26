namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// PUT /walls/{wallIdentifier}/scenes request body (spec 258 US1-15).
/// Replaces the whole ordered scene set atomically.
/// </summary>
public sealed record EditWallScenesRequest(IReadOnlyList<Guid> Scenes);
