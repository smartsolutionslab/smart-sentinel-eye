using SmartSentinelEye.Identity.Application.Queries;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// A seedable, async-capable <see cref="IRegisteredClientQuerySource"/> that counts how often it
/// was read, so "the list never reached the store" is a fact about <see cref="Reads"/>.
/// </summary>
internal sealed class CountingRegisteredClientQuerySource : IRegisteredClientQuerySource
{
    private readonly List<RegisteredClientAggregate> clients = [];

    public int Reads { get; private set; }

    public IQueryable<RegisteredClientAggregate> RegisteredClients
    {
        get
        {
            Reads++;
            return new TestAsyncEnumerable<RegisteredClientAggregate>(clients);
        }
    }

    public void Add(RegisteredClientAggregate client) => clients.Add(client);
}
