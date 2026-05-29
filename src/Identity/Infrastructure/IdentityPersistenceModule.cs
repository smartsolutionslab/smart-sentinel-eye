using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Identity.Infrastructure;

public static class IdentityPersistenceModule
{
    public const string DatabaseConnectionName = "identity-db";

    public static IHostApplicationBuilder AddIdentityPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, IdentityMigrator>();

        return builder;
    }
}
