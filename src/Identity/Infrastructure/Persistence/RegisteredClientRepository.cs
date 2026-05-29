using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

public sealed class RegisteredClientRepository(
    IdentityDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IRegisteredClientRepository
{
    public async Task<Option<RegisteredClientAggregate>> GetByIdentifierAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken)
    {
        RegisteredClientAggregate? found = await dbContext.RegisteredClients
            .FirstOrDefaultAsync(c => c.Id == identifier, cancellationToken)
            .ConfigureAwait(false);
        return found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found);
    }

    public async Task<Option<RegisteredClientAggregate>> GetByClientIdAsync(
        ClientId clientId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        // Disabled rows release the clientId for re-registration
        // (mirrors spec 005's archived-name pattern).
        RegisteredClientAggregate? found = await dbContext.RegisteredClients
            .Where(c => c.ClientId == clientId)
            .Where(c => c.DisabledAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found);
    }

    public void Add(RegisteredClientAggregate client)
    {
        ArgumentNullException.ThrowIfNull(client);
        dbContext.RegisteredClients.Add(client);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        RegisteredClientAggregate[] tracked = dbContext.ChangeTracker
            .Entries<RegisteredClientAggregate>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (RegisteredClientAggregate client in tracked)
        {
            var events = client.PendingEvents.ToArray();
            client.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
