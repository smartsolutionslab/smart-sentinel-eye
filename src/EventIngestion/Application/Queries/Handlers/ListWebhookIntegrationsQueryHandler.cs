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
        Ensure.That(query).IsNotNull();

        var (fabs, includeRevoked) = query;

        IQueryable<WebhookIntegration> source = integrations.WebhookIntegrations
            .Where(integration => fabs.Contains(integration.Fab));
        if (!includeRevoked)
        {
            source = source.Where(integration => integration.RevokedAt == null);
        }

        List<WebhookIntegration> rows = await source
            .ToListAsync(cancellationToken);

        IReadOnlyList<WebhookIntegrationDto> dtos = rows
            .Select(integration => new WebhookIntegrationDto(
                integration.Id.Value, integration.Version, integration.Name.Value, integration.Fab.Value, integration.DefaultKind.Value, integration.RegisteredAt, integration.RevokedAt))
            .OrderBy(dto => dto.Name, StringComparer.Ordinal)
            .ToArray();

        return Success(dtos);
    }
}
