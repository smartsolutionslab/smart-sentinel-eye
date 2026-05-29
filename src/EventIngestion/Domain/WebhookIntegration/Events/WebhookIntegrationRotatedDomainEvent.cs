using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;

/// <summary>
/// Raised when an integration flips from legacy hash-compare to
/// JWT validation against a Keycloak service-account client (spec
/// 008 FR-016). The <see cref="KeycloakClientId"/> is the
/// <c>identity-admin</c>-issued client whose tokens the integration
/// must henceforth present.
/// </summary>
public sealed record WebhookIntegrationRotatedDomainEvent(
    WebhookIntegrationName Name,
    string KeycloakClientId,
    DateTimeOffset RotatedAt) : IDomainEvent;
