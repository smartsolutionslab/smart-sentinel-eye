using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public sealed record RevokeWebhookIntegrationCommand(WebhookIntegrationName Name)
    : ICommand<Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>>;
