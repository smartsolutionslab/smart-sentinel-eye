using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;

public sealed record WebhookIntegrationRevokedDomainEvent(
    WebhookIntegrationName Name,
    FabIdentifier Fab,
    DateTimeOffset RevokedAt) : IDomainEvent;
