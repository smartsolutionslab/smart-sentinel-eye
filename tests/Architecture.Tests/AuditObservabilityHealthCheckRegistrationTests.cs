using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Infrastructure;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Issue #2680. <c>AuditObservabilityInfrastructureModule.AddAuditObservabilityInfrastructure</c>
/// calls <c>AddAzureBlobServiceClient("blobs")</c>, which auto-registers
/// <c>HealthChecks.Azure.Storage.Blobs.AzureBlobStorageHealthCheck</c> under the name
/// <c>"Azure_BlobServiceClient"</c>. <c>ServiceDefaults/Extensions.cs</c> maps <c>/health</c>
/// with no predicate, so every registered check is exposed there today — this one included,
/// which violates ADR-0154 clause 2 (a <c>/health</c> check must not name or reach an external
/// dependency).
///
/// <para>
/// This is a narrow, single-context guard, not the nine-context
/// <c>DependencyHealthCheckGuardTests</c> that spec 282 (#1140) is building on its own
/// unmerged branch — that guard found this exact finding while composing
/// <c>audit-observability</c> and filed it as this issue rather than fixing it, since #1140's
/// scope is Postgres/Npgsql only. This test exists so the fix here has its own red-first
/// evidence in this worktree, ahead of that guard landing separately.
/// </para>
/// </summary>
public class AuditObservabilityHealthCheckRegistrationTests
{
    private static ServiceProvider Compose()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);

        // Nothing here is dialled: AddAuditObservabilityInfrastructure only parses these
        // while registering (mirrors PostgresComposition's audit-observability case).
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            ["ConnectionStrings:audit-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true",
        });

        builder.AddAuditObservabilityInfrastructure();

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void No_registered_health_check_names_or_reaches_the_blob_store()
    {
        using ServiceProvider provider = Compose();
        using IServiceScope scope = provider.CreateScope();

        HealthCheckServiceOptions options = scope.ServiceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        List<string> offenders = [.. options.Registrations
            .Where(registration => registration.Name.Contains("blob", StringComparison.OrdinalIgnoreCase)
                || registration.Name.Contains("azure", StringComparison.OrdinalIgnoreCase)
                || registration.Factory(scope.ServiceProvider).GetType().Assembly.GetName().Name
                    is string assemblyName && assemblyName.StartsWith("HealthChecks.", StringComparison.Ordinal))
            .Select(registration => registration.Name)];

        offenders.ShouldBeEmpty(
            $"audit-observability registers a health check that names or reaches the Azure Blob "
            + $"store (ADR-0154 clause 2): {string.Join(", ", offenders)}. AddAzureBlobServiceClient "
            + "auto-registers AzureBlobStorageHealthCheck, and ServiceDefaults/Extensions.cs maps "
            + "/health with no predicate, so it is live there today. A /health check is a liveness "
            + "signal, not a readiness probe for shared infrastructure.");
    }
}
