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

    /// <summary>
    /// The group a connection joins for each fab it holds. Prefixed so it
    /// cannot collide with any other group name this hub grows later.
    /// </summary>
    public static string FabGroup(string fab) => $"fab:{fab}";

    /// <summary>
    /// Joins one group per fab the connection is assigned to, read from the
    /// same <c>groups</c> claim the server-side guard uses — so the hub and
    /// <c>FabClaims</c> cannot disagree about what a caller holds.
    ///
    /// <para>
    /// A connection holding no fab joins nothing and therefore receives no
    /// resolved-text push. That is the intended failure direction: a
    /// misconfigured account showing nothing is recoverable, where one shown
    /// another plant's production figures is not (spec 014 FR-015).
    /// </para>
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        foreach (string fab in FabClaims.AssignedFabs(Context.User))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, FabGroup(fab));
        }

        await base.OnConnectedAsync();
    }
}
