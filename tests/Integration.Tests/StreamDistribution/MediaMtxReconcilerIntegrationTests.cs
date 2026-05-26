using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        await _aspire.ResetMediaMtxAsync();
        await _aspire.ResetStreamDistributionAsync();
        await _aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reconciler_removes_orphan_paths_and_preserves_paths_with_a_DB_stream()
    {
        using HttpClient mediaMtx = _aspire.App.CreateHttpClient("mediamtx", "api");

        Guid keptCamera = Guid.CreateVersion7();
        Guid orphanCamera = Guid.CreateVersion7();
        MediaMtxPath keptPath = MediaMtxPath.For(CameraIdentifier.From(keptCamera));
        MediaMtxPath orphanPath = MediaMtxPath.For(CameraIdentifier.From(orphanCamera));

        await AddMediaMtxPathAsync(mediaMtx, keptPath, "rtsp://10.0.7.1/h264");
        await AddMediaMtxPathAsync(mediaMtx, orphanPath, "rtsp://10.0.7.2/h264");
        await using (StreamDistributionDbContext context =
            await _aspire.CreateStreamDistributionDbContextAsync())
        {
            Stream stream = Stream.Provision(
                CameraIdentifier.From(keptCamera),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                new TestClock(DateTimeOffset.UtcNow));
            context.Streams.Add(stream);
            await context.SaveChangesAsync();
        }

        MediaMtxReconciler reconciler = await BuildReconcilerAsync();
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        IReadOnlyList<string> remaining = await ListMediaMtxPathNamesAsync(mediaMtx);
        remaining.ShouldContain(keptPath.Value);
        remaining.ShouldNotContain(orphanPath.Value);
    }

    private async Task<MediaMtxReconciler> BuildReconcilerAsync()
    {
        string? connection = await _aspire.App
            .GetConnectionStringAsync(AspireFixture.StreamDistributionConnectionName);
        if (connection is null) throw new InvalidOperationException("missing connection string");

        string? mediaMtxUrl = _aspire.App.GetEndpoint("mediamtx", "api").ToString();
        if (string.IsNullOrEmpty(mediaMtxUrl)) throw new InvalidOperationException("missing mediamtx api endpoint");

        ServiceCollection services = new();
        services.AddDbContextFactory<StreamDistributionDbContext>(opts => opts.UseNpgsql(connection));
        services.AddHttpClient<IRtspGateway, MediaMtxRtspGateway>(client =>
        {
            client.BaseAddress = new Uri(mediaMtxUrl);
        });
        services.AddSingleton<MediaMtxReconciler>();

        ServiceProvider provider = services.BuildServiceProvider();
        return new MediaMtxReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MediaMtxReconciler>.Instance);
    }

    private static async Task AddMediaMtxPathAsync(HttpClient client, MediaMtxPath path, string source)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/v3/config/paths/add/{path.Value}", new { source });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<string>> ListMediaMtxPathNamesAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/v3/config/paths/list");
        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!payload.TryGetProperty("items", out JsonElement items)) return Array.Empty<string>();
        return items.EnumerateArray()
            .Select(item => item.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
