using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

public sealed partial class AspireFixture
{
    public const string CameraCatalogConnectionName = "camera-catalog-db";
    public const string StreamDistributionConnectionName = "stream-distribution-db";
    public const string LayoutCompositionConnectionName = "layout-composition-db";
    public const string OverlayDesignerConnectionName = "overlay-designer-db";
    public const string AuditObservabilityConnectionName = "audit-db";
    public const string EventIngestionConnectionName = "event-ingestion-db";

    public async Task<EventIngestionDbContext> CreateEventIngestionDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        string? connectionString = await App
            .GetConnectionStringAsync(EventIngestionConnectionName, cancellationToken)
            .ConfigureAwait(false);

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Connection string '{EventIngestionConnectionName}' was not provisioned by Aspire.");
        }

        DbContextOptionsBuilder<EventIngestionDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new EventIngestionDbContext(optionsBuilder.Options);
    }

    public async Task<AuditObservabilityDbContext> CreateAuditObservabilityDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        string? connectionString = await App
            .GetConnectionStringAsync(AuditObservabilityConnectionName, cancellationToken)
            .ConfigureAwait(false);

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Connection string '{AuditObservabilityConnectionName}' was not provisioned by Aspire.");
        }

        DbContextOptionsBuilder<AuditObservabilityDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new AuditObservabilityDbContext(optionsBuilder.Options);
    }

    public async Task<CameraCatalogDbContext> CreateCameraCatalogDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        string? connectionString = await App
            .GetConnectionStringAsync(CameraCatalogConnectionName, cancellationToken)
            .ConfigureAwait(false);

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Connection string '{CameraCatalogConnectionName}' was not provisioned by Aspire.");
        }

        DbContextOptionsBuilder<CameraCatalogDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new CameraCatalogDbContext(optionsBuilder.Options);
    }

    public async Task ResetCameraCatalogAsync(CancellationToken cancellationToken = default)
    {
        await using CameraCatalogDbContext context =
            await CreateCameraCatalogDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Cameras.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StreamDistributionDbContext> CreateStreamDistributionDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        string? connectionString = await App
            .GetConnectionStringAsync(StreamDistributionConnectionName, cancellationToken)
            .ConfigureAwait(false);

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Connection string '{StreamDistributionConnectionName}' was not provisioned by Aspire.");
        }

        DbContextOptionsBuilder<StreamDistributionDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new StreamDistributionDbContext(optionsBuilder.Options);
    }

    public async Task ResetStreamDistributionAsync(CancellationToken cancellationToken = default)
    {
        await using StreamDistributionDbContext context =
            await CreateStreamDistributionDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Streams.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LayoutCompositionDbContext> CreateLayoutCompositionDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        string? connectionString = await App
            .GetConnectionStringAsync(LayoutCompositionConnectionName, cancellationToken)
            .ConfigureAwait(false);

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Connection string '{LayoutCompositionConnectionName}' was not provisioned by Aspire.");
        }

        DbContextOptionsBuilder<LayoutCompositionDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new LayoutCompositionDbContext(optionsBuilder.Options);
    }

    public async Task ResetLayoutCompositionAsync(CancellationToken cancellationToken = default)
    {
        await using LayoutCompositionDbContext context =
            await CreateLayoutCompositionDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Layouts.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OverlayDesignerDbContext> CreateOverlayDesignerDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        string? connectionString = await App
            .GetConnectionStringAsync(OverlayDesignerConnectionName, cancellationToken)
            .ConfigureAwait(false);

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Connection string '{OverlayDesignerConnectionName}' was not provisioned by Aspire.");
        }

        DbContextOptionsBuilder<OverlayDesignerDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new OverlayDesignerDbContext(optionsBuilder.Options);
    }

    public async Task ResetOverlayDesignerAsync(CancellationToken cancellationToken = default)
    {
        await using OverlayDesignerDbContext context =
            await CreateOverlayDesignerDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Overlays.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wipes MediaMTX of every path StreamDistribution had registered, so
    /// the next test starts with an empty SFU. The mediamtx HTTP API
    /// returns a paged list under <c>items</c>.
    /// </summary>
    public async Task ResetMediaMtxAsync(CancellationToken cancellationToken = default)
    {
        using HttpClient client = App.CreateHttpClient("mediamtx", "api");
        for (int page = 0; page < 16; page++)
        {
            using HttpResponseMessage list = await SendMediaMtxWithRetryAsync(
                () => client.GetAsync("/v3/config/paths/list", cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (!list.IsSuccessStatusCode)
            {
                return;
            }

            System.Text.Json.JsonElement payload = await list.Content
                .ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken).ConfigureAwait(false);
            if (!payload.TryGetProperty("items", out System.Text.Json.JsonElement items))
            {
                return;
            }

            int removed = 0;
            foreach (System.Text.Json.JsonElement item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out System.Text.Json.JsonElement name))
                {
                    continue;
                }

                string? pathName = name.GetString();
                if (string.IsNullOrEmpty(pathName))
                {
                    continue;
                }

                using HttpResponseMessage del = await SendMediaMtxWithRetryAsync(
                    () => client.DeleteAsync($"/v3/config/paths/delete/{pathName}", cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                removed++;
            }
            if (removed == 0)
            {
                return;
            }
        }
    }

    // CI-only flake (#964): requests to the MediaMTX API intermittently hit a
    // transient connection error on the Linux runner even though the SFU is up
    // (its boot probe in WaitForMediaMtxAsync already passed). Retry a few times
    // so the per-test reset doesn't take out the run.
    private static async Task<HttpResponseMessage> SendMediaMtxWithRetryAsync(
        Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        HttpRequestException? last = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                return await send().ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException(
            "MediaMTX API was unreachable after 10 attempts during the per-test reset.", last);
    }
}
