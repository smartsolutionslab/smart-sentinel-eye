namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// POST /walls request body (spec 258 US1). Primitive types at the trust
/// boundary; validation happens inside <c>WallName.From</c> and
/// <c>Wall.ValidateScenes</c>.
/// </summary>
public sealed record CreateWallRequest(string Name, IReadOnlyList<Guid> Scenes);
