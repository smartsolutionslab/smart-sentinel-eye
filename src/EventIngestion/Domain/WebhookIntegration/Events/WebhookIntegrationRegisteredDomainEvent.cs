using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;

public sealed record WebhookIntegrationRegisteredDomainEvent(
    WebhookIntegrationName Name,
    Kind DefaultKind,
    DateTimeOffset RegisteredAt) : IDomainEvent;
