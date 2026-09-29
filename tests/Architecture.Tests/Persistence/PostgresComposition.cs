using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.AuditObservability.Infrastructure;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Automation.Infrastructure;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.EventIngestion.Infrastructure;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.OverlayDesigner.Infrastructure;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.SystemVariables.Infrastructure;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 282 (#1140), plan §5.2. The one composition harness every Postgres
/// characterisation/red test in this directory shares, following the
/// <c>IngestVolumeRegistrationTests</c>/<c>KioskPrivilegeSweepRegistrationTests</c>
/// precedent: <c>Host.CreateEmptyApplicationBuilder</c>, an in-memory
/// configuration holding only the keys each context's
/// <c>Add{Context}Infrastructure</c> resolves, with syntactically valid,
/// never-dialled values, then <c>BuildServiceProvider(validateScopes: true)</c>.
/// The host is never started, so no hosted service runs and no connection
/// opens.
///
/// <para>
/// <b>The cost, stated.</b> A config-shape change in any of the nine modules
/// breaks the matching case here loudly. That is the trade for one shared
/// harness instead of nine near-identical copies (plan §5.2).
/// </para>
/// </summary>
public static class PostgresComposition
{
    /// <summary>
    /// The nine contexts, named the way each module's own
    /// <c>ContextName</c>/<c>DatabaseConnectionName</c> constant spells them.
    /// A plain string array rather than an enum so xUnit's <c>TheoryData</c>
    /// serialises each case for test discovery, unlike a record carrying
    /// delegates.
    /// </summary>
    public static readonly string[] ContextNames =
    [
        "camera-catalog",
        "identity",
        "automation",
        "event-ingestion",
        "audit-observability",
        "layout-composition",
        "overlay-designer",
        "stream-distribution",
        "system-variables",
    ];

    public static TheoryData<string> AllContexts()
    {
        TheoryData<string> data = [];
        foreach (string name in ContextNames)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>
    /// Composes <c>Add{Context}Infrastructure</c> for <paramref name="contextName"/>
    /// against an in-memory configuration and returns the built container. The
    /// caller disposes it.
    /// </summary>
    public static ServiceProvider Compose(string contextName) => Compose(contextName, configureMore: null);

    /// <summary>
    /// The same composition, with an extra hook run after
    /// <c>Add{Context}Infrastructure</c> and before the container is built —
    /// e.g. to append a capturing metric reader the way
    /// <c>IngestVolumeRegistrationTests</c> does, without duplicating the
    /// per-context configuration for every caller that needs one.
    /// </summary>
    public static ServiceProvider Compose(string contextName, Action<IHostApplicationBuilder>? configureMore)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(ConfigurationFor(contextName));
        AddInfrastructure(contextName, builder);
        configureMore?.Invoke(builder);

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// Creates the context's own <c>DbContext</c> from its
    /// <c>IDbContextFactory&lt;T&gt;</c>, returned as the common base type so
    /// callers stay generic over the nine concrete contexts.
    /// </summary>
    public static DbContext ResolveDbContext(string contextName, IServiceProvider provider) => contextName switch
    {
        "camera-catalog" => provider.GetRequiredService<IDbContextFactory<CameraCatalogDbContext>>().CreateDbContext(),
        "identity" => provider.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext(),
        "automation" => provider.GetRequiredService<IDbContextFactory<AutomationDbContext>>().CreateDbContext(),
        "event-ingestion" => provider.GetRequiredService<IDbContextFactory<EventIngestionDbContext>>().CreateDbContext(),
        "audit-observability" => provider.GetRequiredService<IDbContextFactory<AuditObservabilityDbContext>>().CreateDbContext(),
        "layout-composition" => provider.GetRequiredService<IDbContextFactory<LayoutCompositionDbContext>>().CreateDbContext(),
        "overlay-designer" => provider.GetRequiredService<IDbContextFactory<OverlayDesignerDbContext>>().CreateDbContext(),
        "stream-distribution" => provider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>().CreateDbContext(),
        "system-variables" => provider.GetRequiredService<IDbContextFactory<SystemVariablesDbContext>>().CreateDbContext(),
        _ => throw new ArgumentOutOfRangeException(nameof(contextName), contextName, "Unknown context name."),
    };

    /// <summary>
    /// The DbContext CLR type for <paramref name="contextName"/>, used by the
    /// health-check guard (T012) to recognise Aspire's own <c>{TContext}</c>
    /// check by name.
    /// </summary>
    public static Type DbContextTypeFor(string contextName) => contextName switch
    {
        "camera-catalog" => typeof(CameraCatalogDbContext),
        "identity" => typeof(IdentityDbContext),
        "automation" => typeof(AutomationDbContext),
        "event-ingestion" => typeof(EventIngestionDbContext),
        "audit-observability" => typeof(AuditObservabilityDbContext),
        "layout-composition" => typeof(LayoutCompositionDbContext),
        "overlay-designer" => typeof(OverlayDesignerDbContext),
        "stream-distribution" => typeof(StreamDistributionDbContext),
        "system-variables" => typeof(SystemVariablesDbContext),
        _ => throw new ArgumentOutOfRangeException(nameof(contextName), contextName, "Unknown context name."),
    };

    private static void AddInfrastructure(string contextName, IHostApplicationBuilder builder)
    {
        switch (contextName)
        {
            case "camera-catalog":
                builder.AddCameraCatalogInfrastructure();
                break;
            case "identity":
                builder.AddIdentityInfrastructure();
                break;
            case "automation":
                builder.AddAutomationInfrastructure();
                break;
            case "event-ingestion":
                builder.AddEventIngestionInfrastructure();
                break;
            case "audit-observability":
                builder.AddAuditObservabilityInfrastructure();
                break;
            case "layout-composition":
                builder.AddLayoutCompositionInfrastructure();
                break;
            case "overlay-designer":
                builder.AddOverlayDesignerInfrastructure();
                break;
            case "stream-distribution":
                builder.AddStreamDistributionInfrastructure();
                break;
            case "system-variables":
                builder.AddSystemVariablesInfrastructure();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(contextName), contextName, "Unknown context name.");
        }
    }

    /// <summary>
    /// The database connection name each context's persistence module reads
    /// (its own <c>DatabaseConnectionName</c> constant), named here rather than
    /// reflected out of the assembly: the value is a fact about the module
    /// under test, not something this harness should derive from it.
    /// </summary>
    private static string DatabaseConnectionNameFor(string contextName) => contextName switch
    {
        "camera-catalog" => "camera-catalog-db",
        "identity" => "identity-db",
        "automation" => "automation-db",
        "event-ingestion" => "event-ingestion-db",
        "audit-observability" => "audit-db",
        "layout-composition" => "layout-composition-db",
        "overlay-designer" => "overlay-designer-db",
        "stream-distribution" => "stream-distribution-db",
        "system-variables" => "system-variables-db",
        _ => throw new ArgumentOutOfRangeException(nameof(contextName), contextName, "Unknown context name."),
    };

    /// <summary>
    /// The keys each context's <c>Add{Context}Infrastructure</c> resolves
    /// before it returns. Values are syntactically valid and never dialled:
    /// the module parses them, it does not connect (mirrors
    /// <c>IngestVolumeRegistrationTests.Configuration</c> and
    /// <c>KioskPrivilegeSweepRegistrationTests</c>).
    /// </summary>
    private static Dictionary<string, string?> ConfigurationFor(string contextName)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            [$"ConnectionStrings:{DatabaseConnectionNameFor(contextName)}"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
        };

        // Identity, EventIngestion, StreamDistribution and SystemVariables each
        // resolve ConnectionStrings:keycloak while composing (three of them
        // throw if it is absent; SystemVariables' reverse-index seeder falls
        // back to string.Empty but is given a real-shaped value anyway, so this
        // harness does not depend on that fallback).
        if (contextName is "identity" or "event-ingestion" or "stream-distribution" or "system-variables")
        {
            configuration["ConnectionStrings:keycloak"] = "http://127.0.0.1:8080";
        }

        if (contextName == "stream-distribution")
        {
            configuration["ConnectionStrings:mediamtx-api"] = "http://127.0.0.1:9997";
            configuration["ConnectionStrings:mediamtx-whep"] = "http://127.0.0.1:8889";
        }

        if (contextName == "event-ingestion")
        {
            configuration["Mosquitto:Endpoint"] = "127.0.0.1:1883";
        }

        if (contextName == "audit-observability")
        {
            configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        }

        return configuration;
    }
}
