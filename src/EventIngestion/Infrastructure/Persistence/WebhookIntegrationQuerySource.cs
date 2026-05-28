using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class WebhookIntegrationQuerySource(EventIngestionDbContext dbContext)
    : IWebhookIntegrationQuerySource
{
    public IQueryable<WebhookIntegration> WebhookIntegrations =>
        dbContext.WebhookIntegrations.AsNoTracking();
}
