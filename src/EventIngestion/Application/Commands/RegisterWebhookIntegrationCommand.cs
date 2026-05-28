using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public sealed record RegisterWebhookIntegrationCommand(
    WebhookIntegrationName Name,
    Kind DefaultKind)
    : ICommand<Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>>;

public sealed record RegisterWebhookIntegrationResult(
    WebhookIntegrationIdentifier Identifier,
    string PlainToken);
