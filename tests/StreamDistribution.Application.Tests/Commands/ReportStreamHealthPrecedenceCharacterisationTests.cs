using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
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
/// <c>ReportStreamHealthCommandHandler</c> before the observation-to-transition
/// chain is extracted into a private method. The handler runs on the two-second
/// health sweep, so what is pinned is the precedence of its three branches
/// (declared-offline beats ready beats degraded), the two default error texts,
/// the extent of its <c>try</c> (every branch is covered by the
/// <see cref="InvalidOperationException"/> catch, and the target named in the
/// refusal follows the same precedence), and one behaviour pinned as it is and
/// not as it ought to be: a <see cref="DbUpdateConcurrencyException"/> from
/// <c>SaveAsync</c> escapes the handler (the health-watcher side of board
/// issue #2783 is still open, so this refactor must not change it).
///
/// <para>
/// Counterfactual this file was seen to catch: swapping the <c>declareOffline</c>
/// and <c>IsReady</c> branches turns
/// <see cref="A_declared_offline_wins_over_a_ready_observation"/> red; changing
/// either default text turns the matching default-text test red.
/// </para>
///
/// <para>
/// Expected values are literals written from reading the unrefactored handler
/// and its error records.
/// </para>
/// </summary>
public class ReportStreamHealthPrecedenceCharacterisationTests
{
    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000e3";

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
            Handler = new ReportStreamHealthCommandHandler(
                Repository,
                new FixedClock(Moment.AddMinutes(10)),
                Logger);
        }

        public InMemoryStreamRepository Repository { get; } = new();

        public CapturingLogger<ReportStreamHealthCommandHandler> Logger { get; } = new();

        public ReportStreamHealthCommandHandler Handler { get; }

        public async Task<Domain.Stream.Stream> SeedAsync(StreamState state)
        {
            var stream = new StreamBuilder()
                .ForCamera(Camera)
                .ProvisionedBy(AnAdmin)
                .At(Moment)
                .Build();

            FixedClock clock = new(Moment);

            if (state == StreamState.Healthy || state == StreamState.Degraded)
            {
                stream.ReportHealthy(TranscodeMode.Passthrough, clock);
            }

            if (state == StreamState.Degraded)
            {
                stream.ReportDegraded(StreamError.From("seed-degraded-5"), clock);
            }

            if (state == StreamState.Retired)
            {
                stream.Retire(clock);
            }

            Repository.Add(stream);
            await Repository.SaveAsync(CancellationToken.None);
            return stream;
        }

        public Task<Result<StreamState, ReportStreamHealthError>> ReportAsync(
            RtspPathHealth observation,
            bool declareOffline) =>
            Handler.HandleAsync(
                new ReportStreamHealthCommand(Camera, observation, declareOffline),
                CancellationToken.None);
    }

    [Fact]
    public async Task A_declared_offline_wins_over_a_ready_observation_and_takes_the_offline_default_text()
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync(StreamState.Degraded);

        var result = await harness.ReportAsync(Observation(isReady: true, lastError: null), declareOffline: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(StreamState.Offline);
        stream.State.ShouldBe(StreamState.Offline);
        stream.LastError!.Value.ShouldBe("offline (no frames within retry window)");
        harness.Repository.SaveCallCount.ShouldBe(2);
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_ready_observation_that_is_not_declared_offline_makes_the_stream_healthy_and_clears_the_error()
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync(StreamState.Degraded);

        var result = await harness.ReportAsync(
            Observation(isReady: true, lastError: "ignored-8", mode: TranscodeMode.Software),
            declareOffline: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(StreamState.Healthy);
        stream.State.ShouldBe(StreamState.Healthy);
        stream.TranscodeMode.ShouldBe(TranscodeMode.Software);
        stream.LastError.ShouldBeNull();
        harness.Repository.SaveCallCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_not_ready_observation_without_an_error_degrades_the_stream_with_the_watcher_window_default_text()
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync(StreamState.Healthy);

        var result = await harness.ReportAsync(Observation(isReady: false, lastError: null), declareOffline: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(StreamState.Degraded);
        stream.LastError!.Value.ShouldBe("no frame within the health-watcher window");
    }

    [Fact]
    public async Task A_supplied_error_wins_over_the_degraded_default_text()
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync(StreamState.Healthy);

        await harness.ReportAsync(Observation(isReady: false, lastError: "supplied-degraded-3"), declareOffline: false);

        stream.LastError!.Value.ShouldBe("supplied-degraded-3");
    }

    [Fact]
    public async Task A_supplied_error_wins_over_the_offline_default_text()
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync(StreamState.Degraded);

        await harness.ReportAsync(Observation(isReady: false, lastError: "supplied-offline-4"), declareOffline: true);

        stream.State.ShouldBe(StreamState.Offline);
        stream.LastError!.Value.ShouldBe("supplied-offline-4");
    }

    [Fact]
    public async Task A_rejected_transition_is_answered_as_a_conflict_naming_from_and_to_and_is_logged_without_saving()
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync(StreamState.Healthy);

        var result = await harness.ReportAsync(Observation(isReady: false, lastError: null), declareOffline: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_INVALID_STATE_TRANSITION");
        result.Error.Message.ShouldBe(
            $"Cannot transition stream from Healthy to Offline: Stream {stream.Id.Value} cannot transition from Healthy directly to Offline; must pass through Degraded.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        harness.Repository.SaveCallCount.ShouldBe(1);

        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe($"Rejected health transition for camera {CameraText}.");
        entry.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe(
            $"Stream {stream.Id.Value} cannot transition from Healthy directly to Offline; must pass through Degraded.");
    }

    [Theory]
    [InlineData(true, false, "Healthy", "ReportHealthy")]
    [InlineData(false, false, "Degraded", "ReportDegraded")]
    [InlineData(true, true, "Offline", "ReportOffline")]
    [InlineData(false, true, "Offline", "ReportOffline")]
    public async Task A_report_on_a_retired_stream_is_rejected_and_names_the_target_that_was_attempted(
        bool isReady,
        bool declareOffline,
        string target,
        string behaviour)
    {
        var harness = new Harness();
        var stream = await harness.SeedAsync(StreamState.Retired);

        var result = await harness.ReportAsync(Observation(isReady, lastError: null), declareOffline);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_INVALID_STATE_TRANSITION");
        result.Error.Message.ShouldBe(
            $"Cannot transition stream from Retired to {target}: Stream {stream.Id.Value} is retired; {behaviour} cannot change a retired stream's state.");
        harness.Repository.SaveCallCount.ShouldBe(1);
        harness.Logger.Entries.ShouldHaveSingleItem().Message.ShouldBe(
            $"Rejected health transition for camera {CameraText}.");
    }

    [Fact]
    public async Task A_concurrency_conflict_from_the_save_escapes_the_handler_unhandled()
    {
        var harness = new Harness();
        await harness.SeedAsync(StreamState.Healthy);
        harness.Repository.OnSave = () => throw new DbUpdateConcurrencyException("race-2783");

        var thrown = await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => harness.ReportAsync(Observation(isReady: false, lastError: null), declareOffline: false));

        thrown.Message.ShouldBe("race-2783");
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_camera_is_answered_as_not_found_naming_the_camera()
    {
        var harness = new Harness();

        var result = await harness.ReportAsync(Observation(isReady: true, lastError: null), declareOffline: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("STREAM_NOT_FOUND");
        result.Error.Message.ShouldBe($"No stream is provisioned for camera {CameraText}.");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        harness.Repository.SaveCallCount.ShouldBe(0);
        harness.Logger.Entries.ShouldBeEmpty();
    }

    private static RtspPathHealth Observation(
        bool isReady,
        string? lastError,
        TranscodeMode? mode = null) =>
        new(isReady, lastError, isReady ? Moment : null, mode ?? TranscodeMode.Passthrough);
}
