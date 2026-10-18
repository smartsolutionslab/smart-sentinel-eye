using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.HealthWatcher;
using HealthCommandHandler = SmartSentinelEye.Shared.CQRS.ICommandHandler<
    SmartSentinelEye.StreamDistribution.Application.Commands.ReportStreamHealthCommand,
    SmartSentinelEye.Shared.Kernel.Result<
        SmartSentinelEye.StreamDistribution.Domain.Stream.StreamState,
        SmartSentinelEye.StreamDistribution.Application.Commands.ReportStreamHealthError>>;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.HealthWatcher;

/// <summary>
/// Spec 410 (#3021 item 5). Ordering invariants of the absence hold that the
/// behavioural tests in <see cref="StreamHealthWatcherAbsenceHoldTests"/> do not
/// pin: where the present-set is replaced, what a held tick leaves in the
/// degraded-since window, and that retirement is handled before the hold.
/// </summary>
public class StreamHealthWatcherAbsenceHoldInvariantTests
{
    private const string PathNotRegistered = "path not registered";

    private static readonly Guid CameraX = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6211");
    private static readonly Guid CameraY = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6212");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse(
        "2026-08-23T09:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_handler_that_throws_for_a_later_camera_does_not_stop_a_held_camera_being_reported_on_its_second_absent_sweep()
    {
        using var harness = new Harness([(CameraX, StreamState.Healthy), (CameraY, StreamState.Healthy)]);

        await harness.SweepAsync(Present(true, CameraX, CameraY));
        harness.ThrowFor = CameraY;
        await harness.SweepSwallowingAsync(Present(true, CameraY));
        harness.Recorder.Clear();

        await harness.SweepSwallowingAsync(Present(true, CameraY));

        var reported = harness.Recorder.Single(command => command.Camera.Value == CameraX);
        reported.Observation.LastError.ShouldBe(PathNotRegistered);
    }

    [Fact]
    public async Task A_held_tick_reports_nothing_and_leaves_the_degraded_window_so_the_next_absent_tick_declares_offline()
    {
        using var harness = new Harness([(CameraX, StreamState.Degraded)]);

        await harness.SweepAsync(Present(false, CameraX));
        harness.Recorder.Clear();

        harness.Clock.Now = Start + TimeSpan.FromMinutes(6);
        await harness.SweepAsync([]);
        harness.Recorder.ShouldBeEmpty();

        harness.Clock.Now = Start + TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(2);
        await harness.SweepAsync([]);

        var reported = harness.Recorder.Single();
        reported.Observation.LastError.ShouldBe(PathNotRegistered);
        reported.DeclareOffline.ShouldBeTrue();
    }

    [Fact]
    public async Task A_held_tick_does_not_restart_the_window_of_a_stream_that_was_still_healthy_when_it_was_read()
    {
        using var harness = new Harness([(CameraX, StreamState.Healthy)]);

        await harness.SweepAsync(Present(false, CameraX));
        harness.Recorder.Clear();

        harness.Clock.Now = Start + TimeSpan.FromMinutes(6);
        await harness.SweepAsync([]);
        harness.Recorder.ShouldBeEmpty();

        harness.States[CameraX] = StreamState.Degraded;
        harness.Clock.Now = Start + TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(2);
        await harness.SweepAsync([]);

        harness.Recorder.Single().DeclareOffline.ShouldBeTrue();
    }

    [Fact]
    public async Task A_stream_retired_while_its_path_was_in_the_last_sweep_starts_a_fresh_window_when_it_returns()
    {
        using var harness = new Harness([(CameraX, StreamState.Healthy)]);

        await harness.SweepAsync(Present(false, CameraX));
        harness.Recorder.Clear();

        harness.Clock.Now = Start + TimeSpan.FromMinutes(1);
        harness.States[CameraX] = StreamState.Retired;
        await harness.SweepAsync([]);
        harness.Recorder.ShouldBeEmpty();

        harness.Clock.Now = Start + TimeSpan.FromMinutes(6);
        harness.States[CameraX] = StreamState.Degraded;
        await harness.SweepAsync([]);

        var reported = harness.Recorder.Single();
        reported.Observation.LastError.ShouldBe(PathNotRegistered);
        reported.DeclareOffline.ShouldBeFalse();
    }

    private static Dictionary<MediaMtxPath, RtspPathHealth> Present(bool isReady, params Guid[] cameras) =>
        cameras.ToDictionary(
            camera => MediaMtxPath.For(CameraIdentifier.From(camera)),
            _ => new RtspPathHealth(IsReady: isReady, LastError: null, LastFrameAt: null, DetectedMode: TranscodeMode.Unknown));

    private sealed class Harness : IDisposable
    {
        private readonly StreamHealthWatcher watcher;
        private readonly ServiceProvider provider;
        private readonly StubGateway gateway = new();
        private readonly Guid[] order;

        public Harness((Guid Camera, StreamState State)[] streams)
        {
            order = [.. streams.Select(stream => stream.Camera)];
            States = streams.ToDictionary(stream => stream.Camera, stream => stream.State);
            Clock = new MutableClock { Now = Start };

            ServiceCollection services = new();
            services.AddSingleton(this);
            services.AddScoped<HealthCommandHandler, RecordingHandler>();
            provider = services.BuildServiceProvider();

            watcher = new StreamHealthWatcher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Clock,
                NullLogger<StreamHealthWatcher>.Instance);
        }

        public MutableClock Clock { get; }

        public Dictionary<Guid, StreamState> States { get; }

        public List<ReportStreamHealthCommand> Recorder { get; } = [];

        public List<Exception> Failures { get; } = [];

        public Guid? ThrowFor { get; set; }

        public void Dispose()
        {
            watcher.Dispose();
            provider.Dispose();
        }

        public Task SweepAsync(Dictionary<MediaMtxPath, RtspPathHealth> observed)
        {
            gateway.Answer = observed;

            return watcher.DispatchAsync(
                [.. order.Select(camera => (camera, MediaMtxPath.For(CameraIdentifier.From(camera)), States[camera]))],
                gateway,
                CancellationToken.None);
        }

        public async Task SweepSwallowingAsync(Dictionary<MediaMtxPath, RtspPathHealth> observed)
        {
            try
            {
                await SweepAsync(observed);
            }
            catch (InvalidOperationException failure)
            {
                Failures.Add(failure);
            }
        }
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset Now { get; set; }

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingHandler(Harness harness) : HealthCommandHandler
    {
        public Task<Result<StreamState, ReportStreamHealthError>> HandleAsync(
            ReportStreamHealthCommand command, CancellationToken cancellationToken)
        {
            harness.Recorder.Add(command);

            if (command.Camera.Value == harness.ThrowFor)
            {
                throw new InvalidOperationException("handler failed for this camera");
            }

            return Task.FromResult(Result<StreamState, ReportStreamHealthError>.Success(StreamState.Degraded));
        }
    }

    private sealed class StubGateway : IRtspGateway
    {
        public Dictionary<MediaMtxPath, RtspPathHealth> Answer { get; set; } = [];

        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the watcher must not probe per path");

        public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>>(Answer);

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaMtxPath>>([]);

        public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<MediaMtxPath, string>>(new Dictionary<MediaMtxPath, string>());
    }
}
