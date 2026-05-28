using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries.Handlers;

public sealed class ListWebhookIntegrationsQueryHandler(IWebhookIntegrationQuerySource integrations)
    : IQueryHandler<ListWebhookIntegrationsQuery, Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError>>
{
    public async Task<Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError>> HandleAsync(
        ListWebhookIntegrationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<WebhookIntegration> source = integrations.WebhookIntegrations;
        if (!query.IncludeRevoked)
        {
            source = source.Where(w => w.RevokedAt == null);
        }

        List<WebhookIntegration> rows = await source
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<WebhookIntegrationDto> dtos = rows
            .Select(w => new WebhookIntegrationDto(
                w.Id.Value, w.Name.Value, w.DefaultKind.Value, w.RegisteredAt, w.RevokedAt))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        return Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError>.Success(dtos);
    }
}
