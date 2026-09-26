namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "a wall's scene changed" SignalR frames (spec 258 US1,
/// plan.md §4.3). Primitive types only — value-object types stay in Domain
/// and never hit the wire. <c>SceneVersion</c> lets the kiosk discard a
/// frame at or below the one it is already rendering (US1-16).
/// </summary>
public sealed record WallSceneChangedHubMessage(
    Guid Wall,
    Guid Showing,
    long SceneVersion);
