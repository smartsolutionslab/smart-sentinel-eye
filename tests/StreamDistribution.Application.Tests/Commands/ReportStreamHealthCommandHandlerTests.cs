using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

public class ReportStreamHealthCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Report_healthy_after_provisioning_transitions_the_stream_to_Healthy()
    {
        InMemoryStreamRepository streams = new();
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Stream.Stream existing = new StreamBuilder()
            .ForCamera(camera)
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();
        streams.Add(existing);
        await streams.SaveAsync(CancellationToken.None);

        ReportStreamHealthCommandHandler handler = NewHandler(streams);
        Result<StreamState, ReportStreamHealthError> result = await handler.HandleAsync(
            new ReportStreamHealthCommand(
                camera,
                Healthy(TranscodeMode.Passthrough),
                DeclareOffline: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(StreamState.Healthy);
        streams.Streams.Single().State.ShouldBe(StreamState.Healthy);
    }

    [Fact]
    public async Task Report_degraded_after_healthy_publishes_with_correct_from_to()
    {
        InMemoryStreamRepository streams = new();
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Stream.Stream existing = new StreamBuilder()
            .ForCamera(camera)
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();
        existing.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        streams.Add(existing);
        await streams.SaveAsync(CancellationToken.None);
        existing.ClearPendingEvents();

        ReportStreamHealthCommandHandler handler = NewHandler(streams);
        await handler.HandleAsync(
            new ReportStreamHealthCommand(
                camera,
                Unhealthy("source unreachable"),
                DeclareOffline: false),
            CancellationToken.None);

        streams.Streams.Single().State.ShouldBe(StreamState.Degraded);
        streams.Streams.Single().LastError.ShouldBe("source unreachable");
    }

    [Fact]
    public async Task Report_offline_directly_from_healthy_returns_InvalidStateTransition()
    {
        InMemoryStreamRepository streams = new();
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Stream.Stream existing = new StreamBuilder()
            .ForCamera(camera)
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();
        existing.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        streams.Add(existing);
        await streams.SaveAsync(CancellationToken.None);

        ReportStreamHealthCommandHandler handler = NewHandler(streams);
        Result<StreamState, ReportStreamHealthError> result = await handler.HandleAsync(
            new ReportStreamHealthCommand(
                camera,
                Unhealthy("exhausted"),
                DeclareOffline: true),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ReportStreamHealthError.InvalidStateTransition>();
    }

    [Fact]
    public async Task Report_healthy_when_already_healthy_does_not_change_state()
    {
        InMemoryStreamRepository streams = new();
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Stream.Stream existing = new StreamBuilder()
            .ForCamera(camera)
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();
        existing.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        streams.Add(existing);
        await streams.SaveAsync(CancellationToken.None);
        existing.ClearPendingEvents();

        ReportStreamHealthCommandHandler handler = NewHandler(streams);
        Result<StreamState, ReportStreamHealthError> result = await handler.HandleAsync(
            new ReportStreamHealthCommand(
                camera,
                Healthy(TranscodeMode.Passthrough),
                DeclareOffline: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        existing.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Report_for_an_unknown_camera_returns_StreamNotFound()
    {
        InMemoryStreamRepository streams = new();
        ReportStreamHealthCommandHandler handler = NewHandler(streams);

        Result<StreamState, ReportStreamHealthError> result = await handler.HandleAsync(
            new ReportStreamHealthCommand(
                CameraIdentifier.From(Guid.CreateVersion7()),
                Healthy(TranscodeMode.Passthrough),
                DeclareOffline: false),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ReportStreamHealthError.StreamNotFound>();
    }

    private static ReportStreamHealthCommandHandler NewHandler(InMemoryStreamRepository streams) =>
        new(
            streams,
            new FixedClock(FixedMoment),
            NullLogger<ReportStreamHealthCommandHandler>.Instance);

    private static RtspPathHealth Healthy(TranscodeMode mode) =>
        new(IsReady: true,
            LastError: null,
            LastFrameAt: FixedMoment,
            DetectedMode: mode);

    private static RtspPathHealth Unhealthy(string reason) =>
        new(IsReady: false,
            LastError: reason,
            LastFrameAt: null,
            DetectedMode: TranscodeMode.Unknown);
}
