using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Queries;

public class ListStreamsQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Returns_one_dto_per_requested_camera_that_has_a_stream()
    {
        CameraIdentifier camera1 = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier camera2 = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier cameraWithoutStream = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = Seed(camera1, camera2);
        ListStreamsQueryHandler handler = NewHandler(streams);

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result = await handler.HandleAsync(
            new ListStreamsQuery([camera1, camera2, cameraWithoutStream]),
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
            new ListStreamsQuery(Array.Empty<CameraIdentifier>()),
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
            new ListStreamsQuery(tooMany),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ListStreamsError.InvalidBatchSize>();
    }

    private static InMemoryStreamRepository Seed(params CameraIdentifier[] cameras)
    {
        InMemoryStreamRepository streams = new();
        foreach (CameraIdentifier camera in cameras)
        {
            Domain.Stream.Stream stream = Domain.Stream.Stream.Provision(camera, AnAdmin, new FixedClock(FixedMoment));
            stream.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
            streams.Add(stream);
        }
        streams.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
        return streams;
    }

    private static ListStreamsQueryHandler NewHandler(InMemoryStreamRepository streams) =>
        new(new InMemoryStreamQuerySource(streams), new StaticWhepUrlBuilder());
}
