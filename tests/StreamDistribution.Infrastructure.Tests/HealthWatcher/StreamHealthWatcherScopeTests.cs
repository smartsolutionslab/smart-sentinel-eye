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
/// </summary>
public class StreamHealthWatcherScopeTests
{
    private static readonly RtspPathHealth Unready =
        new(IsReady: false, LastError: "no frames", LastFrameAt: null, DetectedMode: TranscodeMode.Unknown);

    [Fact]
    public async Task Every_camera_in_a_sweep_is_handled_in_a_scope_of_its_own()
    {
        Guid[] cameras = [Camera(1), Camera(2), Camera(3), Camera(4)];

        Sweep sweep = await DispatchAsync([.. cameras.Select(Stream)], _ => Unready);

        sweep.Scopes.Count.ShouldBe(4, "one scope per camera, not one for the sweep");
        sweep.Handled.Select(handled => handled.Camera).ShouldBe(cameras);

        // The assertion a count alone cannot make: four scopes that all resolved
        // the same instance would be four scopes in name only.
        sweep.Handled.Select(handled => handled.Handler).Distinct().Count()
            .ShouldBe(4, "each camera got its own scoped graph, and so its own outbox");
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

        Sweep sweep = await DispatchAsync([.. cameras.Select(Stream)], _ => Unready);

        sweep.Scopes.Count.ShouldBe(2);
        sweep.Scopes.ShouldAllBe(scope => scope.Disposed);
    }

    /// <summary>
    /// The probe comes first, and a failing one skips the camera without opening
    /// a scope for it. Guards the plausible refactor that hoists the scope above
    /// the try.
    /// </summary>
    [Fact]
    public async Task A_camera_whose_probe_fails_opens_no_scope_and_does_not_end_the_sweep()
    {
        Guid first = Camera(1);
        Guid unreachable = Camera(2);
        Guid last = Camera(3);
        MediaMtxPath failing = MediaMtxPath.For(CameraIdentifier.From(unreachable));

        Sweep sweep = await DispatchAsync(
            [.. new[] { first, unreachable, last }.Select(Stream)],
            path => path == failing
                ? throw new HttpRequestException("MediaMTX unreachable")
                : Unready);

        sweep.Scopes.Count.ShouldBe(2, "the unreachable camera never reached the handler");

        // last, not merely first: a sweep that stopped at the failure would still
        // satisfy a count assertion made against the survivors alone.
        sweep.Handled.Select(handled => handled.Camera).ShouldBe(new[] { first, last });
    }

    private static async Task<Sweep> DispatchAsync(
        IReadOnlyList<(Guid Camera, MediaMtxPath Path, StreamState State)> streams,
        Func<MediaMtxPath, RtspPathHealth> health)
    {
        Recorder recorder = new();

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddScoped<HealthCommandHandler, RecordingHandler>();

        await using ServiceProvider provider = services.BuildServiceProvider();

        CountingScopeFactory scopes = new(provider.GetRequiredService<IServiceScopeFactory>());
        StreamHealthWatcher watcher = new(scopes, new FixedClock(), NullLogger<StreamHealthWatcher>.Instance);

        await watcher.DispatchAsync(streams, new StubGateway(health), CancellationToken.None);

        return new Sweep(scopes.Scopes, recorder.Handled);
    }

    private static Guid Camera(int ordinal) =>
        Guid.Parse($"01a02481-3b04-757e-97f3-c3e7d22a61{ordinal:d2}");

    private static (Guid Camera, MediaMtxPath Path, StreamState State) Stream(Guid camera) =>
        (camera, MediaMtxPath.For(CameraIdentifier.From(camera)), StreamState.Healthy);

    private sealed record Sweep(
        IReadOnlyList<TrackedScope> Scopes,
        IReadOnlyList<(object Handler, Guid Camera)> Handled);

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

    private sealed class StubGateway(Func<MediaMtxPath, RtspPathHealth> health) : IRtspGateway
    {
        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            Task.FromResult(health(path));

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaMtxPath>>([]);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            DateTimeOffset.Parse("2026-08-23T09:00:00Z", CultureInfo.InvariantCulture);
    }
}
