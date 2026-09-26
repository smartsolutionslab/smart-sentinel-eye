using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="WebhookIntegrationRevokedDomainEvent"/>
/// into the V1 integration event on the bus (spec 264, #2206). Per ADR-0088
/// the publish rides Wolverine's Postgres outbox, so it commits with the
/// revoke's persistence transaction. This is the whole of the crossing: it
/// makes no Keycloak or repository call, matching the publish-only invariant
/// every domain-event handler on this path holds.
/// </summary>
public sealed class WebhookIntegrationRevokedDomainEventHandler(
    IEventBus events,
    ILogger<WebhookIntegrationRevokedDomainEventHandler> logger)
    : IDomainEventHandler<WebhookIntegrationRevokedDomainEvent>
{
    public async Task Handle(WebhookIntegrationRevokedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (name, fab, revokedAt) = domainEvent;

        await events.PublishAsync(
            new WebhookIntegrationRevokedV1(
                name.Value,
                revokedAt,
                new EventMetadata(Guid.CreateVersion7(), revokedAt, fab.Value, null)),
            cancellationToken);

        logger.PublishedWebhookIntegrationRevokedV1(name, fab);
    }
}
