namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// The <c>revocation-list-reader</c> service account (ADR-0160 §4) every
/// non-Identity API presents to <c>GET /registered-clients/revoked</c>.
/// <c>KeycloakUrl</c> and <c>Realm</c> are filled in by
/// <see cref="AuthenticationDefaults.AddBearerAuthentication"/> from the same
/// configuration it already reads for the bearer pipeline's own authority, so
/// nothing here needs a second source of truth for where Keycloak is.
/// <c>ClientId</c>/<c>ClientSecret</c> come from configuration
/// (<c>RevocationList:ClientId</c> / <c>RevocationList:ClientSecret</c>), left
/// unset by Identity itself — it replaces <see cref="IRevokedClientSource"/>
/// with its own in-process reader and never mints this token (plan.md §3).
/// </summary>
public sealed class RevocationListOptions
{
    public const string SectionName = "RevocationList";

    public string KeycloakUrl { get; set; } = string.Empty;

    public string Realm { get; set; } = "smart-sentinel-eye";

    public string ClientId { get; set; } = "revocation-list-reader";

    public string ClientSecret { get; set; } = string.Empty;
}
