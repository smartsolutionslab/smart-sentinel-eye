using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries.Handlers;

public sealed class ListDevicesQueryHandler(IRegisteredClientQuerySource clients)
    : IQueryHandler<ListDevicesQuery, Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>>
{
    public async Task<Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>> HandleAsync(
        ListDevicesQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        IReadOnlyList<RegisteredClientSummaryDto> devices = await RegisteredClientProjection.ListAsync(
            clients, ClientKind.Device, query.Fab, cancellationToken);

        return Success(devices);
    }
}
