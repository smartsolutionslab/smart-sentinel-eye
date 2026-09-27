using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Queries.Handlers;

/// <summary>
/// Backs <see cref="ListRevokedClientsQuery"/> (spec 270, ADR-0160 §2,
/// plan.md §4.2). Reads every disabled row, groups by client id and keeps the
/// later <c>DisabledAt</c> — FR-001's "latest wins", for the same reason
/// <see cref="ServiceDefaults.Revocation.RevokedClientSnapshot.From"/> does it
/// again on the receiving side: a client id can be disabled, re-registered
/// and disabled again.
/// </summary>
public sealed class ListRevokedClientsQueryHandler(IRegisteredClientQuerySource clients)
    : IQueryHandler<ListRevokedClientsQuery, Result<IReadOnlyList<RevokedClientEntry>, ListClientsError>>
{
    public async Task<Result<IReadOnlyList<RevokedClientEntry>, ListClientsError>> HandleAsync(
        ListRevokedClientsQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        IReadOnlyList<RevokedClientEntry> entries = await BuildQuery(clients.RegisteredClients)
            .ToListAsync(cancellationToken);

        return Success(entries);
    }

    /// <summary>
    /// Groups by the <c>ClientId</c> value object itself and projects
    /// <c>.Value</c> only in the final <c>Select</c>, after grouping.
    /// Grouping by <c>client.ClientId.Value</c> directly does not translate —
    /// EF Core cannot rewrite member access on a value-converted property
    /// inside a <c>GroupBy</c> key selector, and it silently falls back to
    /// client evaluation of the whole table, then throws
    /// <c>InvalidOperationException</c> once <c>AsNoTracking</c>'s server-side
    /// evaluation is required. <c>ListRevokedClientsQueryTranslationTests</c>
    /// pins this offline. Extracted to a static method, mirroring
    /// <c>ListEventsQueryHandler.BuildPagedQuery</c>, so that translation can
    /// be verified without a live database.
    /// </summary>
    public static IQueryable<RevokedClientEntry> BuildQuery(IQueryable<RegisteredClientAggregate> clients) =>
        clients
            .Where(client => client.DisabledAt != null)
            .GroupBy(client => client.ClientId)
            .Select(group => new RevokedClientEntry(
                group.Key.Value,
                group.Max(client => (DateTimeOffset)client.DisabledAt!)));
}
