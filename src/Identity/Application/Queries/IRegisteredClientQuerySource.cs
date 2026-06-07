using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Read-side seam exposing <c>IQueryable&lt;RegisteredClient&gt;</c> so the
/// list query handlers stay in the Application layer while EF Core's
/// translation runs in Infrastructure. Mirrors <c>ICameraQuerySource</c> /
/// <c>IStreamQuerySource</c>. The EF implementation wraps the
/// <c>registered_clients</c> table with <c>AsNoTracking</c>; the in-memory
/// fake in tests wraps a list.
/// </summary>
public interface IRegisteredClientQuerySource
{
    IQueryable<RegisteredClientAggregate> RegisteredClients { get; }
}
