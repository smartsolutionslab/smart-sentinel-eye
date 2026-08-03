using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Tests;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// Stands in for <c>RegisteredClientRepository</c> plus
/// <c>AggregateVersionInterceptor</c>. It reproduces the version bump because
/// omitting it left every Application-layer version at 0 — which is also
/// <c>default(int)</c>, so the rotation gate could not be tested at any value
/// that distinguishes a real comparison from no comparison (#1248).
/// </summary>
public sealed class InMemoryRegisteredClientRepository : IRegisteredClientRepository
{
    private readonly List<RegisteredClientAggregate> _clients = new();
    private readonly HashSet<Guid> _persisted = new();

    public IReadOnlyList<RegisteredClientAggregate> Clients => _clients;

    /// <summary>
    /// Places a client that already exists in the database, at
    /// <paramref name="version"/>. Distinct from <see cref="Add"/>, which is
    /// the production path for a row being created now: the interceptor does
    /// not bump <c>Added</c> roots, so only a seeded row's next save moves.
    /// </summary>
    public void Seed(RegisteredClientAggregate client, int version = 0)
    {
        ArgumentNullException.ThrowIfNull(client);

        AggregateVersions.SetTo(client, version);
        _clients.Add(client);
        _persisted.Add(client.Id.Value);
        client.ClearPendingEvents();
    }

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
            // Mirrors AggregateVersionInterceptor.RequiresBump: an Added root
            // starts at 0 and is not bumped; an already-persisted root with
            // changes is. Pending events stand in for the change tracker's
            // Modified state — every mutator on this aggregate raises one.
            bool wasAlreadyPersisted = !_persisted.Add(c.Id.Value);
            if (wasAlreadyPersisted && c.PendingEvents.Count > 0)
            {
                AggregateVersions.Bump(c);
            }

            c.ClearPendingEvents();
        }

        return Task.CompletedTask;
    }
}
