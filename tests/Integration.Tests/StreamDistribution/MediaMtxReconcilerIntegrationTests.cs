using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 002 T085 — MediaMtxReconciler one-shot startup pass.
/// Verifies the reconciler removes MediaMTX paths that no longer back a
/// Stream aggregate (the "stream deleted while MediaMTX was down" case)
/// without disturbing paths that do correspond to a DB stream.
/// </summary>
[Collection(AspireCollection.Name)]
public class MediaMtxReconcilerIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reconciler_removes_orphan_paths_and_preserves_paths_with_a_DB_stream()
    {
        using var mediaMtx = aspire.CreateMediaMtxApiClient();

        var keptCamera = Guid.CreateVersion7();
        var orphanCamera = Guid.CreateVersion7();
        var keptPath = MediaMtxPath.For(CameraIdentifier.From(keptCamera));
        var orphanPath = MediaMtxPath.For(CameraIdentifier.From(orphanCamera));

        await AddMediaMtxPathAsync(mediaMtx, keptPath, "rtsp://10.0.7.1/h264");
        await AddMediaMtxPathAsync(mediaMtx, orphanPath, "rtsp://10.0.7.2/h264");
        await using (var context =
            await aspire.CreateStreamDistributionDbContextAsync())
        {
            var stream = Stream.Provision(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(keptCamera),
                StreamSourceUrl.From("rtsp://10.0.7.1/h264"),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                new TestClock(DateTimeOffset.UtcNow));
            context.Streams.Add(stream);
            await context.SaveChangesAsync();
        }

        var reconciler = await BuildReconcilerAsync();
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        var remaining = await ListMediaMtxPathNamesAsync(mediaMtx);
        remaining.ShouldContain(keptPath.Value);
        remaining.ShouldNotContain(orphanPath.Value);
    }

    /// <summary>
    /// #197 — the complementary half. A Stream whose MediaMTX path is missing
    /// (the "MediaMTX restarted and lost its runtime config" case) must be
    /// re-created from the SourceUrl persisted on the aggregate. Before that URL
    /// was persisted the reconciler knew the path name but not its source, so
    /// every stream stayed 404 until a CameraRegistered redelivery — which never
    /// fires for a camera that already exists.
    /// </summary>
    [Fact]
    public async Task Reconciler_re_adds_a_missing_path_from_the_persisted_source_url()
    {
        using var mediaMtx = aspire.CreateMediaMtxApiClient();

        var camera = Guid.CreateVersion7();
        var missingPath = MediaMtxPath.For(CameraIdentifier.From(camera));
        const string source = "rtsp://10.0.9.9/h264";

        // Stream row exists; MediaMTX knows nothing about it.
        await using (var context =
            await aspire.CreateStreamDistributionDbContextAsync())
        {
            var stream = Stream.Provision(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(camera),
                StreamSourceUrl.From(source),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                new TestClock(DateTimeOffset.UtcNow));
            context.Streams.Add(stream);
            await context.SaveChangesAsync();
        }

        (await ListMediaMtxPathNamesAsync(mediaMtx)).ShouldNotContain(missingPath.Value);

        var reconciler = await BuildReconcilerAsync();
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        var configured = await ListMediaMtxPathNamesAsync(mediaMtx);
        configured.ShouldContain(missingPath.Value);
    }

    /// <summary>
    /// <c>ProvisionStreamCommandHandlerTests
    /// .Provision_for_a_retired_stream_does_not_re_register_its_path</c>
    /// documents the invariant that re-registering a Retired stream's path
    /// "would resurrect a path nobody wants reachable". The startup pass's
    /// re-add half must honour the same rule as provisioning does, or every
    /// MediaMTX restart undoes it for every retired camera in the DB.
    /// </summary>
    [Fact]
    public async Task Reconciler_does_not_re_add_a_retired_stream_s_path()
    {
        using var mediaMtx = aspire.CreateMediaMtxApiClient();

        var liveCamera = Guid.CreateVersion7();
        var retiredCamera = Guid.CreateVersion7();
        var livePath = MediaMtxPath.For(CameraIdentifier.From(liveCamera));
        var retiredPath = MediaMtxPath.For(CameraIdentifier.From(retiredCamera));

        // Neither path is configured in MediaMTX — the "MediaMTX restarted
        // and lost its runtime config" case the re-add half exists for.
        await using (var context =
            await aspire.CreateStreamDistributionDbContextAsync())
        {
            var live = Stream.Provision(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(liveCamera),
                StreamSourceUrl.From("rtsp://10.0.9.10/h264"),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                new TestClock(DateTimeOffset.UtcNow));

            var retired = Stream.Provision(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(retiredCamera),
                StreamSourceUrl.From("rtsp://10.0.9.11/h264"),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                new TestClock(DateTimeOffset.UtcNow));
            retired.Retire(new TestClock(DateTimeOffset.UtcNow));

            context.Streams.AddRange(live, retired);
            await context.SaveChangesAsync();
        }

        var reconciler = await BuildReconcilerAsync();
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        var configured = await ListMediaMtxPathNamesAsync(mediaMtx);
        configured.ShouldContain(livePath.Value);
        configured.ShouldNotContain(
            retiredPath.Value,
            "re-registering a Retired stream's path would resurrect a path nobody wants reachable");
    }

    /// <summary>
    /// The orphan-sweep half must not special-case a Retired stream whose
    /// path is still configured in MediaMTX — e.g. because its own
    /// RemovePathAsync call never completed. Excluding Retired rows from the
    /// "expected" set (the fix for the re-add half above) also drops them out
    /// of this half's protection, so a leftover Retired path is swept away
    /// like any other orphan rather than being kept alive because its DB row
    /// still exists.
    /// </summary>
    [Fact]
    public async Task Reconciler_removes_a_retired_stream_s_leftover_path_as_an_orphan()
    {
        using var mediaMtx = aspire.CreateMediaMtxApiClient();

        var retiredCamera = Guid.CreateVersion7();
        var retiredPath = MediaMtxPath.For(CameraIdentifier.From(retiredCamera));

        // The path is still configured in MediaMTX — the stream's own
        // RemovePathAsync never completed, so the row is Retired but
        // MediaMTX was never told.
        await AddMediaMtxPathAsync(mediaMtx, retiredPath, "rtsp://10.0.9.12/h264");

        await using (var context =
            await aspire.CreateStreamDistributionDbContextAsync())
        {
            var retired = Stream.Provision(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(retiredCamera),
                StreamSourceUrl.From("rtsp://10.0.9.12/h264"),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                new TestClock(DateTimeOffset.UtcNow));
            retired.Retire(new TestClock(DateTimeOffset.UtcNow));

            context.Streams.Add(retired);
            await context.SaveChangesAsync();
        }

        var reconciler = await BuildReconcilerAsync();
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        var configured = await ListMediaMtxPathNamesAsync(mediaMtx);
        configured.ShouldNotContain(
            retiredPath.Value,
            "a Retired stream's leftover path must be swept as an orphan, not kept alive because its row still exists");
    }

    /// <summary>
    /// #2960 — a retried PATCH can land after a newer repoint and leave the
    /// path pulling an address the database no longer records. The pass puts
    /// the database's value back.
    /// </summary>
    [Fact]
    public async Task Reconciler_repoints_a_path_whose_source_differs_from_the_persisted_source()
    {
        using var mediaMtx = aspire.CreateMediaMtxApiClient();

        var camera = Guid.CreateVersion7();
        var path = MediaMtxPath.For(CameraIdentifier.From(camera));
        const string persisted = "rtsp://10.0.8.1/h264";

        await AddMediaMtxPathAsync(mediaMtx, path, "rtsp://10.0.8.2/h264");
        await PersistStreamAsync(camera, persisted);

        var reconciler = await BuildReconcilerAsync();
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        (await ReadConfiguredSourceAsync(mediaMtx, path)).ShouldBe(persisted);
    }

    [Fact]
    public async Task Reconciler_leaves_a_path_whose_source_matches_the_persisted_source()
    {
        using var mediaMtx = aspire.CreateMediaMtxApiClient();

        var camera = Guid.CreateVersion7();
        var path = MediaMtxPath.For(CameraIdentifier.From(camera));
        const string persisted = "rtsp://10.0.8.3/h264";

        await AddMediaMtxPathAsync(mediaMtx, path, persisted);
        await PersistStreamAsync(camera, persisted);
        var before = await ReadConfiguredPathAsync(mediaMtx, path);

        var reconciler = await BuildReconcilerAsync();
        await reconciler.ReconcileOnceAsync(CancellationToken.None);
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        var after = await ReadConfiguredPathAsync(mediaMtx, path);
        after.GetProperty("source").GetString().ShouldBe(persisted);
        after.GetRawText().ShouldBe(before.GetRawText());
    }

    private async Task PersistStreamAsync(Guid camera, string source)
    {
        await using var context = await aspire.CreateStreamDistributionDbContextAsync();
        var stream = Stream.Provision(
            FabIdentifier.From("munich"),
            CameraIdentifier.From(camera),
            StreamSourceUrl.From(source),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new TestClock(DateTimeOffset.UtcNow));
        context.Streams.Add(stream);
        await context.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadConfiguredPathAsync(HttpClient client, MediaMtxPath path)
    {
        var response = await client.GetAsync($"/v3/config/paths/get/{path.Value}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> ReadConfiguredSourceAsync(HttpClient client, MediaMtxPath path)
    {
        var configured = await ReadConfiguredPathAsync(client, path);
        return configured.GetProperty("source").GetString();
    }

    private async Task<MediaMtxReconciler> BuildReconcilerAsync()
    {
        var connection = await aspire.App
            .GetConnectionStringAsync(AspireFixture.StreamDistributionConnectionName);
        if (connection is null)
        {
            throw new InvalidOperationException("missing connection string");
        }

        var mediaMtxUrl = aspire.App.GetEndpoint("mediamtx", "api").ToString();
        if (string.IsNullOrEmpty(mediaMtxUrl))
        {
            throw new InvalidOperationException("missing mediamtx api endpoint");
        }

        ServiceCollection services = new();
        services.AddDbContextFactory<StreamDistributionDbContext>(opts => opts.UseNpgsql(connection));
        services.AddHttpClient<IRtspGateway, MediaMtxRtspGateway>(client =>
        {
            client.BaseAddress = new Uri(mediaMtxUrl);
            // ADR-0171: every control-API call now authorizes through the
            // shared secret, forwarded as Basic auth exactly as production's
            // own gateway client presents it.
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"stream-distribution:{aspire.MediaMtxApiSecret}")));
        });
        services.AddSingleton<MediaMtxReconciler>();

        var provider = services.BuildServiceProvider();

        // This harness only ever calls ReconcileOnceAsync directly, never
        // StartAsync, so the lifetime the fix added is never actually
        // consulted here — a no-op stands in rather than a real host's.
        return new MediaMtxReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new NoOpHostApplicationLifetime(),
            NullLogger<MediaMtxReconciler>.Instance);
    }

    /// <summary>
    /// <see cref="MediaMtxReconciler"/> only reads this to defer its reconcile
    /// pass past <c>ApplicationStarted</c> (the fix for the startup-ordering
    /// bug ADR-0171 exposed) and to bound it with <c>ApplicationStopping</c>.
    /// Neither is exercised by <see cref="BuildReconcilerAsync"/>'s direct
    /// <c>ReconcileOnceAsync</c> calls, so this never needs to actually fire.
    /// </summary>
    private sealed class NoOpHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private static async Task AddMediaMtxPathAsync(HttpClient client, MediaMtxPath path, string source)
    {
        var response = await client.PostAsJsonAsync(
            $"/v3/config/paths/add/{path.Value}", new { source });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<string>> ListMediaMtxPathNamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/v3/config/paths/list");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!payload.TryGetProperty("items", out var items))
        {
            return Array.Empty<string>();
        }

        return items.EnumerateArray()
            .Select(item => item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
