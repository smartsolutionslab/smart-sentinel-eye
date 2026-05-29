namespace SmartSentinelEye.Shared.Contracts.Identity;

/// <summary>
/// Integration event raised by Identity (spec 008) when an
/// existing spec 006 webhook integration's bearer is rotated to
/// a Keycloak service-account JWT. EventIngestion subscribes and
/// flips the integration's bearer-validation path from the legacy
/// hash-compare to JWT-validate (spec 008 FR-016 hard-cut model).
/// </summary>
public sealed record WebhookIntegrationRotatedV1(
    string IntegrationName,
    string ClientId,
    DateTimeOffset RotatedAt) : IIntegrationEvent;
