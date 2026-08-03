using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries.Handlers;

public sealed class ListWebhookClientsQueryHandler(IRegisteredClientQuerySource clients)
    : IQueryHandler<ListWebhookClientsQuery, Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>>
{
    public async Task<Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>> HandleAsync(
        ListWebhookClientsQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        IReadOnlyList<RegisteredClientSummaryDto> webhooks = await RegisteredClientProjection.ListAsync(
            clients, ClientKind.WebhookIntegration, query.Fab, cancellationToken);

        return Success(webhooks);
    }
}
