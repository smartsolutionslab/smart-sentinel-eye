using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.EventHandlers;

public class CameraRegisteredIntegrationEventHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task On_first_receipt_dispatches_ProvisionStreamCommand()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler command = NewCommandHandler(streams, gateway);
        CameraRegisteredIntegrationEventHandler handler =
            new(command, NullLogger<CameraRegisteredIntegrationEventHandler>.Instance);

        Guid camera = Guid.CreateVersion7();
        CameraRegisteredV1 message = new(
            Camera: camera,
            Name: "Line-1",
            Url: "rtsp://10.0.5.1/h264",
            RegisteredAt: FixedMoment,
            RegisteredBy: Guid.CreateVersion7());

        await handler.Handle(message);

        streams.Streams.Count.ShouldBe(1);
        streams.Streams.Single().Camera.Value.ShouldBe(camera);
        gateway.AddCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task On_redelivery_is_idempotent_because_the_command_handler_is()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler command = NewCommandHandler(streams, gateway);
        CameraRegisteredIntegrationEventHandler handler =
            new(command, NullLogger<CameraRegisteredIntegrationEventHandler>.Instance);

        Guid camera = Guid.CreateVersion7();
        CameraRegisteredV1 message = new(
            Camera: camera,
            Name: "Line-1",
            Url: "rtsp://10.0.5.1/h264",
            RegisteredAt: FixedMoment,
            RegisteredBy: Guid.CreateVersion7());

        await handler.Handle(message);
        await handler.Handle(message);

        streams.Streams.Count.ShouldBe(1);
        gateway.AddCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task On_command_failure_throws_so_Wolverine_redelivers()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new()
        {
            OnAddPath = (_, _) => throw new HttpRequestException("MediaMTX down"),
        };
        ProvisionStreamCommandHandler command = NewCommandHandler(streams, gateway);
        CameraRegisteredIntegrationEventHandler handler =
            new(command, NullLogger<CameraRegisteredIntegrationEventHandler>.Instance);

        CameraRegisteredV1 message = new(
            Camera: Guid.CreateVersion7(),
            Name: "Line-1",
            Url: "rtsp://10.0.5.1/h264",
            RegisteredAt: FixedMoment,
            RegisteredBy: Guid.CreateVersion7());

        Func<Task> act = () => handler.Handle(message);

        await act.ShouldThrowAsync<InvalidOperationException>();
    }

    private static ProvisionStreamCommandHandler NewCommandHandler(InMemoryStreamRepository streams, FakeRtspGateway gateway) =>
        new(
            streams,
            gateway,
            new FixedClock(FixedMoment),
            NullLogger<ProvisionStreamCommandHandler>.Instance);
}
