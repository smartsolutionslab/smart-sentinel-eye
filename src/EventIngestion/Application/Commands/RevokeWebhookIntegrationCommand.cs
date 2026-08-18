using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (#1545). Revoking is the sharper
/// half of the registry: reading another plant's integrations discloses their
/// names, but revoking one silently stops that plant's machine ingest.
/// </summary>
public sealed record RevokeWebhookIntegrationCommand(
    IReadOnlyList<FabIdentifier> Fabs, WebhookIntegrationName Name, int ExpectedVersion)
    : ICommand<Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>>;
