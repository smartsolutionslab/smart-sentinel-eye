using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Characterisation (spec 351, ADR-0144), captured green on the unmodified
/// <c>MediaMtxReconciler</c> before <c>ReconcileOnceAsync</c> is split into a
/// removal pass and a re-add pass. Postgres is real (the Aspire fixture, so the
/// query, its value converters and the <c>Retired</c> filter run as in
/// production); MediaMTX is a recording stand-in, so the order of the gateway
/// calls and the text of every log line can be pinned, which the sibling
/// <see cref="MediaMtxReconcilerIntegrationTests"/> (real MediaMTX, outcomes
/// only) cannot express.
///
/// <para>
/// One seed, with every number distinct so a transposed count cannot pass:
/// three live streams and one retired; MediaMTX reports four configured paths
/// (two orphans, the kept live one, and the retired stream's leftover); removing
/// one orphan fails and re-adding one of the two missing live streams fails. The
/// summary line is therefore <c>Configured=4, expected=3, removed=2,
/// readded=1</c>: no two of the four numbers are equal or interchangeable with
/// the seed's other counts.
/// </para>
///
/// <para>
/// Counterfactual this file was seen to catch (on a throwaway copy of this
/// assertion body whose Postgres query was answered by an interceptor, because
/// the mutation runs were made where Docker was not running): incrementing
/// <c>removed</c> before <c>RemovePathAsync</c> turns the summary line
/// (<c>removed=3</c>) red; running the re-add loop before the removal loop turns
/// the ordered event list red.
/// </para>
///
/// <para>
/// The order inside the re-add pass is not pinned: the query has no
/// <c>ORDER BY</c>, so Postgres owes no order for it. The removal pass follows
/// the order MediaMTX lists its paths in, which the stand-in fixes.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class MediaMtxReconcilerPassCharacterisationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string Orphan1Camera = "0197a1b2-0000-7000-8000-000000000a01";
    private const string KeptCamera = "0197a1b2-0000-7000-8000-000000000a02";
    private const string Orphan2Camera = "0197a1b2-0000-7000-8000-000000000a03";
    private const string RetiredCamera = "0197a1b2-0000-7000-8000-000000000a04";
    private const string MissingGoodCamera = "0197a1b2-0000-7000-8000-000000000a05";
    private const string MissingBadCamera = "0197a1b2-0000-7000-8000-000000000a06";

    public async Task InitializeAsync()
    {
        await aspire.ResetStreamDistributionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_pass_removes_before_it_re_adds_logs_each_outcome_and_summarises_the_counts()
    {
        await SeedAsync();
        List<string> events = [];
        var reconciler = await BuildReconcilerAsync(events);

        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        string[] removalPhase =
        [
            "list",
            "remove cam-0197a1b2-0000-7000-8000-000000000a01",
            "log Information Reconciler removed orphan MediaMTX path cam-0197a1b2-0000-7000-8000-000000000a01.",
            "remove cam-0197a1b2-0000-7000-8000-000000000a03",
            "log Warning Reconciler failed to remove orphan path cam-0197a1b2-0000-7000-8000-000000000a03; will retry on next restart. [HttpRequestException: remove-boom-2]",
            "remove cam-0197a1b2-0000-7000-8000-000000000a04",
            "log Information Reconciler removed orphan MediaMTX path cam-0197a1b2-0000-7000-8000-000000000a04.",
        ];
        string[] readdPhase =
        [
            "add cam-0197a1b2-0000-7000-8000-000000000a05 rtsp://10.0.5.5/h264",
            "add cam-0197a1b2-0000-7000-8000-000000000a06 rtsp://10.0.5.6/h264",
            "log Information Reconciler re-added missing MediaMTX path cam-0197a1b2-0000-7000-8000-000000000a05 -> rtsp://10.0.5.5.",
            "log Warning Reconciler failed to re-add missing path cam-0197a1b2-0000-7000-8000-000000000a06; will retry on next restart. [HttpRequestException: add-boom-3]",
        ];
        const string summary =
            "log Information MediaMtxReconciler startup pass complete. Configured=4, expected=3, removed=2, readded=1.";

        events.Count.ShouldBe(removalPhase.Length + readdPhase.Length + 1);
        events.Take(removalPhase.Length).ShouldBe(removalPhase);
        events.Skip(removalPhase.Length).Take(readdPhase.Length).Order(StringComparer.Ordinal)
            .ShouldBe(readdPhase.Order(StringComparer.Ordinal));
        events[^1].ShouldBe(summary);
    }

    private async Task SeedAsync()
    {
        await using var context = await aspire.CreateStreamDistributionDbContextAsync();
        var operatorIdentifier = OperatorIdentifier.From(
            Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));
        TestClock clock = new(DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture));

        var kept = Provision(KeptCamera, "rtsp://10.0.5.2/h264", operatorIdentifier, clock);
        var missingGood = Provision(MissingGoodCamera, "rtsp://10.0.5.5/h264", operatorIdentifier, clock);
        var missingBad = Provision(MissingBadCamera, "rtsp://10.0.5.6/h264", operatorIdentifier, clock);
        var retired = Provision(RetiredCamera, "rtsp://10.0.5.4/h264", operatorIdentifier, clock);
        retired.Retire(clock);

        context.Streams.AddRange(kept, missingGood, missingBad, retired);
        await context.SaveChangesAsync();
    }

    private static Stream Provision(
        string camera,
        string source,
        OperatorIdentifier operatorIdentifier,
        IClock clock) =>
        Stream.Provision(
            FabIdentifier.From("munich"),
            CameraIdentifier.From(Guid.Parse(camera, CultureInfo.InvariantCulture)),
            StreamSourceUrl.From(source),
            operatorIdentifier,
            clock);

    private async Task<MediaMtxReconciler> BuildReconcilerAsync(List<string> events)
    {
        var connection = await aspire.App
            .GetConnectionStringAsync(AspireFixture.StreamDistributionConnectionName);
        if (connection is null)
        {
            throw new InvalidOperationException("missing connection string");
        }

        ServiceCollection services = new();
        services.AddDbContextFactory<StreamDistributionDbContext>(options => options.UseNpgsql(connection));
        services.AddSingleton<IRtspGateway>(new RecordingGateway(events));

        var provider = services.BuildServiceProvider();

        return new MediaMtxReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new NoOpHostApplicationLifetime(),
            new EventLogger(events));
    }

    private sealed class RecordingGateway(List<string> events) : IRtspGateway
    {
        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken)
        {
            events.Add("list");
            IReadOnlyList<MediaMtxPath> configured =
            [
                PathOf(Orphan1Camera),
                PathOf(KeptCamera),
                PathOf(Orphan2Camera),
                PathOf(RetiredCamera),
            ];
            return Task.FromResult(configured);
        }

        public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
            CancellationToken cancellationToken)
        {
            // Records no event: the kept path already points at its persisted source, so the
            // trailing repair pass finds nothing to do and the pass sequence stays as pinned.
            IReadOnlyDictionary<MediaMtxPath, string> sources = new Dictionary<MediaMtxPath, string>
            {
                [PathOf(KeptCamera)] = "rtsp://10.0.5.2/h264",
            };
            return Task.FromResult(sources);
        }

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken)
        {
            events.Add($"remove {path.Value}");

            if (path == PathOf(Orphan2Camera))
            {
                throw new HttpRequestException("remove-boom-2");
            }

            return Task.CompletedTask;
        }

        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
        {
            events.Add($"add {path.Value} {rtspSourceUrl}");

            if (path == PathOf(MissingBadCamera))
            {
                throw new HttpRequestException("add-boom-3");
            }

            return Task.CompletedTask;
        }

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The reconcile pass never repoints a path.");

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The reconcile pass never probes a path.");

        public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The reconcile pass never probes a path.");

        private static MediaMtxPath PathOf(string camera) =>
            MediaMtxPath.For(CameraIdentifier.From(Guid.Parse(camera, CultureInfo.InvariantCulture)));
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

    private sealed class NoOpHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

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
