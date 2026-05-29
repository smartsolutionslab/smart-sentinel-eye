using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.RegisteredClient.Events;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.EventHandlers;

public class ClientRegisteredDomainEventHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Device_registration_publishes_DeviceRegisteredV1_with_split_devicetype_deviceid()
    {
        FakeEventBus bus = new();
        ClientRegisteredDomainEventHandler handler = new(
            bus, NullLogger<ClientRegisteredDomainEventHandler>.Instance);

        await handler.Handle(
            new ClientRegisteredDomainEvent(
                RegisteredClientIdentifier.New(),
                ClientId.From("plc-station-4"),
                ClientKind.Device,
                FabIdentifier.From("munich"),
                Now,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        DeviceRegisteredV1 v1 = bus.Published
            .OfType<DeviceRegisteredV1>().ShouldHaveSingleItem();
        v1.DeviceType.ShouldBe("plc");
        v1.DeviceIdentifier.ShouldBe("station-4");
        v1.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task Kiosk_registration_publishes_KioskEnrolledV1()
    {
        FakeEventBus bus = new();
        ClientRegisteredDomainEventHandler handler = new(
            bus, NullLogger<ClientRegisteredDomainEventHandler>.Instance);

        await handler.Handle(
            new ClientRegisteredDomainEvent(
                RegisteredClientIdentifier.New(),
                ClientId.From("kiosk-3"),
                ClientKind.Kiosk,
                FabIdentifier.From("munich"),
                Now,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        KioskEnrolledV1 v1 = bus.Published
            .OfType<KioskEnrolledV1>().ShouldHaveSingleItem();
        v1.ClientId.ShouldBe("kiosk-3");
        v1.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task WebhookIntegration_registration_does_NOT_fan_out_here()
    {
        // The rotate handler publishes WebhookIntegrationRotatedV1
        // directly; this handler must stay silent so we don't
        // double-publish.
        FakeEventBus bus = new();
        ClientRegisteredDomainEventHandler handler = new(
            bus, NullLogger<ClientRegisteredDomainEventHandler>.Instance);

        await handler.Handle(
            new ClientRegisteredDomainEvent(
                RegisteredClientIdentifier.New(),
                ClientId.From("webhook-qa"),
                ClientKind.WebhookIntegration,
                FabIdentifier.From("munich"),
                Now,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        bus.Published.ShouldBeEmpty();
    }
}
