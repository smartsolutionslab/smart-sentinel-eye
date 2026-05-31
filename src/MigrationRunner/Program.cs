using SmartSentinelEye.AuditObservability.Infrastructure;
using SmartSentinelEye.Automation.Infrastructure;
using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.EventIngestion.Infrastructure;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.OverlayDesigner.Infrastructure;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.StreamDistribution.Infrastructure;
using SmartSentinelEye.SystemVariables.Infrastructure;

// MigrationRunner orchestrates all bounded-context database migrations and exits (ADR-0067).
// Each IMigrator runs sequentially before any Api service starts.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddCameraCatalogPersistence();
builder.AddStreamDistributionPersistence();
builder.AddLayoutCompositionPersistence();
builder.AddOverlayDesignerPersistence();
builder.AddSystemVariablesPersistence();
builder.AddEventIngestionPersistence();
builder.AddAutomationPersistence();
builder.AddIdentityPersistence();
builder.AddAuditObservabilityPersistence();

IHost host = builder.Build();
ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

await host.StartAsync().ConfigureAwait(false);

IEnumerable<IMigrator> migrators = host.Services.GetServices<IMigrator>();
foreach (IMigrator migrator in migrators)
{
    logger.LogInformation("Running migrations for {Context}.", migrator.ContextName);
    await migrator.RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping)
        .ConfigureAwait(false);
}

logger.LogInformation("All migrations applied; MigrationRunner exiting.");
await host.StopAsync().ConfigureAwait(false);
