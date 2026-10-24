using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;

public sealed record WebhookIntegrationRegisteredDomainEvent(
    WebhookIntegrationIdentifier Identifier,
    WebhookIntegrationName Name,
    FabIdentifier Fab,
    Kind DefaultKind,
    DateTimeOffset RegisteredAt,
    OperatorIdentifier RegisteredBy) : IDomainEvent;
