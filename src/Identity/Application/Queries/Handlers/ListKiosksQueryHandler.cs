using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries.Handlers;

public sealed class ListKiosksQueryHandler(IRegisteredClientQuerySource clients)
    : IQueryHandler<ListKiosksQuery, Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>>
{
    public async Task<Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>> HandleAsync(
        ListKiosksQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        IReadOnlyList<RegisteredClientSummaryDto> kiosks = await RegisteredClientProjection.ListAsync(
            clients, ClientKind.Kiosk, query.Fab, cancellationToken);

        return Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>.Success(kiosks);
    }
}
