using System.Net.Http.Headers;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// Presents the <c>identity-admin</c> service account on every Keycloak Admin
/// API request. Registered on <c>HttpKeycloakAdminClient</c>'s client and
/// deliberately not on the token provider's own — the provider is what mints the
/// token this attaches, so authorising it would make the mint depend on itself.
/// </summary>
public sealed class KeycloakAdminAuthorizationHandler(KeycloakAdminTokenProvider tokens) : AuthorizingHandler
{
    protected override async Task<AuthenticationHeaderValue?> AuthorizationAsync(
        CancellationToken cancellationToken) =>
        new("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));
}
