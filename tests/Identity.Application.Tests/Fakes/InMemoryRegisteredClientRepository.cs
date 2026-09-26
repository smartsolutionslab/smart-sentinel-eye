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
    private readonly List<RegisteredClientAggregate> _clients = [];
    private readonly HashSet<Guid> _persisted = [];

    public IReadOnlyList<RegisteredClientAggregate> Clients => _clients;

    /// <summary>
    /// One-shot: when set, the next <see cref="SaveAsync"/> throws this
    /// instead of committing, then resets to <c>null</c>. Mirrors
    /// <see cref="FakeKeycloakAdminClient.FailNextCall"/>'s shape, for the
    /// same reason — a test injects a Layer-2 loser's exception without a
    /// second real caller racing it.
    /// </summary>
    public Exception? FailNextSaveWith { get; set; }

    /// <summary>
    /// Places a client that already exists in the database, at
    /// <paramref name="version"/>. Distinct from <see cref="Add"/>, which is
    /// the production path for a row being created now: the interceptor does
    /// not bump <c>Added</c> roots, so only a seeded row's next save moves.
    /// </summary>
    public void Seed(RegisteredClientAggregate client, int version = 0)
    {
        Ensure.That(client).IsNotNull();

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
        Ensure.That(clientId).IsNotNull();
        // Disabled rows release the name for re-registration (mirrors
        // spec 005's archived-name pattern).
        RegisteredClientAggregate? found = _clients.SingleOrDefault(c =>
            c.ClientId == clientId && c.DisabledAt is null);
        return Task.FromResult(found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found));
    }

    public Task<Option<RegisteredClientAggregate>> GetWithinFabAsync(
        FabIdentifier fab, ClientId clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull();
        Ensure.That(fab).IsNotNull();

        // Fab is part of the match, not a filter applied afterwards — mirrors
        // the production predicate (spec 180 US1). Disabled rows are excluded,
        // matching GetByClientIdAsync.
        RegisteredClientAggregate? found = _clients.SingleOrDefault(c =>
            c.ClientId == clientId && c.Fab == fab && c.DisabledAt is null);
        return Task.FromResult(found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found));
    }

    /// <summary>
    /// Spec 264 (#2206). Same fab-scoped match as <see cref="GetWithinFabAsync"/>,
    /// minus the <c>DisabledAt is null</c> filter — see the interface doc
    /// comment for why a Disable subscriber needs a Disabled row returned
    /// rather than excluded.
    /// </summary>
    public Task<Option<RegisteredClientAggregate>> GetWithinFabIncludingDisabledAsync(
        FabIdentifier fab, ClientId clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull();
        Ensure.That(fab).IsNotNull();

        RegisteredClientAggregate? found = _clients.SingleOrDefault(c =>
            c.ClientId == clientId && c.Fab == fab);
        return Task.FromResult(found is null
            ? Option<RegisteredClientAggregate>.None
            : Option<RegisteredClientAggregate>.Some(found));
    }

    public void Add(RegisteredClientAggregate client)
    {
        Ensure.That(client).IsNotNull();
        _clients.Add(client);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        if (FailNextSaveWith is not null)
        {
            Exception toThrow = FailNextSaveWith;
            FailNextSaveWith = null;
            throw toThrow;
        }

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
