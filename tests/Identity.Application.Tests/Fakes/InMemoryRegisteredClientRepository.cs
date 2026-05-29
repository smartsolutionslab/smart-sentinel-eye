using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

public sealed class InMemoryRegisteredClientRepository : IRegisteredClientRepository
{
    private readonly List<RegisteredClientAggregate> _clients = new();

    public IReadOnlyList<RegisteredClientAggregate> Clients => _clients;

    public Task<Option<RegisteredClientAggregate>> GetByIdentifierAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken)
    {
        RegisteredClientAggregate? found = _clients.SingleOrDefault(c => c.Id == identifier);
        return Task.FromResult(found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found));
    }

    public Task<Option<RegisteredClientAggregate>> GetByClientIdAsync(
        ClientId clientId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        // Disabled rows release the name for re-registration (mirrors
        // spec 005's archived-name pattern).
        RegisteredClientAggregate? found = _clients.SingleOrDefault(c =>
            c.ClientId == clientId && c.DisabledAt is null);
        return Task.FromResult(found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found));
    }

    public void Add(RegisteredClientAggregate client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _clients.Add(client);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (RegisteredClientAggregate c in _clients)
        {
            c.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
