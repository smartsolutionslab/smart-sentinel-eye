using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.EventHandlers;

/// <summary>
/// #3110. Mirrors <see cref="WebhookIntegrationRevokedDomainEventHandlerTests"/>:
/// the handler's whole job is to translate the in-process domain event into the
/// V1 on the bus, carrying the registering operator as the actor and nothing
/// that could stand in for the bearer token (ADR-0185 decision 4).
/// </summary>
public class WebhookIntegrationRegisteredDomainEventHandlerTests
{
    private static readonly DateTimeOffset RegisteredAtMoment =
        DateTimeOffset.Parse("2026-10-24T08:14:33Z", CultureInfo.InvariantCulture);

    private static readonly JsonSerializerOptions RelaxedEscaping =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task Publishes_exactly_one_WebhookIntegrationRegisteredV1_with_the_domain_events_fields()
    {
        FakeEventBus bus = new();
        WebhookIntegrationRegisteredDomainEventHandler handler = new(
            bus, NullLogger<WebhookIntegrationRegisteredDomainEventHandler>.Instance);
        var identifier = WebhookIntegrationIdentifier.New();
        var registeredBy = new OperatorIdentifier(Guid.CreateVersion7());
        WebhookIntegrationRegisteredDomainEvent domainEvent = new(
            identifier,
            WebhookIntegrationName.From("line-3"),
            FabIdentifier.From("munich"),
            Kind.From("WebhookAlarm"),
            RegisteredAtMoment,
            registeredBy);

        await handler.Handle(domainEvent, CancellationToken.None);

        var v1 = bus.Published.OfType<WebhookIntegrationRegisteredV1>().ShouldHaveSingleItem();
        v1.Integration.ShouldBe(identifier.Value);
        v1.IntegrationName.ShouldBe("line-3");
        v1.DefaultKind.ShouldBe("WebhookAlarm");
        v1.RegisteredAt.ShouldBe(RegisteredAtMoment);
        v1.Metadata.Fab.ShouldBe("munich");
        v1.Metadata.OccurredAt.ShouldBe(RegisteredAtMoment);
    }

    [Fact]
    public async Task Names_the_registering_operator_as_the_actor_and_stamps_a_version_7_event_identifier()
    {
        FakeEventBus bus = new();
        WebhookIntegrationRegisteredDomainEventHandler handler = new(
            bus, NullLogger<WebhookIntegrationRegisteredDomainEventHandler>.Instance);
        var registeredBy = new OperatorIdentifier(Guid.CreateVersion7());
        WebhookIntegrationRegisteredDomainEvent domainEvent = new(
            WebhookIntegrationIdentifier.New(),
            WebhookIntegrationName.From("line-3"),
            FabIdentifier.From("munich"),
            Kind.From("WebhookAlarm"),
            RegisteredAtMoment,
            registeredBy);

        await handler.Handle(domainEvent, CancellationToken.None);

        var v1 = bus.Published.OfType<WebhookIntegrationRegisteredV1>().ShouldHaveSingleItem();
        v1.Metadata.Actor.ShouldBe(registeredBy.Value);
        v1.Metadata.EventIdentifier.Version.ShouldBe(7);
    }

    [Fact]
    public async Task The_published_event_carries_neither_the_token_nor_its_hash()
    {
        FakeEventBus bus = new();
        WebhookIntegrationRegisteredDomainEventHandler handler = new(
            bus, NullLogger<WebhookIntegrationRegisteredDomainEventHandler>.Instance);
        (var integration, var token) = WebhookIntegration.Register(
            WebhookIntegrationName.From("line-3"),
            FabIdentifier.From("munich"),
            Kind.From("WebhookAlarm"),
            new OperatorIdentifier(Guid.CreateVersion7()),
            new FakeClock(RegisteredAtMoment));
        var domainEvent = integration.PendingEvents.OfType<WebhookIntegrationRegisteredDomainEvent>().ShouldHaveSingleItem();

        await handler.Handle(domainEvent, CancellationToken.None);

        // Relaxed escaping: the default encoder writes "+" as 002B, which would let a base64 token slip past a substring check.
        var json = JsonSerializer.Serialize(
            bus.Published.OfType<WebhookIntegrationRegisteredV1>().ShouldHaveSingleItem(),
            RelaxedEscaping);
        json.ShouldNotContain(token);
        json.ShouldNotContain(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
    }
}
