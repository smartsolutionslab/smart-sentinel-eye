using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure;

public static class IdentityPersistenceModule
{
    public const string DatabaseConnectionName = "identity-db";

    public static IHostApplicationBuilder AddIdentityPersistence(this IHostApplicationBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString)
                .AddInterceptors(new AggregateVersionInterceptor()));

        builder.Services.AddSingleton<IMigrator, IdentityMigrator>();

        return builder;
    }
}
