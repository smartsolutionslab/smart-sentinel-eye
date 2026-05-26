using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

public class ProvisionStreamCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Provision_for_a_new_camera_creates_the_stream_and_registers_the_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand command = new(camera, "rtsp://10.0.5.1/h264", AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        streams.Streams.Count.ShouldBe(1);
        streams.Streams.Single().Camera.ShouldBe(camera);
        streams.SaveCallCount.ShouldBe(1);
        gateway.AddCalls.Count.ShouldBe(1);
        gateway.AddCalls.Single().Path.Value.ShouldBe($"cam-{camera.Value}");
        gateway.AddCalls.Single().Source.ShouldBe("rtsp://10.0.5.1/h264");
    }

    [Fact]
    public async Task Provision_for_an_existing_camera_returns_the_existing_identifier_and_does_not_re_register()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> firstResult =
            await handler.HandleAsync(first, CancellationToken.None);

        ProvisionStreamCommand redelivery = new(camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> secondResult =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        secondResult.IsSuccess.ShouldBeTrue();
        secondResult.Value.ShouldBe(firstResult.Value);
        streams.Streams.Count.ShouldBe(1);
        gateway.AddCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Provision_when_the_RTSP_gateway_is_unreachable_returns_RtspGatewayUnavailable()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new()
        {
            OnAddPath = (_, _) => throw new HttpRequestException("connection refused"),
        };
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        ProvisionStreamCommand command = new(
            CameraIdentifier.From(Guid.CreateVersion7()),
            "rtsp://10.0.5.1/h264",
            AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        ProvisionStreamError.RtspGatewayUnavailable err =
            result.Error.ShouldBeOfType<ProvisionStreamError.RtspGatewayUnavailable>();
        err.Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        streams.SaveCallCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Provision_with_blank_RTSP_source_returns_InvalidRtspSource(string source)
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        Result<StreamIdentifier, ProvisionStreamError> result = await handler.HandleAsync(
            new ProvisionStreamCommand(
                CameraIdentifier.From(Guid.CreateVersion7()), source, AnAdmin),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ProvisionStreamError.InvalidRtspSource>();
        gateway.AddCalls.ShouldBeEmpty();
    }

    private static ProvisionStreamCommandHandler NewHandler(InMemoryStreamRepository streams, FakeRtspGateway gateway) =>
        new(
            streams,
            gateway,
            new FixedClock(FixedMoment),
            NullLogger<ProvisionStreamCommandHandler>.Instance);
}
