using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Queries.Handlers;

/// <summary>
/// Shared read-side projection for the device + kiosk list queries
/// (issues #826/#827). Filters by <see cref="ClientKind"/> and an
/// optional <see cref="FabIdentifier"/>, orders newest-first, and maps to
/// <see cref="RegisteredClientSummaryDto"/> — which never carries the
/// (unpersisted) client secret.
/// </summary>
internal static class RegisteredClientProjection
{
    public static async Task<IReadOnlyList<RegisteredClientSummaryDto>> ListAsync(
        IRegisteredClientQuerySource source,
        ClientKind kind,
        Option<FabIdentifier> fab,
        CancellationToken cancellationToken)
    {
        IQueryable<RegisteredClientAggregate> query = source.RegisteredClients
            .Where(client => client.Kind == kind);

        if (fab.HasValue)
        {
            FabIdentifier wanted = fab.Value;
            query = query.Where(client => client.Fab == wanted);
        }

        return await query
            .OrderByDescending(client => client.RegisteredAt)
            .Select(client => new RegisteredClientSummaryDto(
                client.Id.Value,
                client.Version,
                client.ClientId.Value,
                client.Kind.Value,
                client.Fab.Value,
                client.RegisteredAt,
                client.RegisteredBy.Value,
                client.DisabledAt,
                client.LastRotatedAt))
            .ToListAsync(cancellationToken);
    }
}
