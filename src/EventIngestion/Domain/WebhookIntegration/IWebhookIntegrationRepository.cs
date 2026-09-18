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

    void Add(WebhookIntegration integration);

    Task SaveAsync(CancellationToken cancellationToken);
}
