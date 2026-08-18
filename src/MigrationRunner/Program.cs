using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;
using SmartSentinelEye.AuditObservability.Infrastructure;
using SmartSentinelEye.Automation.Infrastructure;
using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.EventIngestion.Infrastructure;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.MigrationRunner;
using SmartSentinelEye.OverlayDesigner.Infrastructure;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.StreamDistribution.Infrastructure;
using SmartSentinelEye.SystemVariables.Infrastructure;

// MigrationRunner orchestrates all bounded-context database migrations and exits (ADR-0067).
// Each IMigrator runs sequentially before any Api service starts.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddSingleton<PostgresNoticeLoggingInterceptor>();

builder.AddCameraCatalogPersistence();
builder.AddStreamDistributionPersistence();
builder.AddLayoutCompositionPersistence();
builder.AddOverlayDesignerPersistence();
builder.AddSystemVariablesPersistence();
builder.AddEventIngestionPersistence();
builder.AddAutomationPersistence();
builder.AddIdentityPersistence();
builder.AddAuditObservabilityPersistence();

// Applied per context type: EF reads IDbContextOptionsConfiguration<T> after
// the options delegate each Add<Context>Persistence supplies, which is the
// only hook that reaches those options without editing all nine modules — and
// keeps this to MigrationRunner rather than every service (#1394).
builder.AddPostgresNoticeLogging<CameraCatalogDbContext>();
builder.AddPostgresNoticeLogging<StreamDistributionDbContext>();
builder.AddPostgresNoticeLogging<LayoutCompositionDbContext>();
builder.AddPostgresNoticeLogging<OverlayDesignerDbContext>();
builder.AddPostgresNoticeLogging<SystemVariablesDbContext>();
builder.AddPostgresNoticeLogging<EventIngestionDbContext>();
builder.AddPostgresNoticeLogging<AutomationDbContext>();
builder.AddPostgresNoticeLogging<IdentityDbContext>();
builder.AddPostgresNoticeLogging<AuditObservabilityDbContext>();

// Spec 019: event partitions are provisioned per fab, and the fabs come from
// the realm's group tree. Identity owns Keycloak and EventIngestion may not
// reference it, so the two meet here — in the composition root, which is
// allowed to know both — and nowhere else.
builder.AddKeycloakAdminClient();
builder.Services.AddScoped<IProvisionedFabSource, KeycloakProvisionedFabSource>();

// Registered last so it runs after every context's EF migrations, including
// EventIngestion's own: the rollover needs the parent `events` table to exist.
builder.Services.AddScoped<IMigrator, EventPartitionRolloverMigrator>();

IHost host = builder.Build();
ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

await host.StartAsync();

// Resolved from a scope rather than the root provider. Every migrator was a
// singleton until spec 019 added one that depends on the Keycloak admin client,
// which is scoped — and resolving a scoped service from the root throws under
// the scope validation the Development environment turns on. That throw exits
// this process non-zero, and because all nine services WaitForCompletion on it,
// every one of them reports FailedToStart with nothing to say why.
await using AsyncServiceScope scope = host.Services.CreateAsyncScope();

IEnumerable<IMigrator> migrators = scope.ServiceProvider.GetServices<IMigrator>();
foreach (IMigrator migrator in migrators)
{
    logger.RunningMigrations(migrator.ContextName);
    await migrator.RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

logger.AllMigrationsApplied();
await host.StopAsync();
