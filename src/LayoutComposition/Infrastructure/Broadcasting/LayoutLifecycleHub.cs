using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// SignalR hub the kiosk (and management-web) connects to for layout
/// lifecycle pushes (spec 003 FR-009 / FR-011). Empty server-side
/// surface — clients listen only. Broadcast happens via
/// <see cref="IHubContext{THub, T}"/> in
/// <see cref="SignalRLayoutLifecycleBroadcaster"/>.
///
/// Authorisation is hub-level: a connection is rejected if the bearer
/// token does not carry <c>sse.layouts.read</c> (or the grandfathered
/// <c>sse.management</c> bundle). The bearer arrives via the WebSocket
/// query string per Microsoft's documented pattern; the
/// <c>JwtBearerOptions.OnMessageReceived</c> hook wired in
/// <c>Program.cs</c> translates query-string to the
/// <c>Authorization</c> header.
/// </summary>
[Authorize(Policy = Scope.Sse.Layouts.Read)]
public sealed class LayoutLifecycleHub : Hub<ILayoutLifecycleClient>
{
    public const string Path = "/hubs/layouts";
}
