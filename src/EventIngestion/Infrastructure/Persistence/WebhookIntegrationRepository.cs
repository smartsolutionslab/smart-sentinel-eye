using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class WebhookIntegrationRepository(
    EventIngestionDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IWebhookIntegrationRepository
{
    public async Task<Option<WebhookIntegration>> GetByNameAsync(
        WebhookIntegrationName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        WebhookIntegration? found = await dbContext.WebhookIntegrations
            .Where(w => w.Name == name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<WebhookIntegration>.None : Option<WebhookIntegration>.Some(found);
    }

    public void Add(WebhookIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        dbContext.WebhookIntegrations.Add(integration);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        WebhookIntegration[] tracked = dbContext.ChangeTracker
            .Entries<WebhookIntegration>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (WebhookIntegration integration in tracked)
        {
            var events = integration.PendingEvents.ToArray();
            integration.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
