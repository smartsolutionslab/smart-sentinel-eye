using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="WebhookIntegrationRegisteredDomainEvent"/>
/// into the V1 integration event on the bus (spec 445, #3110). The publish rides
/// the Postgres outbox, so it commits with the registration's persistence
/// transaction. Publish only: no repository or Keycloak call, and the bearer
/// token never enters the event.
/// </summary>
public sealed class WebhookIntegrationRegisteredDomainEventHandler(
    IEventBus events,
    ILogger<WebhookIntegrationRegisteredDomainEventHandler> logger)
    : IDomainEventHandler<WebhookIntegrationRegisteredDomainEvent>
{
    public async Task Handle(WebhookIntegrationRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (identifier, name, fab, defaultKind, registeredAt, registeredBy) = domainEvent;

        await events.PublishAsync(
            new WebhookIntegrationRegisteredV1(
                identifier.Value,
                name.Value,
                defaultKind.Value,
                registeredAt,
                new EventMetadata(Guid.CreateVersion7(), registeredAt, fab.Value, registeredBy.Value)),
            cancellationToken);

        logger.PublishedWebhookIntegrationRegisteredV1(name, fab);
    }
}
