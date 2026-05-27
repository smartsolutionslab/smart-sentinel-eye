using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
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

    public async Task<CameraCatalogDbContext> CreateCameraCatalogDbContextAsync()
    {
        string? connectionString = await App.GetConnectionStringAsync(CameraCatalogConnectionName)
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

    public async Task ResetCameraCatalogAsync()
    {
        await using CameraCatalogDbContext context = await CreateCameraCatalogDbContextAsync().ConfigureAwait(false);
        await context.Cameras.ExecuteDeleteAsync().ConfigureAwait(false);
    }

    public async Task<StreamDistributionDbContext> CreateStreamDistributionDbContextAsync()
    {
        string? connectionString = await App.GetConnectionStringAsync(StreamDistributionConnectionName)
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

    public async Task ResetStreamDistributionAsync()
    {
        await using StreamDistributionDbContext context =
            await CreateStreamDistributionDbContextAsync().ConfigureAwait(false);
        await context.Streams.ExecuteDeleteAsync().ConfigureAwait(false);
    }

    public async Task<LayoutCompositionDbContext> CreateLayoutCompositionDbContextAsync()
    {
        string? connectionString = await App.GetConnectionStringAsync(LayoutCompositionConnectionName)
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

    public async Task ResetLayoutCompositionAsync()
    {
        await using LayoutCompositionDbContext context =
            await CreateLayoutCompositionDbContextAsync().ConfigureAwait(false);
        await context.Layouts.ExecuteDeleteAsync().ConfigureAwait(false);
    }

    public async Task<OverlayDesignerDbContext> CreateOverlayDesignerDbContextAsync()
    {
        string? connectionString = await App.GetConnectionStringAsync(OverlayDesignerConnectionName)
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

    public async Task ResetOverlayDesignerAsync()
    {
        await using OverlayDesignerDbContext context =
            await CreateOverlayDesignerDbContextAsync().ConfigureAwait(false);
        await context.Overlays.ExecuteDeleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Wipes MediaMTX of every path StreamDistribution had registered, so
    /// the next test starts with an empty SFU. The mediamtx HTTP API
    /// returns a paged list under <c>items</c>.
    /// </summary>
    public async Task ResetMediaMtxAsync()
    {
        using HttpClient client = App.CreateHttpClient("mediamtx", "api");
        for (int page = 0; page < 16; page++)
        {
            using HttpResponseMessage list = await client.GetAsync("/v3/config/paths/list").ConfigureAwait(false);
            if (!list.IsSuccessStatusCode) return;
            System.Text.Json.JsonElement payload =
                await list.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>().ConfigureAwait(false);
            if (!payload.TryGetProperty("items", out System.Text.Json.JsonElement items)) return;

            int removed = 0;
            foreach (System.Text.Json.JsonElement item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out System.Text.Json.JsonElement name)) continue;
                string? pathName = name.GetString();
                if (string.IsNullOrEmpty(pathName)) continue;
                using HttpResponseMessage del = await client
                    .DeleteAsync($"/v3/config/paths/delete/{pathName}").ConfigureAwait(false);
                removed++;
            }
            if (removed == 0) return;
        }
    }
}
