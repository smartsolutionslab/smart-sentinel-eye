using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

public interface IWebhookIntegrationRepository
{
    Task<Option<WebhookIntegration>> GetByNameAsync(
        WebhookIntegrationName name, CancellationToken cancellationToken);

    void Add(WebhookIntegration integration);

    Task SaveAsync(CancellationToken cancellationToken);
}
