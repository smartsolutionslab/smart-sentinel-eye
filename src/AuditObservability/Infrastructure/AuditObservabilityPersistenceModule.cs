using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.AuditObservability.Infrastructure;

public static class AuditObservabilityPersistenceModule
{
    public const string DatabaseConnectionName = "audit-db";

    public static IHostApplicationBuilder AddAuditObservabilityPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is required.");

        // Register the factory only (singleton factory + singleton options).
        // The background retention worker needs IDbContextFactory; repositories,
        // query handlers, and the Wolverine outbox need a scoped DbContext.
        // We get the scoped instance *from* the factory rather than also calling
        // AddDbContext: a second AddDbContext registration would add a SCOPED
        // IDbContextOptionsConfiguration<AuditObservabilityDbContext>, which the
        // singleton factory then cannot resolve from the root provider — that
        // crashes the MigrationRunner (it resolves IMigrator from root) and, via
        // WaitForCompletion(migrations), gates every API service into
        // FailedToStart.
        builder.Services.AddDbContextFactory<AuditObservabilityDbContext>(options =>
            options.UseNpgsql(connectionString));
        builder.Services.AddScoped<AuditObservabilityDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<AuditObservabilityDbContext>>()
                .CreateDbContext());

        builder.Services.AddSingleton<IMigrator, AuditObservabilityMigrator>();

        return builder;
    }
}
