namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// Configuration for the Keycloak Admin REST client. Bound from
/// the <c>Keycloak</c> section + the Aspire-injected
/// <c>ConnectionStrings:keycloak</c> base URL.
/// </summary>
public sealed class KeycloakAdminOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Realm name (e.g. <c>smart-sentinel-eye</c>).</summary>
    public string Realm { get; set; } = "smart-sentinel-eye";

    /// <summary>
    /// Keycloak base URL (e.g. <c>http://keycloak:8080</c>).
    /// Set from the Aspire-injected
    /// <c>ConnectionStrings:keycloak</c> at startup.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Client id of the Identity admin service-account client
    /// (seeded by the realm import in PR E).
    /// </summary>
    public string AdminClientId { get; set; } = "identity-admin";

    /// <summary>
    /// Client secret of the Identity admin service-account
    /// client. Set from a configuration secret.
    /// </summary>
    public string AdminClientSecret { get; set; } = string.Empty;
}
