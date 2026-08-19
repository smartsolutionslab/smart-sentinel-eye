using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

public sealed class RegisteredClientRepository(
    IdentityDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : IRegisteredClientRepository
{
    public async Task<Option<RegisteredClientAggregate>> GetByIdentifierAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken)
    {
        RegisteredClientAggregate? found = await dbContext.RegisteredClients
            .FirstOrDefaultAsync(client => client.Id == identifier, cancellationToken);
        return found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found);
    }

    public async Task<Option<RegisteredClientAggregate>> GetByClientIdAsync(
        ClientId clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull();
        // Disabled rows release the clientId for re-registration
        // (mirrors spec 005's archived-name pattern).
        RegisteredClientAggregate? found = await dbContext.RegisteredClients
            .Where(client => client.ClientId == clientId)
            .Where(client => client.DisabledAt == null)
            .FirstOrDefaultAsync(cancellationToken);
        return found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found);
    }

    public void Add(RegisteredClientAggregate client)
    {
        Ensure.That(client).IsNotNull();
        dbContext.RegisteredClients.Add(client);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        RegisteredClientAggregate[] tracked = dbContext.ChangeTracker
            .Entries<RegisteredClientAggregate>()
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
        foreach (RegisteredClientAggregate client in tracked)
        {
            IDomainEvent[] events = client.PendingEvents.ToArray();
            client.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
