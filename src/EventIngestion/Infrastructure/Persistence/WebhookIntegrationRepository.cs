using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class WebhookIntegrationRepository(
    EventIngestionDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : IWebhookIntegrationRepository
{
    public async Task<Option<WebhookIntegration>> GetByNameAsync(
        WebhookIntegrationName name, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();
        WebhookIntegration? found = await dbContext.WebhookIntegrations
            .Where(integration => integration.Name == name)
            .FirstOrDefaultAsync(cancellationToken);
        return found is null ? Option<WebhookIntegration>.None : Option<WebhookIntegration>.Some(found);
    }

    public void Add(WebhookIntegration integration)
    {
        Ensure.That(integration).IsNotNull();
        dbContext.WebhookIntegrations.Add(integration);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        WebhookIntegration[] tracked = dbContext.ChangeTracker
            .Entries<WebhookIntegration>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Dispatch first: the announcement is captured into the outbox, and the
        // commit below writes the rows and the messages in one transaction
        // (spec 021 FR-001). It used to be the other way round, and the gap
        // between the two was where an integration event went missing.
        //
        // Which means a handler now runs before the write is durable, and one
        // that throws fails the write rather than leaving the row behind. Every
        // handler on this path publishes and does nothing else - checked across
        // all twelve (research.md R2), not assumed.
        foreach (WebhookIntegration integration in tracked)
        {
            IDomainEvent[] events = integration.PendingEvents.ToArray();
            integration.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
