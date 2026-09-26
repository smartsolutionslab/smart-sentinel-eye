namespace SmartSentinelEye.LayoutComposition.Application.DTOs;

/// <summary>
/// Read-side projection of a <c>Wall</c> (spec 258 US1). Read directly
/// through <c>IWallRepository</c> — deliberately simpler than
/// <see cref="LayoutDto"/>'s separate query-source, since a wall's read
/// shape is exactly its aggregate state.
///
/// <para>
/// <see cref="Version"/> is the wall's optimistic-concurrency version
/// (ADR-0113); it is also returned as an <c>ETag</c> by <c>GET /walls/{id}</c>.
/// </para>
/// </summary>
public sealed record WallDto(
    Guid Wall,
    int Version,
    string Fab,
    string Name,
    IReadOnlyList<Guid> Scenes,
    Guid Showing,
    long SceneVersion,
    DateTimeOffset ShowingSince);
