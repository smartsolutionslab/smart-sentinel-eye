namespace SmartSentinelEye.Shared.Contracts.EventIngestion;

/// <summary>
/// Integration event raised by EventIngestion (spec 264, #2206) when an
/// operator revokes a registered webhook integration. Identity subscribes
/// and disables the integration's Keycloak client the same way it disables
/// a kiosk or a device, so the revoked integration can mint no further
/// token on any path that trusts one.
/// </summary>
public sealed record WebhookIntegrationRevokedV1(
    string IntegrationName,
    DateTimeOffset RevokedAt,
    EventMetadata Metadata) : IIntegrationEvent;
