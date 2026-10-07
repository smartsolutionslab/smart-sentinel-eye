using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts;
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
    private static readonly EventMetadata TestMetadata = MetadataForFab("munich");

    private static EventMetadata MetadataForFab(string? fab) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        fab,
        null);

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
            RegisteredBy: Guid.CreateVersion7(),
            Metadata: TestMetadata);

        await handler.Handle(message);

        streams.Streams.Count.ShouldBe(1);
        streams.Streams.Single().Camera.Value.ShouldBe(camera);
        gateway.AddCalls.Count.ShouldBe(1);
    }

    /// <summary>
    /// Spec 309 FR-004 (declared edit — <c>AddCalls.Count</c> was 1 and the
    /// name said "idempotent" of the gateway call itself; missed in the
    /// original characterisation table and corrected alongside its sibling,
    /// <c>ProvisionStreamCommandHandlerTests.Provision_for_an_existing_camera_returns_the_existing_identifier_and_re_asserts_its_path</c>).
    /// A redelivery through this handler reaches the same command handler as
    /// a direct redelivery, so it now re-asserts the MediaMTX path instead of
    /// skipping the gateway call. The handler is still idempotent in the
    /// sense that matters — one row, no error — the gateway call is simply
    /// no longer deduplicated.
    /// </summary>
    [Fact]
    public async Task On_redelivery_re_asserts_the_path_because_the_command_handler_does()
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
            RegisteredBy: Guid.CreateVersion7(),
            Metadata: TestMetadata);

        await handler.Handle(message);
        await handler.Handle(message);

        streams.Streams.Count.ShouldBe(1);
        gateway.AddCalls.Count.ShouldBe(2);
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
            RegisteredBy: Guid.CreateVersion7(),
            Metadata: TestMetadata);

        Func<Task> act = () => handler.Handle(message);

        await act.ShouldThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// US2. Dresden rather than munich on purpose: munich is the default
    /// everywhere else in these tests, so a hard-coded fab — or one silently
    /// falling back to a default — would pass a munich assertion.
    /// </summary>
    [Fact]
    public async Task A_camera_registered_in_dresden_provisions_a_dresden_stream()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler command = NewCommandHandler(streams, gateway);
        CameraRegisteredIntegrationEventHandler handler =
            new(command, NullLogger<CameraRegisteredIntegrationEventHandler>.Instance);

        CameraRegisteredV1 message = new(
            Camera: Guid.CreateVersion7(),
            Name: "Line-1",
            Url: "rtsp://10.0.5.1/h264",
            RegisteredAt: FixedMoment,
            RegisteredBy: Guid.CreateVersion7(),
            Metadata: MetadataForFab("dresden"));

        await handler.Handle(message);

        streams.Streams.Single().Fab.ShouldBe(FabIdentifier.From("dresden"));
    }

    /// <summary>
    /// FR-004. The assertion is on the downstream effect — no stream, no
    /// MediaMTX path — because "nothing threw" is also what a successful
    /// provision looks like from the handler's return.
    /// </summary>
    [Fact]
    public async Task A_camera_registered_without_a_fab_provisions_nothing_and_is_recorded()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler command = NewCommandHandler(streams, gateway);
        CapturingLogger<CameraRegisteredIntegrationEventHandler> logger = new();
        CameraRegisteredIntegrationEventHandler handler = new(command, logger);

        Guid camera = Guid.CreateVersion7();
        CameraRegisteredV1 message = new(
            Camera: camera,
            Name: "Line-1",
            Url: "rtsp://10.0.5.1/h264",
            RegisteredAt: FixedMoment,
            RegisteredBy: Guid.CreateVersion7(),
            Metadata: MetadataForFab(null));

        await handler.Handle(message);

        streams.Streams.ShouldBeEmpty();
        gateway.AddCalls.ShouldBeEmpty();

        var drop = logger.Entries.ShouldHaveSingleItem();
        drop.Level.ShouldBe(LogLevel.Warning);
        drop.Message.ShouldContain(camera.ToString());
        drop.Message.ShouldContain("without a fab");
    }

    private static ProvisionStreamCommandHandler NewCommandHandler(InMemoryStreamRepository streams, FakeRtspGateway gateway) =>
        new(
            streams,
            gateway,
            new FixedClock(FixedMoment),
            NullLogger<ProvisionStreamCommandHandler>.Instance);
}
