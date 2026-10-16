using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 351, ADR-0144), captured green on the unmodified
/// <c>RepointStreamCommandHandler</c> before its existence and retirement checks
/// are extracted. The handler moves a live camera's address, so what is pinned
/// is the order it reaches its ports in (one shared call log over the
/// repository and the gateway: the address is parsed before any lookup, and the
/// durable save comes before the gateway is touched), the exact extent of its
/// <c>catch</c> (only <see cref="HttpRequestException"/>), and the text a log
/// reader sees. Existing tests pin outcomes; none of them can see an order.
///
/// <para>
/// Counterfactual this file was seen to catch: moving <c>SaveAsync</c> below the
/// gateway call turns
/// <see cref="A_successful_repoint_looks_the_stream_up_then_saves_then_tells_the_gateway"/>
/// red (the call log reads lookup-camera, repoint, save); dropping the retired
/// check turns <see cref="A_retired_stream_is_skipped_without_a_save_or_a_gateway_call"/> red.
/// </para>
///
/// <para>
/// Expected values are literals written from reading the unrefactored handler
/// and its error records.
/// </para>
/// </summary>
public class RepointStreamOrderCharacterisationTests
{
    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const string CorrectedUrl = "rtsp://camera-sim:8554/corrected";

    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000d2";

    private const string PathText = "cam-0197a1b2-0000-7000-8000-0000000000d2";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly CameraIdentifier Camera =
        CameraIdentifier.From(Guid.Parse(CameraText, CultureInfo.InvariantCulture));

    private sealed class Harness
    {
        public Harness()
        {
            Handler = new RepointStreamCommandHandler(
                new RecordingStreamRepository(Repository, Calls),
                new RecordingRtspGateway(Gateway, Calls),
                new FixedClock(Moment),
                Logger);
        }

        public List<string> Calls { get; } = [];

        public InMemoryStreamRepository Repository { get; } = new();

        public FakeRtspGateway Gateway { get; } = new();

        public CapturingLogger<RepointStreamCommandHandler> Logger { get; } = new();

        public RepointStreamCommandHandler Handler { get; }

        public async Task<Domain.Stream.Stream> SeedAsync(bool retired = false)
        {
            var stream = new StreamBuilder()
                .ForCamera(Camera)
                .WithSourceUrl(StreamSourceUrl.From(OriginalUrl))
                .ProvisionedBy(AnAdmin)
                .At(Moment)
                .Build();

            if (retired)
            {
                stream.Retire(new FixedClock(Moment));
            }

            Repository.Add(stream);
            await Repository.SaveAsync(CancellationToken.None);
            Calls.Clear();
            return stream;
        }

        public Task<Result<Option<StreamIdentifier>, RepointStreamError>> RepointAsync(string rtspSourceUrl) =>
            Handler.HandleAsync(new RepointStreamCommand(Camera, rtspSourceUrl, SourceChangedAt.From(Moment)), CancellationToken.None);
    }

    [Fact]
    public async Task An_address_that_cannot_be_parsed_is_refused_before_any_lookup_with_the_reason_in_the_message()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        var result = await harness.RepointAsync("http://not-rtsp.example/stream");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_INVALID_RTSP_SOURCE");
        result.Error.Message.ShouldBe("RTSP source rejected: value must start with 'rtsp://'. (Parameter 'value')");
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        harness.Calls.ShouldBeEmpty();
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_successful_repoint_looks_the_stream_up_then_saves_then_tells_the_gateway()
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync();

        var result = await harness.RepointAsync(CorrectedUrl);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        result.Value.Value.ShouldBe(stream.Id);
        harness.Calls.ShouldBe(["lookup-camera", "save", "repoint"]);
        harness.Gateway.RepointCalls.ShouldBe([(MediaMtxPath.From(PathText), CorrectedUrl)]);

        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe($"Re-pointed stream {stream.Id.Value} for camera {CameraText} at path {PathText}.");
        entry.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_gateway_failure_is_answered_as_unavailable_after_the_save_and_logged_with_its_exception()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        HttpRequestException boom = new("boom-7");
        harness.Gateway.OnRepointPath = (_, _) => throw boom;

        var result = await harness.RepointAsync(CorrectedUrl);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_RTSP_GATEWAY_UNAVAILABLE");
        result.Error.Message.ShouldBe("Could not re-point the stream's path in MediaMTX: boom-7");
        result.Error.Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        harness.Calls.ShouldBe(["lookup-camera", "save", "repoint"]);
        harness.Repository.SaveCallCount.ShouldBe(2);
        harness.Repository.Streams.Single().SourceUrl.Value.ShouldBe(CorrectedUrl);

        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe($"MediaMTX path re-point failed for camera {CameraText}; the stream already holds the new address.");
        entry.Exception.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task A_failure_that_is_not_an_http_failure_escapes_the_handler_after_the_save()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.Gateway.OnRepointPath = (_, _) => throw new InvalidOperationException("not-http-9");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => harness.RepointAsync(CorrectedUrl));

        thrown.Message.ShouldBe("not-http-9");
        harness.Calls.ShouldBe(["lookup-camera", "save", "repoint"]);
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_camera_with_no_stream_is_a_success_with_nothing_saved_and_the_absence_logged()
    {
        var harness = new Harness();

        var result = await harness.RepointAsync(CorrectedUrl);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeFalse();
        harness.Calls.ShouldBe(["lookup-camera"]);

        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe($"Camera {CameraText} address changed but has no provisioned stream; nothing to re-point.");
    }

    [Fact]
    public async Task A_retired_stream_is_skipped_without_a_save_or_a_gateway_call()
    {
        var harness = new Harness();
        await harness.SeedAsync(retired: true);

        var result = await harness.RepointAsync(CorrectedUrl);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeFalse();
        harness.Calls.ShouldBe(["lookup-camera"]);
        harness.Repository.Streams.Single().SourceUrl.Value.ShouldBe(OriginalUrl);

        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe($"Camera {CameraText} address changed but its stream is retired; not re-pointed.");
    }
}
