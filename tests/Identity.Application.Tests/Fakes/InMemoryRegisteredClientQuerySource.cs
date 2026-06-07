using SmartSentinelEye.Identity.Application.Queries;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRegisteredClientQuerySource"/> for query-handler
/// tests (ADR-0052). The list is exposed through
/// <see cref="TestAsyncEnumerable{T}"/> so EF Core's ToListAsync resolves
/// against an IAsyncQueryProvider — no DbContext, no Postgres. The real
/// implementation wraps <c>IdentityDbContext.RegisteredClients</c>.
/// </summary>
public sealed class InMemoryRegisteredClientQuerySource(List<RegisteredClientAggregate> clients)
    : IRegisteredClientQuerySource
{
    public IQueryable<RegisteredClientAggregate> RegisteredClients =>
        new TestAsyncEnumerable<RegisteredClientAggregate>(clients);
}
