using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Tests;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

// Spec 459 (#3154), phase 4a: every field of EventDto, RegisteredEventTypeDto, SourceModeDto
// and WebhookIntegrationDto on the mapping as it stands, each with a value distinct from its
// same-typed siblings.
public class EventIngestionDtoMappingCharacterisationTests
{
    private static readonly FabIdentifier Hamburg = FabIdentifier.From("hamburg");

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    [Fact]
    public async Task EventDto_maps_every_field_from_its_own_source()
    {
        var identifier = EventIdentifier.New();
        var occurredAt = At("2026-04-01T10:00:00Z");
        var ingestedAt = At("2026-04-01T10:00:05Z");
        var @event = new EventBuilder()
            .WithIdentifier(identifier)
            .WithFab("hamburg")
            .WithSource(Source.Webhook)
            .WithDevice("press-9")
            .WithKind("DefectDetected")
            .WithOccurredAt(occurredAt)
            .WithPayload("""{"score":0.97}""")
            .WithClock(ingestedAt)
            .Build();
        GetEventQueryHandler handler = new(new TestEventQuerySource([@event]));

        var result = await handler.HandleAsync(new GetEventQuery([Hamburg], identifier), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.EventIdentifier.ShouldBe(identifier.Value);
        dto.Fab.ShouldBe("hamburg");
        dto.Source.ShouldBe("webhook");
        dto.Device.ShouldBe("press-9");
        dto.Kind.ShouldBe("DefectDetected");
        dto.OccurredAt.ShouldBe(occurredAt);
        dto.IngestedAt.ShouldBe(ingestedAt);
        dto.Payload.ShouldBe("""{"score":0.97}""");
    }

    [Fact]
    public async Task RegisteredEventTypeDto_maps_every_field_from_its_own_source()
    {
        var registeredBy = Guid.Parse("0e0e0e0e-0000-7000-8000-00000000000e");
        var registeredAt = At("2026-04-02T11:00:00Z");
        var eventType = RegisteredEventType.Register(
            Hamburg, Kind.From("DefectDetected"), OperatorIdentifier.From(registeredBy), new FakeClock(registeredAt));
        ListEventTypesQueryHandler handler = new(new TestRegisteredEventTypeQuerySource([eventType]));

        var result = await handler.HandleAsync(new ListEventTypesQuery([Hamburg]), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.EventTypeId.ShouldBe(eventType.Id.Value);
        dto.Fab.ShouldBe("hamburg");
        dto.Kind.ShouldBe("DefectDetected");
        dto.State.ShouldBe(RegistrationState.Registered.Value);
        dto.RegisteredAt.ShouldBe(registeredAt);
        dto.RegisteredBy.ShouldBe(registeredBy);
        dto.Version.ShouldBe(eventType.Version);
    }

    [Fact]
    public async Task SourceModeDto_maps_every_field_from_its_own_source()
    {
        var declaredBy = Guid.Parse("0f0f0f0f-0000-7000-8000-00000000000f");
        var declaredAt = At("2026-04-03T12:00:00Z");
        var sourceMode = SourceMode.Declare(
            Hamburg, Source.Manual, EventTypeMode.Strict, OperatorIdentifier.From(declaredBy), new FakeClock(declaredAt));
        ListSourceModesQueryHandler handler = new(new TestSourceModeQuerySource([sourceMode]));

        var result = await handler.HandleAsync(new ListSourceModesQuery([Hamburg]), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.SourceModeId.ShouldBe(sourceMode.Id.Value);
        dto.Fab.ShouldBe("hamburg");
        dto.Source.ShouldBe("manual");
        dto.Mode.ShouldBe(EventTypeMode.Strict.Value);
        dto.DeclaredAt.ShouldBe(declaredAt);
        dto.DeclaredBy.ShouldBe(declaredBy);
        dto.Version.ShouldBe(sourceMode.Version);
    }

    [Fact]
    public async Task WebhookIntegrationDto_maps_every_field_for_an_active_integration()
    {
        var registeredAt = At("2026-04-04T13:00:00Z");
        (var integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("vision-gateway"),
            Hamburg,
            Kind.From("DefectDetected"),
            new OperatorIdentifier(Guid.CreateVersion7()),
            new FakeClock(registeredAt));
        ListWebhookIntegrationsQueryHandler handler = new(new TestWebhookIntegrationQuerySource([integration]));

        var result = await handler.HandleAsync(
            new ListWebhookIntegrationsQuery([Hamburg], IncludeRevoked: true), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.Identifier.ShouldBe(integration.Id.Value);
        dto.Version.ShouldBe(integration.Version);
        dto.Name.ShouldBe("vision-gateway");
        dto.Fab.ShouldBe("hamburg");
        dto.DefaultKind.ShouldBe("DefectDetected");
        dto.RegisteredAt.ShouldBe(registeredAt);
        dto.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task WebhookIntegrationDto_maps_the_exact_revocation_moment()
    {
        var registeredAt = At("2026-04-04T13:00:00Z");
        var revokedAt = At("2026-04-05T14:30:00Z");
        (var integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("vision-gateway"),
            Hamburg,
            Kind.From("DefectDetected"),
            new OperatorIdentifier(Guid.CreateVersion7()),
            new FakeClock(registeredAt));
        integration.Revoke(new FakeClock(revokedAt));
        ListWebhookIntegrationsQueryHandler handler = new(new TestWebhookIntegrationQuerySource([integration]));

        var result = await handler.HandleAsync(
            new ListWebhookIntegrationsQuery([Hamburg], IncludeRevoked: true), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.RegisteredAt.ShouldBe(registeredAt);
        dto.RevokedAt.ShouldBe(revokedAt);
    }
}
