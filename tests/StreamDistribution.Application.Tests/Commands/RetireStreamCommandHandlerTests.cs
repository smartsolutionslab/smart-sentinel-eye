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
/// Spec 028 T025 — the stream follows the camera (FR-008), and the retirement
/// survives the SFU not cooperating (FR-008a).
/// </summary>
public class RetireStreamCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Retiring_a_stream_makes_it_terminal_and_removes_its_path()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        Domain.Stream.Stream existing = await SeedAsync(streams, camera);

        FakeRtspGateway gateway = new();

        Result<Option<StreamIdentifier>, RetireStreamError> result =
            await NewHandler(streams, gateway).HandleAsync(new RetireStreamCommand(camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        result.Value.Value.ShouldBe(existing.Id);

        streams.Streams.Single().State.ShouldBe(StreamState.Retired);
        gateway.RemoveCalls.ShouldBe([existing.Path]);
    }

    /// <summary>
    /// FR-008. The row is kept — retirement records that hardware <em>was</em>
    /// there, so a stream that vanished would lose the very fact it exists to
    /// record.
    /// </summary>
    [Fact]
    public async Task The_streams_row_is_kept_rather_than_deleted()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        await SeedAsync(streams, camera);

        await NewHandler(streams, new FakeRtspGateway())
            .HandleAsync(new RetireStreamCommand(camera), CancellationToken.None);

        streams.Streams.Count.ShouldBe(1);
    }

    /// <summary>
    /// FR-008a, and the reason the handler saves before it calls the gateway. An
    /// unreachable SFU must not lose the retirement: the row has to be terminal
    /// regardless, because that terminal state is what stops the health watcher
    /// sweeping a path that is on its way out.
    /// </summary>
    [Fact]
    public async Task A_gateway_failure_does_not_lose_the_retirement()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        await SeedAsync(streams, camera);

        FakeRtspGateway gateway = new()
        {
            OnRemovePath = _ => throw new HttpRequestException("MediaMTX unreachable"),
        };

        Result<Option<StreamIdentifier>, RetireStreamError> result =
            await NewHandler(streams, gateway).HandleAsync(new RetireStreamCommand(camera), CancellationToken.None);

        // Reported as a failure so the outbox redelivers and the path removal is
        // retried — unfinished cleanup, not lost work.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_RTSP_GATEWAY_UNAVAILABLE");

        // The part that matters: the aggregate is terminal anyway.
        streams.Streams.Single().State.ShouldBe(StreamState.Retired);
        streams.SaveCallCount.ShouldBeGreaterThan(0, "the retirement was committed before the gateway was called");
    }

    /// <summary>
    /// The redelivery case. Idempotent as "no event", not merely "no error":
    /// a second retirement that announced again would tell every subscriber the
    /// stream was retired twice, and the audit trail would agree.
    /// </summary>
    [Fact]
    public async Task Retiring_an_already_retired_stream_announces_nothing_further()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        InMemoryStreamRepository streams = new();
        await SeedAsync(streams, camera);

        FakeRtspGateway gateway = new();
        RetireStreamCommandHandler handler = NewHandler(streams, gateway);

        await handler.HandleAsync(new RetireStreamCommand(camera), CancellationToken.None);
        streams.Streams.Single().ClearPendingEvents();

        Result<Option<StreamIdentifier>, RetireStreamError> second =
            await handler.HandleAsync(new RetireStreamCommand(camera), CancellationToken.None);

        second.IsSuccess.ShouldBeTrue();
        streams.Streams.Single().PendingEvents.ShouldBeEmpty();
        streams.Streams.Single().State.ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// A camera registered without a resolvable fab is never provisioned a
    /// stream (spec 016 FR-004). Retiring it is a success with nothing to do —
    /// a failure would have the outbox redeliver the retirement forever for a
    /// camera that never had a stream.
    /// </summary>
    [Fact]
    public async Task A_camera_with_no_stream_is_a_success_with_nothing_retired()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();

        Result<Option<StreamIdentifier>, RetireStreamError> result = await NewHandler(streams, gateway)
            .HandleAsync(new RetireStreamCommand(CameraIdentifier.From(Guid.CreateVersion7())), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeFalse();
        gateway.RemoveCalls.ShouldBeEmpty("there was no path to remove");
    }

    private static async Task<Domain.Stream.Stream> SeedAsync(
        InMemoryStreamRepository streams,
        CameraIdentifier camera)
    {
        Domain.Stream.Stream existing = new StreamBuilder()
            .ForCamera(camera)
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();

        streams.Add(existing);
        await streams.SaveAsync(CancellationToken.None);
        existing.ClearPendingEvents();

        return existing;
    }

    private static RetireStreamCommandHandler NewHandler(
        InMemoryStreamRepository streams,
        FakeRtspGateway gateway) =>
        new(streams, gateway, new FixedClock(FixedMoment), NullLogger<RetireStreamCommandHandler>.Instance);
}
