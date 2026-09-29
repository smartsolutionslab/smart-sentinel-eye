using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.ServiceDefaults;

/// <summary>
/// Spec 282 (#1140), T022 / FR-008. US2, red: observed failing before T040,
/// green once T040 enables tracing in <c>PostgresTelemetry</c>. The one test
/// in this spec's coverage that runs a real query against a real Postgres
/// (ADR-0103 — the Aspire fixture, no Testcontainers), so it is the only one
/// that can show an actual span rather than a registration.
///
/// <para>
/// <b>Why this composes <c>AddCameraCatalogInfrastructure</c> in-process
/// rather than hitting the running CameraCatalog API.</b> Same reasoning as
/// <c>KioskPrivilegeSweepStartupIntegrationTests.DriveIdentityStartupAsync</c>:
/// the API's own container is not addressable from here, so the registration
/// extension is composed directly, against the fixture's real
/// <c>camera-catalog-db</c> connection string. The host is <b>never
/// started</b> — only <c>IDbContextFactory&lt;CameraCatalogDbContext&gt;</c>
/// is resolved and used, so Wolverine's RabbitMQ transport (configured with a
/// connection string that is parsed but never dialled) never connects.
/// </para>
///
/// <para>
/// <b>Why this is red today, and for the right reason.</b> Npgsql's own
/// instrumentation always calls <c>NpgsqlActivitySource.Source.StartActivity</c>
/// for every command, but that call returns a *sampled* activity only when
/// something has subscribed a listener to a source named <c>"Npgsql"</c>. No
/// context registers a <c>TracerProvider</c> today (see
/// <c>PostgresTracingRegistrationTests</c>), so this test supplies its own
/// <c>WithTracing(...)</c> call purely to make the (currently source-less)
/// <c>TracerProvider</c> resolvable and to attach the capturing exporter —
/// deliberately <b>not</b> an <c>AddSource("Npgsql")</c> call, which would
/// make this test pass regardless of whether the production wiring works.
/// Before T040, nothing has told that provider to listen to <c>"Npgsql"</c>,
/// so the query's activity is never sampled and the exporter captures
/// nothing: a missing registration, not a connectivity failure — the query
/// itself succeeds either way.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class PostgresSpanIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_camera_catalog_query_produces_a_postgres_span()
    {
        CancellationToken cancellationToken = CancellationToken.None;

        string? connectionString = await aspire.App
            .GetConnectionStringAsync(AspireFixture.CameraCatalogConnectionName, cancellationToken);
        connectionString.ShouldNotBeNull(
            $"Connection string '{AspireFixture.CameraCatalogConnectionName}' was not provisioned by Aspire.");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:camera-catalog-db"] = connectionString,
            // Parsed while AddCameraCatalogInfrastructure registers Wolverine's
            // RabbitMQ transport, and never dialled: the host below is never
            // started, so no listener or hosted service runs.
            ["ConnectionStrings:rabbitmq"] = "amqp://unused:unused@127.0.0.1:5672",
        });

        builder.AddCameraCatalogInfrastructure();

        CapturingExporter exporter = new();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));

        await using ServiceProvider provider = builder.Services
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using (IServiceScope scope = provider.CreateScope())
        {
            IDbContextFactory<CameraCatalogDbContext> factory =
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<CameraCatalogDbContext>>();
            using CameraCatalogDbContext dbContext = factory.CreateDbContext();

            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        }

        provider.GetRequiredService<TracerProvider>().ForceFlush();

        exporter.Captured.ShouldContain(
            activity => "postgresql".Equals(activity.GetTagItem("db.system") as string, StringComparison.Ordinal)
                || "postgresql".Equals(activity.GetTagItem("db.system.name") as string, StringComparison.Ordinal),
            "no captured activity carries db.system(.name) = postgresql. A camera-catalog request's "
            + "database time stays a gap in its trace instead of a span underneath it (FR-008).");
    }

    /// <summary>
    /// Records every activity the processor sees, mirroring
    /// <c>IngestVolumeRegistrationTests.CapturingExporter</c>'s shape for
    /// metrics.
    /// </summary>
    private sealed class CapturingExporter : BaseExporter<Activity>
    {
        public List<Activity> Captured { get; } = [];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (Activity activity in batch)
            {
                Captured.Add(activity);
            }

            return ExportResult.Success;
        }
    }
}
