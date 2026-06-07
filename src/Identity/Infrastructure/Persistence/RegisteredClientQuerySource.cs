using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Identity.Application.Queries;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

/// <summary>
/// EF-Core-backed read-side seam (<see cref="IRegisteredClientQuerySource"/>).
/// Reads the <c>registered_clients</c> table with <c>AsNoTracking</c> to keep
/// list queries cheap. Mirrors <c>CameraQuerySource</c> / <c>StreamQuerySource</c>.
/// </summary>
public sealed class RegisteredClientQuerySource(IdentityDbContext dbContext) : IRegisteredClientQuerySource
{
    public IQueryable<RegisteredClientAggregate> RegisteredClients => dbContext.RegisteredClients.AsNoTracking();
}
