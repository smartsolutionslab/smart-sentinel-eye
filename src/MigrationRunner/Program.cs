using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.StreamDistribution.Infrastructure;

// MigrationRunner orchestrates all bounded-context database migrations and exits (ADR-0067).
// Each IMigrator runs sequentially before any Api service starts.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddCameraCatalogPersistence();
builder.AddStreamDistributionPersistence();

IHost host = builder.Build();
ILogger<Program> log = host.Services.GetRequiredService<ILogger<Program>>();

await host.StartAsync().ConfigureAwait(false);

IEnumerable<IMigrator> migrators = host.Services.GetServices<IMigrator>();
foreach (IMigrator migrator in migrators)
{
    log.LogInformation("Running migrations for {Context}.", migrator.ContextName);
    await migrator.RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping)
        .ConfigureAwait(false);
}

log.LogInformation("All migrations applied; MigrationRunner exiting.");
await host.StopAsync().ConfigureAwait(false);
