using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.EventHandlers;

public sealed class WebhookIntegrationRegisteredDomainEventHandler(
    IEventBus events,
    ILogger<WebhookIntegrationRegisteredDomainEventHandler> logger)
    : IDomainEventHandler<WebhookIntegrationRegisteredDomainEvent>
{
    public Task Handle(WebhookIntegrationRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        // Scaffold (red commit): publishes nothing yet.
        _ = events;
        _ = logger;
        return Task.CompletedTask;
    }
}
