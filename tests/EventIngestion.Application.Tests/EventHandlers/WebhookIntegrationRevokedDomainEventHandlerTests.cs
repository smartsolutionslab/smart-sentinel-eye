using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.Contracts.EventIngestion;

namespace SmartSentinelEye.EventIngestion.Application.Tests.EventHandlers;

/// <summary>
/// Spec 264 (#2206). Mirrors <see cref="EventIngestedDomainEventHandlerTests"/>'
/// shape: the handler's whole job is to translate the in-process domain event
/// into the V1 on the bus (plan.md §5.1). Red: compile, until
/// <c>WebhookIntegrationRevokedDomainEventHandler</c> and the domain event's
/// <c>Fab</c> field exist.
/// </summary>
public class WebhookIntegrationRevokedDomainEventHandlerTests
{
    private static readonly DateTimeOffset RevokedAtMoment =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publishes_exactly_one_WebhookIntegrationRevokedV1_with_the_domain_events_fields()
    {
        FakeEventBus bus = new();
        WebhookIntegrationRevokedDomainEventHandler handler = new(
            bus, NullLogger<WebhookIntegrationRevokedDomainEventHandler>.Instance);

        WebhookIntegrationName name = WebhookIntegrationName.From("w");
        FabIdentifier fab = FabIdentifier.From("munich");
        WebhookIntegrationRevokedDomainEvent domainEvent = new(name, fab, RevokedAtMoment);

        await handler.Handle(domainEvent, CancellationToken.None);

        WebhookIntegrationRevokedV1 v1 = bus.Published.OfType<WebhookIntegrationRevokedV1>().ShouldHaveSingleItem();
        v1.IntegrationName.ShouldBe("w");
        v1.RevokedAt.ShouldBe(RevokedAtMoment);
        v1.Metadata.Fab.ShouldBe("munich");
        v1.Metadata.OccurredAt.ShouldBe(RevokedAtMoment);
        v1.Metadata.Actor.ShouldBeNull();
    }
}
