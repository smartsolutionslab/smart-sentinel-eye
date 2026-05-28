using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;

public sealed record WebhookIntegrationRevokedDomainEvent(
    WebhookIntegrationName Name,
    DateTimeOffset RevokedAt) : IDomainEvent;
