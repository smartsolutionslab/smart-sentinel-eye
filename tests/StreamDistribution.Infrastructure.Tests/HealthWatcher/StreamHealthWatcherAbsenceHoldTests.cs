using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.HealthWatcher;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;
using HealthCommandHandler = SmartSentinelEye.Shared.CQRS.ICommandHandler<
    SmartSentinelEye.StreamDistribution.Application.Commands.ReportStreamHealthCommand,
    SmartSentinelEye.Shared.Kernel.Result<
        SmartSentinelEye.StreamDistribution.Domain.Stream.StreamState,
        SmartSentinelEye.StreamDistribution.Application.Commands.ReportStreamHealthError>>;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.HealthWatcher;

/// <summary>
/// Spec 410 (#3021 item 5). MediaMTX lists paths by offset pagination, so a
/// path removed between two page requests shifts a neighbour onto a page that
/// was already read and the neighbour is missing from the result. The watcher
/// reads a missing path as "not registered", so a path that was present in the
/// last successful sweep and is absent from this one is held for one tick
/// instead of being reported as an outage that is not happening.
/// </summary>
public class StreamHealthWatcherAbsenceHoldTests
{
    private const string PathNotRegistered = "path not registered";

    private static readonly Guid CameraA = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6201");
    private static readonly Guid CameraB = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6202");
    private static readonly Guid CameraC = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6203");
    private static readonly Guid CameraD = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6204");

    [Fact]
    public async Task A_path_removed_between_page_requests_does_not_degrade_the_camera_shifted_out_of_the_read()
    {
        var sweepsStarted = 0;
        PagesHandler handler = new(page =>
        {
            if (page == 0)
            {
                sweepsStarted++;
            }

            return (sweepsStarted, page) switch
            {
                (1, 0) => Body(ReadyItem(CameraA), ReadyItem(CameraB)),
                (1, 1) => Body(ReadyItem(CameraC), ReadyItem(CameraD)),
                (_, 0) => Body(ReadyItem(CameraA), ReadyItem(CameraB)),
                _ => Body(ReadyItem(CameraD)),
            };
        });
        using var harness = Harness.OverRealGateway(handler, CameraA, CameraB, CameraC, CameraD);

        await harness.SweepAsync();
        harness.Recorder.Clear();
        await harness.SweepAsync();

        harness.Recorder.Handled.Select(command => command.Camera.Value).ShouldBe([CameraA, CameraB, CameraD]);
        harness.Recorder.Handled.ShouldAllBe(command => command.Observation.IsReady);
    }

    [Fact]
    public async Task A_path_absent_on_two_consecutive_sweeps_is_held_on_the_first_and_reported_on_the_second()
    {
        using var harness = Harness.OverStubGateway(
            Present(CameraA, CameraB),
            Present(CameraA),
            Present(CameraA));

        await harness.SweepAsync();
        harness.Recorder.Clear();

        await harness.SweepAsync();
        harness.Recorder.Handled.Select(command => command.Camera.Value).ShouldBe([CameraA]);
        harness.Recorder.Clear();

        await harness.SweepAsync();
        harness.Recorder.Handled.Select(command => command.Camera.Value).ShouldBe([CameraA, CameraB]);
        var reported = harness.Recorder.Handled.Single(command => command.Camera.Value == CameraB);
        reported.Observation.IsReady.ShouldBeFalse();
        reported.Observation.LastError.ShouldBe(PathNotRegistered);
    }

    [Fact]
    public async Task A_path_that_returns_after_one_missed_sweep_is_never_reported_as_not_registered()
    {
        using var harness = Harness.OverStubGateway(
            Present(CameraA, CameraB),
            Present(CameraA),
            Present(CameraA, CameraB));

        await harness.SweepAsync();
        await harness.SweepAsync();
        await harness.SweepAsync();

        harness.Recorder.Handled
            .Where(command => command.Camera.Value == CameraB)
            .ShouldAllBe(command => command.Observation.IsReady);
        harness.Recorder.Handled.ShouldNotContain(command => command.Observation.LastError == PathNotRegistered);
    }

    [Fact]
    public async Task A_path_never_seen_is_reported_as_not_registered_on_the_first_sweep_it_is_absent()
    {
        using var harness = Harness.OverStubGateway(Present(CameraA));

        await harness.SweepAsync();

        harness.Recorder.Handled.Select(command => command.Camera.Value).ShouldBe([CameraA, CameraB]);
        var reported = harness.Recorder.Handled.Single(command => command.Camera.Value == CameraB);
        reported.Observation.LastError.ShouldBe(PathNotRegistered);
    }

    [Fact]
    public async Task A_failed_tick_between_does_not_reset_the_hold()
    {
        using var harness = Harness.OverStubGateway(
            Present(CameraA, CameraB),
            new HttpRequestException("MediaMTX unreachable"),
            Present(CameraA),
            Present(CameraA));

        await harness.SweepAsync();
        await harness.SweepAsync();
        harness.Recorder.Clear();

        await harness.SweepAsync();
        harness.Recorder.Handled.Select(command => command.Camera.Value).ShouldBe([CameraA]);
        harness.Recorder.Clear();

        await harness.SweepAsync();
        harness.Recorder.Handled.Single(command => command.Camera.Value == CameraB)
            .Observation.LastError.ShouldBe(PathNotRegistered);
    }

    [Fact]
    public async Task A_failed_health_read_dispatches_no_camera_and_logs_one_warning()
    {
        using var harness = Harness.OverStubGateway(new HttpRequestException("MediaMTX unreachable"));

        await harness.SweepAsync();

        harness.Recorder.Handled.ShouldBeEmpty();
        harness.WatcherLog.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(1);
    }

    [Fact]
    public async Task A_sweep_with_a_held_camera_makes_one_batch_call_and_no_per_path_call()
    {
        using var harness = Harness.OverStubGateway(Present(CameraA, CameraB), Present(CameraA));

        await harness.SweepAsync();
        await harness.SweepAsync();

        var gateway = harness.Gateway.ShouldNotBeNull();
        gateway.BatchCalls.ShouldBe(2);
        gateway.PerPathCalls.ShouldBe(0);
    }

    private static Dictionary<MediaMtxPath, RtspPathHealth> Present(params Guid[] cameras) =>
        cameras.ToDictionary(
            camera => MediaMtxPath.For(CameraIdentifier.From(camera)),
            _ => new RtspPathHealth(IsReady: true, LastError: null, LastFrameAt: null, DetectedMode: TranscodeMode.Unknown));

    private static string ReadyItem(Guid camera) => $"{{\"name\":\"cam-{camera}\",\"ready\":true}}";

    private static string Body(params string[] items) =>
        $"{{\"pageCount\":2,\"items\":[{string.Join(",", items)}]}}";

    private sealed class Harness : IDisposable
    {
        private readonly StreamHealthWatcher watcher;
        private readonly ServiceProvider provider;
        private readonly IRtspGateway gateway;
        private readonly (Guid Camera, MediaMtxPath Path, StreamState State)[] streams;

        private Harness(IRtspGateway gateway, IReadOnlyList<Guid> cameras)
        {
            Recorder = new Recorder();
            WatcherLog = new CapturingLogger<StreamHealthWatcher>();

            ServiceCollection services = new();
            services.AddSingleton(Recorder);
            services.AddScoped<HealthCommandHandler, RecordingHandler>();
            provider = services.BuildServiceProvider();

            watcher = new StreamHealthWatcher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FixedClock(),
                WatcherLog);
            this.gateway = gateway;
            streams =
            [
                .. cameras.Select(camera =>
                    (camera, MediaMtxPath.For(CameraIdentifier.From(camera)), StreamState.Healthy)),
            ];
        }

        public Recorder Recorder { get; }

        public CapturingLogger<StreamHealthWatcher> WatcherLog { get; }

        public StubGateway? Gateway => gateway as StubGateway;

        public static Harness OverRealGateway(HttpMessageHandler mediaMtx, params Guid[] cameras)
        {
            MediaMtxRtspGateway real = new(
                new HttpClient(mediaMtx) { BaseAddress = new Uri("http://mediamtx.test") },
                NullLogger<MediaMtxRtspGateway>.Instance);

            return new Harness(real, cameras);
        }

        public static Harness OverStubGateway(params object[] answers) =>
            new(new StubGateway(answers), [CameraA, CameraB]);

        public void Dispose()
        {
            watcher.Dispose();
            provider.Dispose();
        }

        public Task SweepAsync() => watcher.DispatchAsync(streams, gateway, CancellationToken.None);
    }

    private sealed class Recorder
    {
        private readonly List<ReportStreamHealthCommand> handled = [];

        public IReadOnlyList<ReportStreamHealthCommand> Handled => handled;

        public void Record(ReportStreamHealthCommand command) => handled.Add(command);

        public void Clear() => handled.Clear();
    }

    private sealed class RecordingHandler(Recorder recorder) : HealthCommandHandler
    {
        public Task<Result<StreamState, ReportStreamHealthError>> HandleAsync(
            ReportStreamHealthCommand command, CancellationToken cancellationToken)
        {
            recorder.Record(command);

            return Task.FromResult(Result<StreamState, ReportStreamHealthError>.Success(StreamState.Degraded));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            DateTimeOffset.Parse("2026-08-23T09:00:00Z", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Answers one sweep per queued entry: a dictionary of present paths, or an
    /// exception to throw. The last answer repeats if more sweeps run.
    /// </summary>
    private sealed class StubGateway(object[] answers) : IRtspGateway
    {
        private int next;

        public int BatchCalls { get; private set; }

        public int PerPathCalls { get; private set; }

        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken)
        {
            PerPathCalls++;

            throw new InvalidOperationException("the watcher must not probe per path");
        }

        public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
            CancellationToken cancellationToken)
        {
            BatchCalls++;
            var answer = answers[Math.Min(next++, answers.Length - 1)];

            if (answer is Exception failure)
            {
                throw failure;
            }

            return Task.FromResult<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>>(
                (Dictionary<MediaMtxPath, RtspPathHealth>)answer);
        }

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaMtxPath>>([]);

        public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<MediaMtxPath, string>>(new Dictionary<MediaMtxPath, string>());
    }

    private sealed class PagesHandler(Func<int, string> bodyForPage) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            var page = uri.Query.TrimStart('?').Split('&')
                .Where(part => part.StartsWith("page=", StringComparison.Ordinal))
                .Select(part => int.Parse(part["page=".Length..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .First();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodyForPage(page), Encoding.UTF8, "application/json"),
            });
        }
    }
}
