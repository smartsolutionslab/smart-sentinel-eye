using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

public sealed partial class AspireFixture
{
    public const string CameraCatalogConnectionName = "camera-catalog-db";

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
}
