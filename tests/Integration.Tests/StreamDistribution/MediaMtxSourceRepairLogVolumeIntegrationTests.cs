using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// #2994 / spec 388. The real <see cref="MediaMtxSourceRepairSweep"/> over real
/// Postgres (the Aspire fixture) and a MediaMTX stand-in that refuses every PATCH:
/// the sweep must reuse one repair across its ticks, so the refusal Warns once
/// and the ticks after it log at Debug. And the startup pass's trailing repair
/// must report its own failure after the summary rather than throw out of
/// <see cref="MediaMtxReconciler.ReconcileOnceAsync"/>. Harness copied from
/// <see cref="MediaMtxReconcilerPassCharacterisationTests"/>.
/// </summary>
[Collection(AspireCollection.Name)]
public class MediaMtxSourceRepairLogVolumeIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string Camera = "0197a1b2-0000-7000-8000-000000000b01";
    private const string PersistedSource = "rtsp://10.0.5.2/h264";
    private const string ReportedSource = "rtsp://10.0.5.9/h264";
    private const string FailureWarning =
        "Reconciler failed to repair MediaMTX path cam-0197a1b2-0000-7000-8000-000000000b01; the next sweep retries.";
    private const string StartupSummary =
        "log Information MediaMtxReconciler startup pass complete. Configured=0, expected=0, removed=0, readded=0.";

    private static readonly TimeSpan SweepBound = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await aspire.ResetStreamDistributionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_sweep_warns_once_for_a_patch_mediamtx_keeps_refusing()
    {
        await SeedAsync();
        RefusingGateway gateway = new();
        SynchronizedLogger<MediaMtxSourceRepairSweep> logger = new();
        using var provider = await BuildProviderAsync(gateway);
        using MediaMtxSourceRepairSweep sweep = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StartedLifetime(),
            Options.Create(new MediaMtxSourceRepairOptions { Interval = TimeSpan.FromMilliseconds(50) }),
            TimeProvider.System,
            logger);

        await sweep.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => gateway.Patches >= 4 && logger.Count(PathText) >= 4);
        await sweep.StopAsync(CancellationToken.None);

        var entries = logger.Snapshot();
        entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message)
            .ShouldBe([FailureWarning]);
        entries.Count(entry => entry.Level == LogLevel.Debug && entry.Message.Contains(PathText, StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_failed_repair_list_at_startup_is_logged_as_its_own_failure_after_the_summary()
    {
        List<string> events = [];
        ListFailingGateway gateway = new(new HttpRequestException("list-boom"));
        using var provider = await BuildProviderAsync(gateway);
        var reconciler = Reconciler(provider, events);

        await Should.NotThrowAsync(() => reconciler.ReconcileOnceAsync(CancellationToken.None));

        events.TakeLast(2).ShouldBe(
        [
            StartupSummary,
            "log Warning MediaMtxReconciler source repair after the startup pass failed; the periodic sweep retries. [HttpRequestException: list-boom]",
        ]);
    }

    [Fact]
    public async Task A_shutdown_during_the_startup_repair_still_propagates()
    {
        List<string> events = [];
        using CancellationTokenSource shutdown = new();
        ListFailingGateway gateway = new(new OperationCanceledException(shutdown.Token))
        {
            BeforeThrow = shutdown.Cancel,
        };
        using var provider = await BuildProviderAsync(gateway);
        var reconciler = Reconciler(provider, events);

        await Should.ThrowAsync<OperationCanceledException>(() => reconciler.ReconcileOnceAsync(shutdown.Token));

        events.ShouldNotContain(entry => entry.StartsWith("log Warning", StringComparison.Ordinal));
    }

    private static string PathText => $"cam-{Camera}";

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + SweepBound;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("the sweep did not reach the expected number of passes in time");
            }

            await Task.Delay(10);
        }
    }

    private static MediaMtxReconciler Reconciler(ServiceProvider provider, List<string> events) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StartedLifetime(),
            new EventLogger(events));

    private async Task SeedAsync()
    {
        await using var context = await aspire.CreateStreamDistributionDbContextAsync();
        var operatorIdentifier = OperatorIdentifier.From(
            Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));
        TestClock clock = new(DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture));

        var stream = Stream.Provision(
            FabIdentifier.From("munich"),
            CameraIdentifier.From(Guid.Parse(Camera, CultureInfo.InvariantCulture)),
            StreamSourceUrl.From(PersistedSource),
            operatorIdentifier,
            clock);

        context.Streams.Add(stream);
        await context.SaveChangesAsync();
    }

    private async Task<ServiceProvider> BuildProviderAsync(IRtspGateway gateway)
    {
        var connection = await aspire.App
            .GetConnectionStringAsync(AspireFixture.StreamDistributionConnectionName);
        if (connection is null)
        {
            throw new InvalidOperationException("missing connection string");
        }

        ServiceCollection services = new();
        services.AddDbContextFactory<StreamDistributionDbContext>(options => options.UseNpgsql(connection));
        services.AddSingleton(gateway);

        return services.BuildServiceProvider();
    }

    private sealed class RefusingGateway : IRtspGateway
    {
        private int patches;

        public int Patches => Volatile.Read(ref patches);

        private static MediaMtxPath PathOf() =>
            MediaMtxPath.For(CameraIdentifier.From(Guid.Parse(Camera, CultureInfo.InvariantCulture)));

        public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<MediaMtxPath, string> sources = new Dictionary<MediaMtxPath, string>
            {
                [PathOf()] = ReportedSource,
            };

            return Task.FromResult(sources);
        }

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref patches);

            throw new HttpRequestException(
                "Response status code does not indicate success: 400 (Bad Request).",
                null,
                HttpStatusCode.BadRequest);
        }

        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The source repair never adds a path.");

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The source repair never removes a path.");

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("The sweep never lists the paths alone.");

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The source repair never probes a path.");

        public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The source repair never probes a path.");
    }

    private sealed class ListFailingGateway(Exception failure) : IRtspGateway
    {
        public Action? BeforeThrow { get; init; }

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<MediaMtxPath> none = [];

            return Task.FromResult(none);
        }

        public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
            CancellationToken cancellationToken)
        {
            BeforeThrow?.Invoke();

            throw failure;
        }

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Nothing is diverged when the list cannot be read.");

        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Nothing is seeded, so nothing is re-added.");

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Nothing is configured, so nothing is removed.");

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The reconcile pass never probes a path.");

        public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The reconcile pass never probes a path.");
    }

    private sealed class SynchronizedLogger<T> : ILogger<T>
    {
        private readonly object gate = new();
        private readonly List<(LogLevel Level, string Message)> entries = [];

        public List<(LogLevel Level, string Message)> Snapshot()
        {
            lock (gate)
            {
                return [.. entries];
            }
        }

        public int Count(string containing)
        {
            lock (gate)
            {
                return entries.Count(entry => entry.Message.Contains(containing, StringComparison.Ordinal));
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (gate)
            {
                entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class EventLogger(List<string> events) : ILogger<MediaMtxReconciler>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var rendered = formatter(state, exception);
            events.Add(exception is null
                ? $"log {logLevel} {rendered}"
                : $"log {logLevel} {rendered} [{exception.GetType().Name}: {exception.Message}]");
        }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => new(canceled: true);

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
