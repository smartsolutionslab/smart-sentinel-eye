using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Queries;

public class ListStreamsQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task Returns_one_dto_per_requested_camera_that_has_a_stream()
    {
        CameraIdentifier camera1 = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier camera2 = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier cameraWithoutStream = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = Seed(Munich, camera1, camera2);
        ListStreamsQueryHandler handler = NewHandler(streams);

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result = await handler.HandleAsync(
            new ListStreamsQuery([Munich], [camera1, camera2, cameraWithoutStream]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Select(dto => dto.CameraIdentifier).ShouldContain(camera1.Value);
        result.Value.Select(dto => dto.CameraIdentifier).ShouldContain(camera2.Value);
    }

    [Fact]
    public async Task Returns_an_empty_list_for_zero_identifiers()
    {
        ListStreamsQueryHandler handler = NewHandler(new InMemoryStreamRepository());

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result = await handler.HandleAsync(
            new ListStreamsQuery([Munich], Array.Empty<CameraIdentifier>()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_InvalidBatchSize_when_above_maximum()
    {
        IReadOnlyList<CameraIdentifier> tooMany = Enumerable
            .Range(0, ListStreamsDefaults.MaximumBatchSize + 1)
            .Select(_ => CameraIdentifier.From(Guid.CreateVersion7()))
            .ToList();
        ListStreamsQueryHandler handler = NewHandler(new InMemoryStreamRepository());

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result = await handler.HandleAsync(
            new ListStreamsQuery([Munich], tooMany),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ListStreamsError.InvalidBatchSize>();
    }

    /// <summary>
    /// FR-005 + FR-006 on the batch route: another fab's stream drops out of
    /// the result exactly like a camera that was never provisioned, so the two
    /// are indistinguishable from the caller's side.
    /// </summary>
    [Fact]
    public async Task A_stream_in_another_fab_is_omitted_like_one_that_does_not_exist()
    {
        CameraIdentifier inMunich = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier inDresden = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = Seed(Munich, inMunich);
        Add(streams, Dresden, inDresden);
        ListStreamsQueryHandler handler = NewHandler(streams);

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result = await handler.HandleAsync(
            new ListStreamsQuery([Dresden], [inMunich, inDresden]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(dto => dto.CameraIdentifier).ShouldBe([inDresden.Value]);
    }

    [Fact]
    public async Task A_multi_fab_caller_sees_both_plants()
    {
        CameraIdentifier inMunich = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier inDresden = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = Seed(Munich, inMunich);
        Add(streams, Dresden, inDresden);
        ListStreamsQueryHandler handler = NewHandler(streams);

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result = await handler.HandleAsync(
            new ListStreamsQuery([Munich, Dresden], [inMunich, inDresden]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(dto => dto.Fab).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    private static InMemoryStreamRepository Seed(FabIdentifier fab, params CameraIdentifier[] cameras)
    {
        InMemoryStreamRepository streams = new();
        Add(streams, fab, cameras);
        return streams;
    }

    private static void Add(InMemoryStreamRepository streams, FabIdentifier fab, params CameraIdentifier[] cameras)
    {
        foreach (CameraIdentifier camera in cameras)
        {
            Domain.Stream.Stream stream = new StreamBuilder()
                .WithFab(fab)
                .ForCamera(camera)
                .ProvisionedBy(AnAdmin)
                .At(FixedMoment)
                .Build();
            stream.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
            streams.Add(stream);
        }
        streams.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static ListStreamsQueryHandler NewHandler(InMemoryStreamRepository streams) =>
        new(new InMemoryStreamQuerySource(streams), new StaticWhepUrlBuilder());
}
