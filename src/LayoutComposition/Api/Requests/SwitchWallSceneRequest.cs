namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// POST /walls/{wallIdentifier}/switch request body (spec 258 US1, plan.md
/// §4.4). <c>Target</c> is <c>"next"</c> or <c>"layout"</c>; <c>Layout</c>
/// is required if and only if <c>Target</c> is <c>"layout"</c>. Parsed at
/// the edge into a <c>SceneTarget</c>.
/// </summary>
public sealed record SwitchWallSceneRequest(string Target, Guid? Layout);
