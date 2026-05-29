namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

/// <summary>
/// How <c>EventsEndpoints.IngestWebhook</c> validates the bearer
/// the caller presents for this integration (spec 008 FR-016
/// hard-cut migration).
/// </summary>
public enum BearerValidationMode
{
    /// <summary>
    /// Legacy: the integration's <see cref="WebhookIntegration.TokenHash"/>
    /// is compared SHA-256 against the presented plaintext. Default for
    /// integrations registered before spec 008.
    /// </summary>
    StaticHash = 0,

    /// <summary>
    /// JWT: the bearer is a Keycloak access token for the
    /// rotation-issued service-account client; the endpoint checks
    /// signature, expiry, scope <c>sse.events.write</c>, and fab
    /// membership.
    /// </summary>
    Jwt = 1,
}
