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

    [Fact]
    public async Task Returns_the_stream_health_DTO_for_a_provisioned_camera()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = SeededWith(camera, state =>
        {
            state.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        });
        GetStreamQueryHandler handler = NewHandler(streams);

        Result<StreamHealthDto, GetStreamError> result =
            await handler.HandleAsync(new GetStreamQuery(camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CameraIdentifier.ShouldBe(camera.Value);
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
            new GetStreamQuery(CameraIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetStreamError.StreamNotFound>();
    }

    [Fact]
    public async Task DTO_carries_the_LastError_when_the_stream_is_degraded()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = SeededWith(camera, state =>
        {
            state.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
            state.ReportDegraded("source unreachable", new FixedClock(FixedMoment.AddSeconds(15)));
        });
        GetStreamQueryHandler handler = NewHandler(streams);

        Result<StreamHealthDto, GetStreamError> result =
            await handler.HandleAsync(new GetStreamQuery(camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("Degraded");
        result.Value.Error.ShouldBe("source unreachable");
    }

    private static InMemoryStreamRepository SeededWith(CameraIdentifier camera, Action<Domain.Stream.Stream> setup)
    {
        InMemoryStreamRepository streams = new();
        Domain.Stream.Stream stream = new StreamBuilder()
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
