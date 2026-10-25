using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Application.DTOs;

internal static class WebhookIntegrationMappings
{
    public static WebhookIntegrationDto ToDto(this WebhookIntegration integration) =>
        new(
            integration.Id.Value,
            integration.Version,
            integration.Name.Value,
            integration.Fab.Value,
            integration.DefaultKind.Value,
            integration.RegisteredAt,
            integration.RevokedAt?.Value);
}
