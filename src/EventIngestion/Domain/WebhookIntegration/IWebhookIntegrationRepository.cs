using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

public interface IWebhookIntegrationRepository
{
    Task<Option<WebhookIntegration>> GetByNameAsync(
        WebhookIntegrationName name, CancellationToken cancellationToken);

    /// <summary>
    /// Scoped to <paramref name="fab"/> in the predicate itself, mirroring
    /// <c>IRegisteredClientRepository.GetWithinFabAsync</c> (spec 180): an
    /// integration registered in a different fab is never materialised, so a
    /// rotation announcement naming the wrong fab cannot mutate it by a
    /// caller forgetting to compare afterwards.
    /// </summary>
    Task<Option<WebhookIntegration>> GetWithinFabAsync(
        FabIdentifier fab, WebhookIntegrationName name, CancellationToken cancellationToken);

    /// <summary>
    /// Whether any integration row naming this Keycloak client id is revoked,
    /// so a caller presenting that client's JWT elsewhere can be refused the
    /// same way <c>AuthenticateWebhookAsync</c> refuses it (#2814).
    /// Deliberately fail-closed rather than "pick one row and check only it":
    /// in production <see cref="WebhookIntegration.KeycloakClientId"/> is
    /// always derived 1:1 from the integration's own globally-unique
    /// <see cref="WebhookIntegrationName"/> (<c>ux_webhook_integrations_name</c>),
    /// so no two live rows ever share a client id and there is no ambiguity
    /// to resolve by recency. A revoked row that happens to share a client id
    /// with a live one — the shape this repo's own integration-test fixtures
    /// take, never a production shape — must still refuse, not lose to a
    /// newer sibling.
    /// </summary>
    Task<bool> IsRevokedByKeycloakClientIdAsync(
        KeycloakClientIdentifier keycloakClientId, CancellationToken cancellationToken);

    void Add(WebhookIntegration integration);

    Task SaveAsync(CancellationToken cancellationToken);
}
