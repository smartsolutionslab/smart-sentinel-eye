using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Queries;

// Spec 459 (#3154), phase 4a: every StreamHealthDto field on the mapping as it stands. The
// stream is healthy at one moment and degraded at a later one, so LastSuccessAt cannot be
// satisfied by the degradation moment, and the WHEP URL is asserted whole rather than by suffix.
public class StreamHealthDtoMappingCharacterisationTests
{
    private static readonly DateTimeOffset Healthy =
        DateTimeOffset.Parse("2026-09-01T07:30:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_degraded_stream_maps_every_field_from_its_own_source()
    {
        var camera = CameraIdentifier.From(Guid.Parse("e1e1e1e1-0000-7000-8000-0000000000e1"));
        var fab = FabIdentifier.From("hamburg");
        var stream = new StreamBuilder()
            .WithFab(fab)
            .ForCamera(camera)
            .ProvisionedBy(OperatorIdentifier.From(Guid.CreateVersion7()))
            .At(Healthy.AddHours(-1))
            .Build();
        stream.ReportHealthy(TranscodeMode.Software, new FixedClock(Healthy));
        stream.ReportDegraded(StreamError.From("keyframes stopped"), new FixedClock(Healthy.AddSeconds(45)));
        InMemoryStreamRepository streams = new();
        streams.Add(stream);
        await streams.SaveAsync(CancellationToken.None);
        GetStreamQueryHandler handler = new(
            new InMemoryStreamQuerySource(streams),
            new StaticWhepUrlBuilder("http://sfu.test:9000"));

        var result = await handler.HandleAsync(new GetStreamQuery([fab], camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.CameraIdentifier.ShouldBe(camera.Value);
        dto.Fab.ShouldBe("hamburg");
        dto.State.ShouldBe("Degraded");
        dto.WhepUrl.ShouldBe($"http://sfu.test:9000/{stream.Path.Value}/whep");
        dto.WhepUrl.ShouldBe($"http://sfu.test:9000/cam-{camera.Value}/whep");
        dto.TranscodeMode.ShouldBe("Software");
        dto.LastSuccessAt.ShouldBe(Healthy);
        dto.Error.ShouldBe("keyframes stopped");
    }

    [Fact]
    public async Task A_freshly_provisioned_stream_has_no_last_success_and_no_error()
    {
        var camera = CameraIdentifier.From(Guid.CreateVersion7());
        var fab = FabIdentifier.From("hamburg");
        var stream = new StreamBuilder().WithFab(fab).ForCamera(camera).At(Healthy).Build();
        InMemoryStreamRepository streams = new();
        streams.Add(stream);
        await streams.SaveAsync(CancellationToken.None);
        GetStreamQueryHandler handler = new(new InMemoryStreamQuerySource(streams), new StaticWhepUrlBuilder());

        var result = await handler.HandleAsync(new GetStreamQuery([fab], camera), CancellationToken.None);

        result.Value.LastSuccessAt.ShouldBeNull();
        result.Value.Error.ShouldBeNull();
    }
}
