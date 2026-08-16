using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Queries;

public class GetStreamQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task Returns_the_stream_health_DTO_for_a_provisioned_camera()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = SeededWith(Munich, camera, state =>
        {
            state.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        });
        GetStreamQueryHandler handler = NewHandler(streams);

        Result<StreamHealthDto, GetStreamError> result =
            await handler.HandleAsync(new GetStreamQuery([Munich], camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CameraIdentifier.ShouldBe(camera.Value);
        result.Value.Fab.ShouldBe("munich");
        result.Value.State.ShouldBe("Healthy");
        result.Value.WhepUrl.ShouldEndWith($"/cam-{camera.Value}/whep");
        result.Value.TranscodeMode.ShouldBe("Passthrough");
        result.Value.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_StreamNotFound_when_no_stream_exists_for_the_camera()
    {
        InMemoryStreamRepository streams = new();
        GetStreamQueryHandler handler = NewHandler(streams);

        Result<StreamHealthDto, GetStreamError> result = await handler.HandleAsync(
            new GetStreamQuery([Munich], CameraIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetStreamError.StreamNotFound>();
    }

    /// <summary>
    /// FR-006. Not forbidden — the same failure a camera with no stream
    /// produces, so the caller cannot learn that the stream exists. A stream
    /// record carries the MediaMTX path its video is served on, which is
    /// exactly what makes the existence worth withholding.
    /// </summary>
    [Fact]
    public async Task A_stream_in_a_fab_the_caller_does_not_hold_is_reported_as_not_found()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = SeededWith(Munich, camera, _ => { });
        GetStreamQueryHandler handler = NewHandler(streams);

        Result<StreamHealthDto, GetStreamError> result =
            await handler.HandleAsync(new GetStreamQuery([Dresden], camera), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetStreamError.StreamNotFound>();
    }

    [Fact]
    public async Task A_multi_fab_caller_reaches_a_stream_in_either_of_their_fabs()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = SeededWith(Dresden, camera, _ => { });
        GetStreamQueryHandler handler = NewHandler(streams);

        Result<StreamHealthDto, GetStreamError> result = await handler.HandleAsync(
            new GetStreamQuery([Munich, Dresden], camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task DTO_carries_the_LastError_when_the_stream_is_degraded()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = SeededWith(Munich, camera, state =>
        {
            state.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
            state.ReportDegraded("source unreachable", new FixedClock(FixedMoment.AddSeconds(15)));
        });
        GetStreamQueryHandler handler = NewHandler(streams);

        Result<StreamHealthDto, GetStreamError> result =
            await handler.HandleAsync(new GetStreamQuery([Munich], camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("Degraded");
        result.Value.Error.ShouldBe("source unreachable");
    }

    private static InMemoryStreamRepository SeededWith(FabIdentifier fab, CameraIdentifier camera, Action<Domain.Stream.Stream> setup)
    {
        InMemoryStreamRepository streams = new();
        Domain.Stream.Stream stream = new StreamBuilder()
            .WithFab(fab)
            .ForCamera(camera)
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();
        setup(stream);
        streams.Add(stream);
        streams.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
        return streams;
    }

    private static GetStreamQueryHandler NewHandler(InMemoryStreamRepository streams) =>
        new(new InMemoryStreamQuerySource(streams), new StaticWhepUrlBuilder());
}
