using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// <c>Fab</c> is the plant the registering operator resolved to (#1545), not a
/// field they supply: an integration belongs to the plant that created it, and
/// the ingest path refuses any delivery naming another.
/// </summary>
public sealed record RegisterWebhookIntegrationCommand(
    WebhookIntegrationName Name,
    FabIdentifier Fab,
    Kind DefaultKind)
    : ICommand<Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>>;

public sealed record RegisterWebhookIntegrationResult(
    WebhookIntegrationIdentifier Identifier,
    string PlainToken);
