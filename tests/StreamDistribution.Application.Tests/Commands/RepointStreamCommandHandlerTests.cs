using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

/// <summary>
/// Spec 029 T027 — the stream follows its camera's corrected address
/// (FR-013, FR-013a, FR-014).
/// </summary>
public class RepointStreamCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-24T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    private const string OriginalUrl = "rtsp://camera-sim:8554/original";
    private const string CorrectedUrl = "rtsp://camera-sim:8554/corrected";

    [Fact]
    public async Task Repointing_updates_the_aggregate_and_tells_the_SFU()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        Domain.Stream.Stream existing = await SeedAsync(streams, camera);

        FakeRtspGateway gateway = new();

        Result<Option<StreamIdentifier>, RepointStreamError> result =
            await NewHandler(streams, gateway).HandleAsync(
                new RepointStreamCommand(camera, CorrectedUrl), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(existing.Id);

        streams.Streams.Single().SourceUrl.Value.ShouldBe(CorrectedUrl);

        // Both halves. The aggregate holding the new address proves nothing
        // about what the SFU is actually pulling, which is the whole defect
        // this phase exists to prevent.
        gateway.RepointCalls.ShouldBe([(existing.Path, CorrectedUrl)]);
    }

    /// <summary>
    /// FR-014. The path is derived from the camera identifier, which is
    /// immutable, so a correction must not tear the path down — anyone already
    /// watching keeps watching, and the two-second health sweep never sees a
    /// missing path to announce about.
    /// </summary>
    [Fact]
    public async Task Repointing_does_not_remove_and_re_add_the_path()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        await SeedAsync(streams, camera);

        FakeRtspGateway gateway = new();

        await NewHandler(streams, gateway).HandleAsync(
            new RepointStreamCommand(camera, CorrectedUrl), CancellationToken.None);

        gateway.RemoveCalls.ShouldBeEmpty("a re-point must not leave a window with no path");
        gateway.AddCalls.ShouldBeEmpty();
    }

    /// <summary>
    /// FR-013a. The catalogue has already recorded the corrected address by the
    /// time this runs, so an unreachable SFU must not lose the correction here
    /// either: the aggregate holds the new address and the retry finishes the
    /// teardown rather than redoing it.
    /// </summary>
    [Fact]
    public async Task A_gateway_failure_does_not_lose_the_new_address()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        await SeedAsync(streams, camera);

        FakeRtspGateway gateway = new()
        {
            OnRepointPath = (_, _) => throw new HttpRequestException("MediaMTX unreachable"),
        };

        Result<Option<StreamIdentifier>, RepointStreamError> result =
            await NewHandler(streams, gateway).HandleAsync(
                new RepointStreamCommand(camera, CorrectedUrl), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_RTSP_GATEWAY_UNAVAILABLE");

        // The part that matters: the record already moved, so the retry is
        // cleanup rather than a redo.
        streams.Streams.Single().SourceUrl.Value.ShouldBe(CorrectedUrl);
        streams.SaveCallCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Repointing_to_the_address_it_already_has_is_a_success()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        await SeedAsync(streams, camera);

        FakeRtspGateway gateway = new();

        Result<Option<StreamIdentifier>, RepointStreamError> result =
            await NewHandler(streams, gateway).HandleAsync(
                new RepointStreamCommand(camera, OriginalUrl), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        streams.Streams.Single().SourceUrl.Value.ShouldBe(OriginalUrl);
    }

    /// <summary>
    /// A camera registered without a resolvable fab is never provisioned a
    /// stream (spec 016 FR-004). Reporting that as a failure would have the
    /// outbox redeliver the correction forever for a camera that never had one.
    /// </summary>
    [Fact]
    public async Task A_camera_with_no_stream_is_a_success_with_nothing_repointed()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();

        Result<Option<StreamIdentifier>, RepointStreamError> result =
            await NewHandler(streams, gateway).HandleAsync(
                new RepointStreamCommand(CameraIdentifier.From(Guid.CreateVersion7()), CorrectedUrl),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeFalse();
        gateway.RepointCalls.ShouldBeEmpty();
    }

    /// <summary>
    /// A retired stream is skipped rather than refused. The aggregate would
    /// throw, and rightly — but no retry would ever make it succeed, so
    /// reporting a failure would have the outbox redeliver forever.
    /// </summary>
    [Fact]
    public async Task A_retired_stream_is_skipped_rather_than_retried_forever()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        Domain.Stream.Stream existing = await SeedAsync(streams, camera);
        existing.Retire(new FixedClock(FixedMoment));

        FakeRtspGateway gateway = new();

        Result<Option<StreamIdentifier>, RepointStreamError> result =
            await NewHandler(streams, gateway).HandleAsync(
                new RepointStreamCommand(camera, CorrectedUrl), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeFalse();
        gateway.RepointCalls.ShouldBeEmpty();
        streams.Streams.Single().SourceUrl.Value.ShouldBe(OriginalUrl);
    }

    [Fact]
    public async Task An_address_this_context_cannot_parse_is_refused()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        await SeedAsync(streams, camera);

        Result<Option<StreamIdentifier>, RepointStreamError> result =
            await NewHandler(streams, new FakeRtspGateway()).HandleAsync(
                new RepointStreamCommand(camera, "http://not-rtsp.example/stream"),
                CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_INVALID_RTSP_SOURCE");
        streams.Streams.Single().SourceUrl.Value.ShouldBe(OriginalUrl);
    }

    private static async Task<Domain.Stream.Stream> SeedAsync(
        InMemoryStreamRepository streams,
        CameraIdentifier camera)
    {
        Domain.Stream.Stream existing = new StreamBuilder()
            .ForCamera(camera)
            .WithSourceUrl(StreamSourceUrl.From(OriginalUrl))
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();

        streams.Add(existing);
        await streams.SaveAsync(CancellationToken.None);
        existing.ClearPendingEvents();

        return existing;
    }

    private static RepointStreamCommandHandler NewHandler(
        InMemoryStreamRepository streams,
        FakeRtspGateway gateway) =>
        new(streams, gateway, new FixedClock(FixedMoment), NullLogger<RepointStreamCommandHandler>.Instance);
}
