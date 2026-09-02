using System.Net.Http.Headers;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

/// <summary>
/// Presents the <c>stream-distribution-attribution</c> service account on each
/// camera-catalogue request the startup attribution pass makes (ADR-0116).
///
/// <para>
/// The pass reads a fab-wide listing page by page, so the client is held across
/// several requests — which is exactly the shape where a header written once
/// onto the client outlives what it was written for.
/// </para>
/// </summary>
public sealed class CameraCatalogAuthorizationHandler(CameraCatalogTokenProvider tokens) : AuthorizingHandler
{
    protected override async Task<AuthenticationHeaderValue?> AuthorizationAsync(
        CancellationToken cancellationToken) =>
        new("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));
}
