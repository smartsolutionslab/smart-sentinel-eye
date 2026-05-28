using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public interface IWebhookIntegrationQuerySource
{
    IQueryable<WebhookIntegration> WebhookIntegrations { get; }
}
