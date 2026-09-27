using System.Net.Http.Headers;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// Presents the <c>revocation-list-reader</c> service account on each
/// <c>GET /registered-clients/revoked</c> request (spec 270, ADR-0160 §4).
/// Mirrors <c>CameraCatalogAuthorizationHandler</c>.
/// </summary>
public sealed class RevocationListAuthorizationHandler(RevocationListTokenProvider tokens) : AuthorizingHandler
{
    protected override async Task<AuthenticationHeaderValue?> AuthorizationAsync(
        CancellationToken cancellationToken) =>
        new("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));
}
