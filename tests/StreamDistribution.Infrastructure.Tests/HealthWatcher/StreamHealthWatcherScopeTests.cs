using System.Globalization;
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
/// Regression cover for #1801. The watcher used to open one scope for the whole
/// sweep, so every camera after the first published its health change into a
/// message context the previous camera had already flushed — dropped with no
/// exception, no outbox row and nothing in the log.
///
/// <para>
/// The loss itself needs Wolverine's real outbox against Postgres to reproduce,
/// which ADR-0103 puts out of reach of a unit test. What is assertable here is
/// the structure that caused it: <b>a scope per camera, not one per sweep</b>.
/// Distinct handler instances are the evidence — a shared scope hands back the
/// same scoped graph, which is exactly what shared the spent outbox.
/// </para>
///
/// <para>
/// Also covers the ADR-0171 security review's rate-limit fix: the sweep fetches
/// every path's health in <b>one</b> <see cref="IRtspGateway.GetAllPathsHealthAsync"/>
/// call rather than one <c>GetPathHealthAsync</c> round trip per stream — the
/// call <see cref="StubGateway"/> exists to count.
/// </para>
/// </summary>
public class StreamHealthWatcherScopeTests
{
    private static readonly RtspPathHealth Unready =
        new(IsReady: false, LastError: "no frames", LastFrameAt: null, DetectedMode: TranscodeMode.Unknown);

    [Fact]
    public async Task Every_camera_in_a_sweep_is_handled_in_a_scope_of_its_own()
    {
        Guid[] cameras = [Camera(1), Camera(2), Camera(3), Camera(4)];

        var sweep = await DispatchAsync([.. cameras.Select(Stream)], _ => Unready);

        sweep.Scopes.Count.ShouldBe(4, "one scope per camera, not one for the sweep");
        sweep.Handled.Select(handled => handled.Camera).ShouldBe(cameras);

        // The assertion a count alone cannot make: four scopes that all resolved
        // the same instance would be four scopes in name only.
        sweep.Handled.Select(handled => handled.Handler).Distinct().Count()
            .ShouldBe(4, "each camera got its own scoped graph, and so its own outbox");
    }

    /// <summary>
    /// The rate-limit fix's own counterfactual (ADR-0171 security review):
    /// at production scale (250 cameras) the sweep must still issue exactly one
    /// MediaMTX call, not one per camera — ~125 hook calls/s at a 2 s poll would
    /// have saturated the shared <c>whep-authorize</c> limiter and started
    /// refusing real viewers.
    /// </summary>
    [Fact]
    public async Task A_sweep_of_250_cameras_issues_exactly_one_batch_call()
    {
        // Guid.CreateVersion7(), not the Camera(ordinal) helper: that helper's
        // fixed-width literal only has room for a 2-digit ordinal (1-99), and
        // this fact needs 250 distinct, genuinely unique cameras, not a
        // specific or reproducible value for any of them.
        (Guid Camera, MediaMtxPath Path, StreamState State)[] streams =
            [.. Enumerable.Range(1, 250).Select(_ => Stream(Guid.CreateVersion7()))];

        var sweep = await DispatchAsync(streams, _ => Unready);

        sweep.BatchCalls.ShouldBe(1, "250 cameras must cost one MediaMTX call, not 250");
        sweep.PerPathCalls.ShouldBe(
            0,
            "250 cameras must cost zero GetPathHealthAsync calls — a mix of one batch call plus "
            + "leftover per-path calls would still satisfy the BatchCalls assertion above alone");
        sweep.Scopes.Count.ShouldBe(250);
    }

    /// <summary>
    /// Every scope the sweep opens is also closed. A watcher polling every two
    /// seconds for the life of the process leaks a scoped graph per camera per
    /// poll otherwise — a slower failure than the one this file is named for,
    /// and one the fix could have introduced.
    /// </summary>
    [Fact]
    public async Task Every_scope_the_sweep_opens_is_disposed()
    {
        Guid[] cameras = [Camera(1), Camera(2)];

        var sweep = await DispatchAsync([.. cameras.Select(Stream)], _ => Unready);

        sweep.Scopes.Count.ShouldBe(2);
        sweep.Scopes.ShouldAllBe(scope => scope.Disposed);
    }

    /// <summary>
    /// A path MediaMTX's batch response does not report reads the same way a
    /// 404 from the old per-path probe did: "not registered", not a reason to
    /// skip the camera. Unlike the old per-path <see cref="HttpRequestException"/>
    /// case, a missing entry is not a transport failure — the batch call itself
    /// succeeded — so the camera still reaches the handler.
    /// </summary>
    [Fact]
    public async Task A_path_missing_from_the_batch_response_is_handled_as_not_registered_not_skipped()
    {
        var present = Camera(1);
        var missing = Camera(2);

        var sweep = await DispatchAsync(
            [Stream(present), Stream(missing)],
            _ => Unready,
            excludeFromBatch: [MediaMtxPath.For(CameraIdentifier.From(missing))]);

        sweep.Scopes.Count.ShouldBe(2, "a path absent from the batch is still dispatched, not skipped");
        sweep.Handled.Select(handled => handled.Camera).ShouldBe([present, missing]);
    }

    /// <summary>
    /// With one HTTP call standing in for what used to be N, there is no longer
    /// a per-camera transport failure to guard against — there is only a
    /// whole-tick one. This is that counterfactual: the batch call itself
    /// throwing must not crash the watcher (the next tick, two seconds later,
    /// tries again) and must not dispatch any camera from a result it never
    /// got.
    /// </summary>
    [Fact]
    public async Task A_failed_batch_call_skips_the_whole_tick_without_crashing_the_watcher()
    {
        Guid[] cameras = [Camera(1), Camera(2), Camera(3)];

        var sweep = await DispatchAsync(
            [.. cameras.Select(Stream)],
            _ => Unready,
            batchFailure: new HttpRequestException("MediaMTX unreachable"));

        sweep.BatchCalls.ShouldBe(1);
        sweep.Scopes.ShouldBeEmpty("no camera can be dispatched from a batch call that never answered");
        sweep.Handled.ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 028 T030 (research §4). A retired stream is skipped before its
    /// health is even looked up, so it never reaches the handler and opens no
    /// scope.
    ///
    /// <para>
    /// The listing query excludes these rows too, and this is the half a unit
    /// test can reach — ADR-0103 rules out both an in-memory provider and
    /// Testcontainers, so the query filter is only assertable through the Aspire
    /// integration test. It also closes the race the query cannot: a stream
    /// retired between the read and the dispatch.
    /// </para>
    ///
    /// <para>
    /// Why it matters more than a wasted dispatch: since #1801 the watcher
    /// announces <em>every</em> health change rather than one per sweep, so a
    /// retired stream that kept being dispatched would become a permanent
    /// source of health announcements and audit rows for hardware that does not
    /// exist — leaving the system noisier after this feature than before it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_retired_stream_never_reaches_the_handler_and_opens_no_scope()
    {
        var live = Camera(1);
        var retired = Camera(2);
        var alsoLive = Camera(3);

        var sweep = await DispatchAsync(
            [StreamIn(live, StreamState.Healthy),
             StreamIn(retired, StreamState.Retired),
             StreamIn(alsoLive, StreamState.Degraded)],
            _ => Unready);

        sweep.BatchCalls.ShouldBe(1, "a retired stream mixed into the sweep still costs one call, not more");
        sweep.Scopes.Count.ShouldBe(2, "the retired stream never reached the handler");

        // alsoLive, not merely live: skipping must continue the sweep, not end
        // it — a `break` would satisfy an assertion made about the first camera
        // alone.
        sweep.Handled.Select(handled => handled.Camera).ShouldBe([live, alsoLive]);
    }

    private static async Task<Sweep> DispatchAsync(
        IReadOnlyList<(Guid Camera, MediaMtxPath Path, StreamState State)> streams,
        Func<MediaMtxPath, RtspPathHealth> health,
        IEnumerable<MediaMtxPath>? excludeFromBatch = null,
        Exception? batchFailure = null)
    {
        Recorder recorder = new();

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddScoped<HealthCommandHandler, RecordingHandler>();

        await using var provider = services.BuildServiceProvider();

        CountingScopeFactory scopes = new(provider.GetRequiredService<IServiceScopeFactory>());
        StreamHealthWatcher watcher = new(scopes, new FixedClock(), NullLogger<StreamHealthWatcher>.Instance);

        var excluded = excludeFromBatch?.ToHashSet() ?? [];
        MediaMtxPath[] reported = [.. streams.Select(stream => stream.Path).Where(path => !excluded.Contains(path))];
        StubGateway gateway = new(reported, health, batchFailure);

        await watcher.DispatchAsync(streams, gateway, CancellationToken.None);

        return new Sweep(scopes.Scopes, recorder.Handled, gateway.BatchCalls, gateway.PerPathCalls);
    }

    private static Guid Camera(int ordinal) =>
        Guid.Parse($"01a02481-3b04-757e-97f3-c3e7d22a61{ordinal:d2}");

    private static (Guid Camera, MediaMtxPath Path, StreamState State) Stream(Guid camera) =>
        (camera, MediaMtxPath.For(CameraIdentifier.From(camera)), StreamState.Healthy);

    private static (Guid Camera, MediaMtxPath Path, StreamState State) StreamIn(Guid camera, StreamState state) =>
        (camera, MediaMtxPath.For(CameraIdentifier.From(camera)), state);

    private sealed record Sweep(
        IReadOnlyList<TrackedScope> Scopes,
        IReadOnlyList<(object Handler, Guid Camera)> Handled,
        int BatchCalls,
        int PerPathCalls);

    private sealed class Recorder
    {
        private readonly List<(object Handler, Guid Camera)> handled = [];

        public IReadOnlyList<(object Handler, Guid Camera)> Handled => handled;

        public void Record(object handler, Guid camera) => handled.Add((handler, camera));
    }

    private sealed class RecordingHandler(Recorder recorder) : HealthCommandHandler
    {
        public Task<Result<StreamState, ReportStreamHealthError>> HandleAsync(
            ReportStreamHealthCommand command, CancellationToken cancellationToken)
        {
            recorder.Record(this, command.Camera.Value);

            return Task.FromResult(Result<StreamState, ReportStreamHealthError>.Success(StreamState.Degraded));
        }
    }

    /// <summary>
    /// Wraps the real factory rather than replacing it, so the scoped lifetimes
    /// asserted on are the container's own and not a hand-rolled imitation.
    /// </summary>
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private readonly List<TrackedScope> scopes = [];

        public IReadOnlyList<TrackedScope> Scopes => scopes;

        public IServiceScope CreateScope()
        {
            TrackedScope scope = new(inner.CreateScope());
            scopes.Add(scope);

            return scope;
        }
    }

    private sealed class TrackedScope(IServiceScope inner) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => inner.ServiceProvider;

        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            inner.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            Disposed = true;

            if (inner is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
                return;
            }

            inner.Dispose();
        }
    }

    /// <summary>
    /// Stands in for MediaMTX's batch endpoint. <see cref="BatchCalls"/> is the
    /// counterfactual the rate-limit fix is about: it must stay 1 for a sweep
    /// tick regardless of how many cameras it covers. <see cref="PerPathCalls"/>
    /// is the counterfactual a <see cref="BatchCalls"/> assertion alone cannot
    /// make: a watcher that fell back to probing per path alongside the batch
    /// call would still leave <see cref="BatchCalls"/> at 1.
    /// </summary>
    private sealed class StubGateway : IRtspGateway
    {
        private readonly MediaMtxPath[] paths;
        private readonly Func<MediaMtxPath, RtspPathHealth> health;
        private readonly Exception? batchFailure;

        public StubGateway(
            IEnumerable<MediaMtxPath> paths, Func<MediaMtxPath, RtspPathHealth> health, Exception? batchFailure = null)
        {
            this.paths = [.. paths];
            this.health = health;
            this.batchFailure = batchFailure;
        }

        public int BatchCalls { get; private set; }

        public int PerPathCalls { get; private set; }

        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken)
        {
            PerPathCalls++;
            return Task.FromResult(health(path));
        }

        public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(CancellationToken cancellationToken)
        {
            BatchCalls++;

            if (batchFailure is not null)
            {
                throw batchFailure;
            }

            Dictionary<MediaMtxPath, RtspPathHealth> result = [];
            foreach (var path in paths)
            {
                result[path] = health(path);
            }

            return Task.FromResult<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>>(result);
        }

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaMtxPath>>([]);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            DateTimeOffset.Parse("2026-08-23T09:00:00Z", CultureInfo.InvariantCulture);
    }
}
