using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SmartSentinelEye.AuditObservability.Infrastructure;
using SmartSentinelEye.Automation.Infrastructure;
using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.EventIngestion.Infrastructure;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.OverlayDesigner.Infrastructure;
using SmartSentinelEye.StreamDistribution.Infrastructure;
using SmartSentinelEye.SystemVariables.Infrastructure;

namespace SmartSentinelEye.MigrationRunner.Tests;

/// <summary>
/// Spec 282 (#1140), T013 / plan §4. Characterisation, captured green on
/// unmodified <c>develop</c> before T030, and required to stay green
/// <b>unmodified</b> after (constitution §Testing).
///
/// <para>
/// Mirrors <c>src/MigrationRunner/Program.cs</c>'s nine
/// <c>Add{Context}Persistence</c> calls — the persistence path only, not
/// <c>Add{Context}Infrastructure</c>. That is the whole point: the
/// enrichment helper T030 adds (<c>PostgresTelemetry.EnrichPostgresDbContext</c>)
/// is called from each context's <c>AddXInfrastructure</c>, never from
/// <c>AddXPersistence</c> (ADR-0067, plan §4), so <c>MigrationRunner</c> —
/// which composes only the persistence path, runs its migrations, and exits —
/// must gain no tracing, no metrics, no health check and no retry strategy
/// from this spec. A future change that reaches the enrichment from the
/// persistence path breaks this test rather than silently changing what a
/// process that runs to completion and exits does.
/// </para>
///
/// <para>
/// <c>AddKeycloakAdminClient</c> and the notice-logging interceptor are left
/// out deliberately: they are not part of any context's persistence
/// registration (the same reasoning <c>KioskPrivilegeSweepRegistrationTests</c>
/// applies to <c>AddKeycloakAdminClient</c> alone), and including them here
/// would couple this test to config shapes this spec does not touch.
/// </para>
/// </summary>
public class PersistenceCompositionCharacterisationTests
{
    [Fact]
    public void MigrationRunners_persistence_composition_registers_no_health_check_tracer_or_meter_provider()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:camera-catalog-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:stream-distribution-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:layout-composition-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:overlay-designer-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:system-variables-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:event-ingestion-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:automation-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:identity-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:audit-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
        });

        // Exactly the nine calls MigrationRunner/Program.cs makes, in the same
        // order, and nothing else — no AddServiceDefaults, no
        // AddPostgresNoticeLogging, no AddKeycloakAdminClient.
        builder.AddCameraCatalogPersistence();
        builder.AddStreamDistributionPersistence();
        builder.AddLayoutCompositionPersistence();
        builder.AddOverlayDesignerPersistence();
        builder.AddSystemVariablesPersistence();
        builder.AddEventIngestionPersistence();
        builder.AddAutomationPersistence();
        builder.AddIdentityPersistence();
        builder.AddAuditObservabilityPersistence();

        using ServiceProvider provider = builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        HealthCheckServiceOptions? healthCheckOptions =
            provider.GetService<IOptions<HealthCheckServiceOptions>>()?.Value;
        (healthCheckOptions?.Registrations ?? []).ShouldBeEmpty(
            "the nine AddXPersistence calls MigrationRunner makes must register no health check. "
            + "A health check reaching Postgres is added by AddWolverineForContext / the "
            + "enrichment helper, and neither is reachable from the persistence path (ADR-0067).");

        provider.GetService<TracerProvider>().ShouldBeNull(
            "MigrationRunner's persistence composition must register no TracerProvider; Npgsql "
            + "tracing is wired only from AddXInfrastructure (plan §4), which MigrationRunner "
            + "never calls.");

        provider.GetService<MeterProvider>().ShouldBeNull(
            "MigrationRunner's persistence composition must register no MeterProvider; Npgsql "
            + "metrics are wired only from AddXInfrastructure (plan §4), which MigrationRunner "
            + "never calls.");
    }
}
