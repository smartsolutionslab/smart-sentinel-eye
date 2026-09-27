using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.Revocation;

/// <summary>
/// Identity's own <see cref="IRevokedClientSource"/> (spec 270, ADR-0160 §3,
/// plan.md §4.2): runs <see cref="ListRevokedClientsQueryHandler"/> in
/// process instead of calling <c>GET /registered-clients/revoked</c> over
/// HTTP, so Identity never needs the <c>revocation-list-reader</c> credential
/// to check its own bearer pipeline.
///
/// <para>
/// Registered as a singleton — <see cref="ServiceDefaults.Revocation.RevokedClientRefresher"/>
/// holds it for the process lifetime — so the query handler, which depends on
/// the scoped <c>IdentityDbContext</c>, is resolved from a fresh
/// <see cref="IServiceScope"/> on every fetch rather than captured at
/// construction.
/// </para>
/// </summary>
public sealed class LocalRevokedClientSource(IServiceScopeFactory scopeFactory) : IRevokedClientSource
{
    public async Task<IReadOnlyList<RevokedClientEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ListRevokedClientsQueryHandler handler = scope.ServiceProvider.GetRequiredService<ListRevokedClientsQueryHandler>();

        Result<IReadOnlyList<RevokedClientEntry>, ListClientsError> result =
            await handler.HandleAsync(new ListRevokedClientsQuery(), cancellationToken);

        return result.Match(
            onSuccess: entries => entries,
            onFailure: error => throw new InvalidOperationException(
                $"Failed to read the revoked-client list: {error.Code} {error.Message}"));
    }
}
